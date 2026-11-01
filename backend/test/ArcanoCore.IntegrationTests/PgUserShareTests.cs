using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using ArcanoCore.Infrastructure.Data;
using ArcanoCore.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.IntegrationTests;

// User sharing on real PostgreSQL: partial unique indexes, check constraint, cascade, subtree walk, real user lookup.
public class PgUserShareTests(PgFixture pg) : PgTestBase(pg)
{
    private Guid UserWithEmail(string email)
    {
        var id = Guid.NewGuid();
        using var db = Pg.NewContext();
        db.Users.Add(new IdentityUser<Guid>
        {
            Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = new CustomLookupNormalizer().NormalizeEmail(email)
        });
        db.SaveChanges();
        return id;
    }

    private UserShareService Svc(Guid user)
    {
        var db = Pg.NewContext();
        var current = new FakeUser(user);
        return new UserShareService(db, current, new UserDirectory(db, new CustomLookupNormalizer()), Storage, new FolderService(db, current, Storage));
    }

    private static string Mail(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8] + "@example.com";

    [RequiresPgFact]
    public async Task UniqueIndexes_RejectDuplicates_ForFileAndFolderTargets_AndTheCheckConstraintHolds()
    {
        var recipient = Pg.NewUser();
        var file = await Upload(Files(), "a.txt");
        var folder = (await Folders().CreateFolderAsync(new("f", null))).Value;

        await using var db = Pg.NewContext();
        db.UserShares.AddRange(
            new UserShare { Id = Guid.NewGuid(), OwnerId = Owner, RecipientId = recipient, FileId = file },
            new UserShare { Id = Guid.NewGuid(), OwnerId = Owner, RecipientId = recipient, FolderId = folder.Id });
        await db.SaveChangesAsync();

        // Same recipient on a different item is fine; the same triple is not, for either kind.
        var other = await Upload(Files(), "b.txt");
        await using var ok = Pg.NewContext();
        ok.UserShares.Add(new UserShare { Id = Guid.NewGuid(), OwnerId = Owner, RecipientId = recipient, FileId = other });
        await ok.SaveChangesAsync();

        foreach (var dup in new[] { new UserShare { FileId = file }, new UserShare { FolderId = folder.Id } })
        {
            await using var d = Pg.NewContext();
            dup.Id = Guid.NewGuid(); dup.OwnerId = Owner; dup.RecipientId = recipient;
            d.UserShares.Add(dup);
            await Assert.ThrowsAsync<DbUpdateException>(() => d.SaveChangesAsync());
        }

        foreach (var bad in new[] { new UserShare(), new UserShare { FileId = file, FolderId = folder.Id } })
        {
            await using var d = Pg.NewContext();
            bad.Id = Guid.NewGuid(); bad.OwnerId = Owner; bad.RecipientId = Pg.NewUser();
            d.UserShares.Add(bad);
            await Assert.ThrowsAsync<DbUpdateException>(() => d.SaveChangesAsync());
        }
    }

    [RequiresPgFact]
    public async Task PurgingTheItem_CascadesTheShare_ButUserFkIsRestrict()
    {
        var recipient = Pg.NewUser();
        var file = await Upload(Files(), "a.txt");
        await using (var db = Pg.NewContext())
        {
            db.UserShares.Add(new UserShare { Id = Guid.NewGuid(), OwnerId = Owner, RecipientId = recipient, FileId = file });
            await db.SaveChangesAsync();
        }

        await using var del = Pg.NewContext();
        await Assert.ThrowsAnyAsync<Exception>(() => del.Users.Where(u => u.Id == recipient).ExecuteDeleteAsync());
        Assert.Equal(1, await del.UserShares.CountAsync(s => s.FileId == file));
        await del.FileMetadatas.IgnoreQueryFilters().Where(f => f.Id == file).ExecuteDeleteAsync();
        Assert.Equal(0, await del.UserShares.CountAsync(s => s.FileId == file));
    }

    [RequiresPgFact]
    public async Task EndToEnd_SubtreeWalk_EmailLookup_Revoke_AndAccountCleanup()
    {
        var owner = UserWithEmail(Mail("own"));
        var bobMail = Mail("bob");
        var bob = UserWithEmail(bobMail);
        var folders = Folders(owner);
        var root = (await folders.CreateFolderAsync(new("root", null))).Value;
        var sub = (await folders.CreateFolderAsync(new("sub", root.Id))).Value;
        var deep = (await folders.CreateFolderAsync(new("deep", sub.Id))).Value;
        var other = (await folders.CreateFolderAsync(new("other", null))).Value;
        var inDeep = await Upload(Files(owner), "d.txt", folderId: deep.Id);
        var inOther = await Upload(Files(owner), "o.txt", folderId: other.Id);

        // Email lookup is case-insensitive; unknown and self are rejected.
        var share = (await Svc(owner).CreateAsync(new(null, root.Id, bobMail.ToUpperInvariant()))).Value.Share;
        Assert.Equal("UserShare.InvalidRecipient", (await Svc(owner).CreateAsync(new(null, other.Id, "nobody@example.com"))).Error.Code);

        var bobSvc = Svc(bob);
        Assert.True((await bobSvc.DownloadFileAsync(share.Id, inDeep)).IsSuccess);
        Assert.Equal("UserShare.NotFound", (await bobSvc.DownloadFileAsync(share.Id, inOther)).Error.Code);
        Assert.Equal(["deep"], (await Svc(bob).GetChildrenAsync(share.Id, sub.Id, 0, 50)).Value.Folders.Select(f => f.Name));
        Assert.Equal("UserShare.NotFound", (await Svc(bob).GetChildrenAsync(share.Id, other.Id, 0, 50)).Error.Code);

        // Trashing the middle folder hides everything below it.
        Assert.True((await Folders(owner).DeleteFolderAsync(sub.Id)).IsSuccess);
        Assert.Equal("UserShare.NotFound", (await Svc(bob).DownloadFileAsync(share.Id, inDeep)).Error.Code);

        // Account deletion removes shares in both directions.
        var accounts = new AccountService(Pg.NewContext(), Storage, new NoParts());
        await accounts.DeleteUserDataAsync(bob);
        await using var check = Pg.NewContext();
        Assert.Empty(check.UserShares.Where(s => s.OwnerId == owner));
        await accounts.DeleteUserDataAsync(owner);
    }

    private sealed class NoParts : ArcanoCore.Application.Common.Interfaces.IMultipartStorage
    {
        public Task<(string Key, string UploadId)> InitiateAsync(string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UploadPartAsync(string key, string uploadId, int partNumber, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<int, long>> ListPartsAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompleteAsync(string key, string uploadId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
