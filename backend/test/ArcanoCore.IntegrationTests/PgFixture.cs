using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Services;
using ArcanoCore.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ArcanoCore.IntegrationTests;

// Real services against a real PostgreSQL. Each test class gets a fresh database arcano_it_<8 hex>
// (real EF migrations), dropped afterwards. Run (compose container arcanocore_postgres):
//   ARCANO_IT_PG='Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgrespassword' dotnet test backend/test/ArcanoCore.IntegrationTests
// Without ARCANO_IT_PG the tests are skipped.
public class RequiresPgFactAttribute : FactAttribute
{
    public RequiresPgFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARCANO_IT_PG")))
        {
            Skip = "Set ARCANO_IT_PG to a PostgreSQL admin connection string (see PgFixture.cs).";
        }
    }
}

public sealed class PgFixture : IDisposable
{
    private readonly string? _admin = Environment.GetEnvironmentVariable("ARCANO_IT_PG");
    private readonly string _dbName = "arcano_it_" + Guid.NewGuid().ToString("N")[..8];
    private readonly string? _testConnection;

    public PgFixture()
    {
        if (string.IsNullOrEmpty(_admin)) return; // fixtures are built even when every test is skipped

        _testConnection = new NpgsqlConnectionStringBuilder(_admin) { Database = _dbName }.ConnectionString;
        Exec($"CREATE DATABASE \"{_dbName}\"");
        using var db = NewContext();
        db.Database.Migrate();
    }

    public ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(_testConnection, b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
        .Options);

    // Owner FKs point at AspNetUsers, so every owner needs a user row.
    public Guid NewUser()
    {
        var id = Guid.NewGuid();
        using var db = NewContext();
        db.Users.Add(new IdentityUser<Guid> { Id = id, UserName = id.ToString(), NormalizedUserName = id.ToString().ToUpperInvariant() });
        db.SaveChanges();
        return id;
    }

    public void Dispose()
    {
        if (string.IsNullOrEmpty(_admin)) return;
        NpgsqlConnection.ClearAllPools();
        Exec($"DROP DATABASE IF EXISTS \"{_dbName}\" WITH (FORCE)");
    }

    private void Exec(string sql)
    {
        using var conn = new NpgsqlConnection(_admin);
        conn.Open();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }
}

public abstract class PgTestBase : IClassFixture<PgFixture>
{
    protected readonly PgFixture Pg;
    protected readonly MemoryStorage Storage = new();
    protected readonly Guid Owner;

    protected PgTestBase(PgFixture pg)
    {
        Pg = pg;
        Owner = pg.NewUser();
    }

    protected FileService Files(Guid? owner = null, long limit = StorageQuotaService.DefaultLimitBytes, ApplicationDbContext? db = null)
    {
        db ??= Pg.NewContext();
        return new FileService(db, Storage, new FakeUser(owner ?? Owner), new StorageQuotaService(db, limit));
    }

    protected FolderService Folders(Guid? owner = null) => new(Pg.NewContext(), new FakeUser(owner ?? Owner), Storage);

    protected ShareService Shares(Guid? owner = null) => new(Pg.NewContext(), Storage, new FakeUser(owner ?? Owner));

    protected static async Task<Guid> Upload(FileService svc, string name, int size = 10, Guid? folderId = null)
    {
        var r = await svc.UploadFileAsync(new(new MemoryStream(new byte[size]), name, "text/plain", size, folderId));
        Assert.True(r.IsSuccess, r.Error.Code);
        return r.Value.Id;
    }
}

public sealed class FakeUser(Guid id) : ICurrentUser
{
    public string? UserId => id.ToString();
    public bool IsAuthenticated => true;
}

public sealed class MemoryStorage : IStorageService
{
    private readonly Dictionary<string, byte[]> _objects = new();

    public async Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var key = Guid.NewGuid().ToString("N");
        _objects[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(_objects[key]));

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _objects.Remove(key);
        return Task.CompletedTask;
    }

    public bool Contains(string key) => _objects.ContainsKey(key);
}
