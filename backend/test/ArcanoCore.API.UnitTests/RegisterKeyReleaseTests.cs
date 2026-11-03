using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.API.Controllers;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ArcanoCore.API.UnitTests;

public class RegisterKeyReleaseTests
{
    [Fact]
    public async Task Register_WhenProfileSaveThrows_ReleasesTheJoinKey()
    {
        var failProfiles = new FailProfileSaves();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failProfiles).Options);
        db.UserProfiles.Add(new UserProfile { UserId = Guid.NewGuid(), IsAdmin = true });
        await db.SaveChangesAsync();
        var keys = new InviteKeyService(db);
        var (userManager, configuration) = AuthControllerTests.CreateUserManager();
        var controller = new AuthController(userManager, configuration, db, keys);
        var (key, plain) = await keys.CreateAsync(InviteKeyType.Join, null, null, 7, Guid.NewGuid());

        failProfiles.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Register(new RegisterRequest("b@example.com", "Passw0rd!", plain)));
        failProfiles.Enabled = false;

        Assert.Null((await db.InviteKeys.AsNoTracking().SingleAsync(k => k.Id == key.Id)).UsedAtUtc);
    }

    private sealed class FailProfileSaves : SaveChangesInterceptor
    {
        public bool Enabled;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Enabled && eventData.Context!.ChangeTracker.Entries<UserProfile>().Any(e => e.State == EntityState.Added)
                ? throw new InvalidOperationException("boom")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
