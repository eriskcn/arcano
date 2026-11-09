using ArcanoCore.API.Authorization;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Data;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ArcanoCore.IntegrationTests;

// Real ASP.NET Identity stores, real InviteKeyService and controllers on real PostgreSQL.
public class PgGroupModeTests(PgFixture pg) : IClassFixture<PgFixture>
{
    private const string Password = "Passw0rd!";

    private (AuthController Auth, AdminController Admin, UserManager<IdentityUser<Guid>> Users, ApplicationDbContext Db) Build(Guid? callerId = null)
    {
        var db = pg.NewContext();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(db);
        services.AddIdentityCore<IdentityUser<Guid>>(o => o.Password.RequiredLength = 8)
            .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.AddSingleton<ILookupNormalizer, CustomLookupNormalizer>();
        var users = services.BuildServiceProvider().GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "test-secret-key-that-is-at-least-32-bytes-long" }).Build();
        var keys = new InviteKeyService(db);
        var admin = new AdminController(users, db, new StorageQuotaService(db, 1000), new AccountService(db, new MemoryStorage(), new NoMultipart()), keys, new FakeUser(callerId ?? Guid.NewGuid()));
        return (new AuthController(users, config, db, keys), admin, users, db);
    }

    [RequiresPgFact]
    public async Task KeyRedeemFlow_FirstAdmin_ThenInvite_ThenReset_ThenDelete()
    {
        var (auth, _, users, db) = Build();
        // Other tests in this class may have added profiles; start from an empty install for the first-user rule.
        db.UserProfiles.RemoveRange(db.UserProfiles);
        await db.SaveChangesAsync();
        db.Users.RemoveRange(db.Users.Where(u => u.UserName!.Contains("@")));
        await db.SaveChangesAsync();
        Assert.False(await db.UserProfiles.AnyAsync());

        Assert.IsType<OkObjectResult>(await auth.Register(new RegisterRequest("admin@example.com", Password)));
        var admin = (await users.FindByEmailAsync("admin@example.com"))!;
        Assert.True((await db.UserProfiles.SingleAsync(p => p.UserId == admin.Id)).IsAdmin);

        // Without a key: refused. With a key: created with its quota; key is stored hashed and cannot be reused.
        Assert.Equal(403, Assert.IsType<ObjectResult>(await auth.Register(new RegisterRequest("m@example.com", Password))).StatusCode);
        AdminController Ctl() => Build(admin.Id).Admin; // fresh context per call, like one request
        var created = Assert.IsType<ObjectResult>(await Ctl().CreateKey(new CreateKeyRequest("Join", 42, null, 7), default));
        var plain = (string)created.Value!.GetType().GetProperty("Key")!.GetValue(created.Value)!;
        Assert.False(await db.InviteKeys.AnyAsync(k => k.KeyHash == plain));

        Assert.IsType<OkObjectResult>(await Build().Auth.Register(new RegisterRequest("m@example.com", Password, plain)));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Build().Auth.Register(new RegisterRequest("n@example.com", Password, plain))).StatusCode);
        var member = (await users.FindByEmailAsync("m@example.com"))!;
        var profile = await pg.NewContext().UserProfiles.SingleAsync(p => p.UserId == member.Id);
        Assert.Equal((false, 42L), (profile.IsAdmin, profile.QuotaBytes));

        // Admin user list (real Users query, usage, effective quota).
        var list = Assert.IsAssignableFrom<IEnumerable<AdminUserDto>>(Assert.IsType<OkObjectResult>(await Ctl().Users(default)).Value).ToList();
        Assert.Equal((42L, 42L), (list.Single(u => u.Email == "m@example.com").QuotaBytes, list.Single(u => u.Email == "m@example.com").EffectiveQuotaBytes));
        Assert.True(list.Single(u => u.Email == "admin@example.com").IsAdmin);

        // Disable / enable round-trips through PostgreSQL (LockoutEnd = max).
        Assert.IsType<NoContentResult>(await Ctl().Disable(member.Id));
        Assert.True((await Build().Users.FindByIdAsync(member.Id.ToString()))!.IsDisabled());
        Assert.IsType<UnauthorizedObjectResult>(await Build().Auth.Login(new LoginRequest("m@example.com", Password)));
        Assert.IsType<NoContentResult>(await Ctl().Enable(member.Id));
        Assert.IsType<OkObjectResult>(await Build().Auth.Login(new LoginRequest("m@example.com", Password)));

        // Reset key: new password works, old does not, stamp rotated, key single use.
        var stampBefore = (await Build().Users.FindByIdAsync(member.Id.ToString()))!.SecurityStamp;
        var reset = Assert.IsType<ObjectResult>(await Ctl().CreateKey(new CreateKeyRequest("Reset", null, member.Id, null), default));
        var resetKey = (string)reset.Value!.GetType().GetProperty("Key")!.GetValue(reset.Value)!;
        Assert.IsType<NoContentResult>(await Build().Auth.Reset(new ResetRequest(resetKey, "N3w-Passw0rd")));
        Assert.NotEqual(stampBefore, (await Build().Users.FindByIdAsync(member.Id.ToString()))!.SecurityStamp);
        Assert.IsType<UnauthorizedObjectResult>(await Build().Auth.Login(new LoginRequest("m@example.com", Password)));
        Assert.IsType<OkObjectResult>(await Build().Auth.Login(new LoginRequest("m@example.com", "N3w-Passw0rd")));
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Build().Auth.Reset(new ResetRequest(resetKey, "An0ther-Passw0rd"))).StatusCode);

        // Last admin is protected; deleting the member removes the profile row (cascade) and an open key targeting them.
        var keyForMember = Assert.IsType<ObjectResult>(await Ctl().CreateKey(new CreateKeyRequest("Reset", null, member.Id, null), default));
        Assert.IsType<ConflictObjectResult>(await Ctl().DeleteUser(admin.Id, default));
        Assert.IsType<NoContentResult>(await Ctl().DeleteUser(member.Id, default));
        await using var check = pg.NewContext();
        Assert.False(await check.UserProfiles.AnyAsync(p => p.UserId == member.Id));
        Assert.False(await check.InviteKeys.AnyAsync(k => k.TargetUserId == member.Id));
        Assert.NotNull(keyForMember);
    }

    [RequiresPgFact]
    public async Task ConcurrentRedeem_OfOneKey_SucceedsExactlyOnce()
    {
        var adminId = pg.NewUser();
        await using (var seed = pg.NewContext())
        {
            seed.UserProfiles.Add(new UserProfile { UserId = adminId, IsAdmin = true });
            await seed.SaveChangesAsync();
        }
        var plain = (await new InviteKeyService(pg.NewContext()).CreateAsync(InviteKeyType.Join, null, null, 7, adminId)).Plain;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => new InviteKeyService(pg.NewContext()).ConsumeAsync(plain, InviteKeyType.Join))));

        Assert.Equal(1, results.Count(r => r.IsSuccess));
    }

    [RequiresPgFact]
    public async Task DeleteUser_WhenTheUserRowChangesDuringThePurge_StillDeletesTheUser()
    {
        var (_, _, users, db) = Build();
        Assert.True((await users.CreateAsync(new IdentityUser<Guid> { UserName = "late@example.com", Email = "late@example.com" }, Password)).Succeeded);
        var member = (await users.FindByEmailAsync("late@example.com"))!;
        var admin = new AdminController(users, db, new StorageQuotaService(db, 1000), new ChangesUserAfterPurge(new AccountService(db, new MemoryStorage(), new NoMultipart()), pg, member.Id), new InviteKeyService(db), new FakeUser(Guid.NewGuid()));

        Assert.IsType<NoContentResult>(await admin.DeleteUser(member.Id, default));

        await using var check = pg.NewContext();
        Assert.False(await check.Users.AnyAsync(u => u.Id == member.Id));
    }

    // Stands in for a login or an admin action that touches the user row while the purge runs.
    private sealed class ChangesUserAfterPurge(IAccountService inner, PgFixture pg, Guid userId) : IAccountService
    {
        public async Task DeleteUserDataAsync(Guid ownerId, CancellationToken cancellationToken = default)
        {
            await inner.DeleteUserDataAsync(ownerId, cancellationToken);
            await using var other = pg.NewContext();
            await other.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.ConcurrencyStamp, Guid.NewGuid().ToString()), cancellationToken);
        }
    }

    [RequiresPgFact]
    public async Task Migration_BackfillsProfilesForExistingUsers_AsNonAdmin()
    {
        var admin = Environment.GetEnvironmentVariable("ARCANO_IT_PG")!;
        var name = "arcano_it_mig_" + Guid.NewGuid().ToString("N")[..8];
        var conn = new NpgsqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        Exec(admin, $"CREATE DATABASE \"{name}\"");
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(conn).Options);
            var migrations = db.Database.GetMigrations().ToList();
            var before = migrations[migrations.FindIndex(m => m.EndsWith("_GroupMode")) - 1];
            await db.GetInfrastructure().GetRequiredService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(before);
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO "AspNetUsers" ("Id","UserName","NormalizedUserName","Email","NormalizedEmail","EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount")
                VALUES ({id}, 'old@x.com','OLD@X.COM','old@x.com','OLD@X.COM', false, 'h','s','c', false, false, true, 0)
                """);

            await db.Database.MigrateAsync();

            var p = await db.UserProfiles.SingleAsync(x => x.UserId == id);
            Assert.False(p.IsAdmin);
            Assert.Null(p.QuotaBytes);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            Exec(admin, $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
        }
    }

    private static void Exec(string connection, string sql)
    {
        using var c = new NpgsqlConnection(connection);
        c.Open();
        using var cmd = new NpgsqlCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    private sealed class NoMultipart : IMultipartStorage
    {
        public Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
