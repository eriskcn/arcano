using ArcanoCore.Application.Common.Models;
using ArcanoCore.Application.Services;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ArcanoCore.IntegrationTests;

// Real services on real PostgreSQL; see PgFixture.cs for how to run.

public class PgFileListTests(PgFixture pg) : PgTestBase(pg)
{
    [RequiresPgFact]
    public async Task List_RootVsFolder()
    {
        var folder = (await Folders().CreateFolderAsync(new("docs", null))).Value.Id;
        var svc = Files();
        await Upload(svc, "root.txt");
        await Upload(svc, "inside.txt", folderId: folder);

        var root = (await Files().ListFilesAsync(0, 10)).Value;
        var inside = (await Files().ListFilesAsync(0, 10, folder)).Value;

        Assert.Equal(new[] { "root.txt" }, root.Select(f => f.FileName));
        Assert.Equal(new[] { "inside.txt" }, inside.Select(f => f.FileName));
    }

    [RequiresPgFact]
    public async Task Search_MixedCase_And_LikeWildcardsAreLiteral()
    {
        var svc = Files();
        foreach (var n in new[] { "Report_Final.PDF", "100%.txt", "a_b.txt", "axb.txt" }) await Upload(svc, n);

        async Task<string[]> Q(string q) => (await Files().ListFilesAsync(0, 10, query: q)).Value.Select(f => f.FileName).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Report_Final.PDF" }, await Q("rEpOrT"));
        Assert.Equal(new[] { "100%.txt" }, await Q("%"));
        Assert.Equal(new[] { "Report_Final.PDF", "a_b.txt" }, await Q("_"));
        Assert.Equal(new[] { "a_b.txt" }, await Q("a_b"));
    }

    [RequiresPgFact]
    public async Task Pagination_IsStableAndNewestFirst()
    {
        var svc = Files();
        for (var i = 0; i < 5; i++) await Upload(svc, $"f{i}.txt");

        var all = (await Files().ListFilesAsync(0, 10)).Value.Select(f => f.FileName).ToList();
        var paged = new List<string>();
        for (var skip = 0; skip < 5; skip += 2)
            paged.AddRange((await Files().ListFilesAsync(skip, 2)).Value.Select(f => f.FileName));

        Assert.Equal(new[] { "f4.txt", "f3.txt", "f2.txt", "f1.txt", "f0.txt" }, all);
        Assert.Equal(all, paged);
    }

    [RequiresPgFact]
    public async Task OtherOwnersFilesAreNotListed()
    {
        await Upload(Files(), "mine.txt");
        await Upload(Files(Pg.NewUser()), "theirs.txt");

        Assert.Equal(new[] { "mine.txt" }, (await Files().ListFilesAsync(0, 10)).Value.Select(f => f.FileName));
    }

    [RequiresPgFact]
    public async Task CreatedAtUtc_RoundTripsAsUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var id = await Upload(Files(), "t.txt");

        await using var db = Pg.NewContext();
        var file = await db.FileMetadatas.AsNoTracking().SingleAsync(f => f.Id == id);

        Assert.Equal(DateTimeKind.Utc, file.CreatedAtUtc.Kind);
        Assert.InRange(file.CreatedAtUtc, before, DateTime.UtcNow.AddSeconds(5));
    }
}

public class PgTrashTests(PgFixture pg) : PgTestBase(pg)
{
    private async Task<int> RowCount(Guid id)
    {
        await using var db = Pg.NewContext();
        return await db.FileMetadatas.IgnoreQueryFilters().CountAsync(f => f.Id == id);
    }

    [RequiresPgFact]
    public async Task Delete_HidesFromList_ShowsInTrash_RestoreBringsBack()
    {
        var id = await Upload(Files(), "a.txt");

        Assert.True((await Files().DeleteFileAsync(id)).IsSuccess);
        Assert.Empty((await Files().ListFilesAsync(0, 10)).Value);
        Assert.Equal(new[] { id }, (await Files().ListTrashAsync(0, 10)).Value.Select(f => f.Id));

        Assert.True((await Files().RestoreFileAsync(id)).IsSuccess);
        Assert.Equal(new[] { id }, (await Files().ListFilesAsync(0, 10)).Value.Select(f => f.Id));
        Assert.Empty((await Files().ListTrashAsync(0, 10)).Value);
    }

    [RequiresPgFact]
    public async Task PermanentDelete_RemovesRowAndObject()
    {
        var id = await Upload(Files(), "a.txt");
        string key;
        await using (var db = Pg.NewContext()) key = (await db.FileMetadatas.SingleAsync(f => f.Id == id)).StorageKey;
        await Files().DeleteFileAsync(id);

        Assert.True((await Files().PermanentlyDeleteFileAsync(id)).IsSuccess);

        Assert.Equal(0, await RowCount(id));
        Assert.False(Storage.Contains(key));
    }

    [RequiresPgFact]
    public async Task PermanentDelete_OfLiveFile_IsNotFound()
    {
        var id = await Upload(Files(), "a.txt");

        Assert.Equal("File.NotFound", (await Files().PermanentlyDeleteFileAsync(id)).Error.Code);
        Assert.Equal(1, await RowCount(id));
    }
}

public class PgShareTests(PgFixture pg) : PgTestBase(pg)
{
    private async Task<(Guid File, string Token, Guid LinkId)> Shared(DateTime? expires = null)
    {
        var file = await Upload(Files(), "s.txt", 5);
        var link = (await Shares().CreateLinkAsync(new(file, expires))).Value;
        return (file, link.Token, link.Id);
    }

    [RequiresPgFact]
    public async Task Link_Works()
    {
        var (_, token, _) = await Shared();

        var info = await Shares(Guid.NewGuid()).GetSharedFileInfoAsync(token);
        var open = await Shares(Guid.NewGuid()).OpenSharedFileAsync(token);

        Assert.Equal("s.txt", info.Value.FileName);
        Assert.Equal(5, open.Value.Content.Length);
    }

    [RequiresPgFact]
    public async Task Link_ToTrashedFile_IsNotFound_AndWorksAgainAfterRestore()
    {
        var (file, token, _) = await Shared();
        await Files().DeleteFileAsync(file);

        Assert.Equal("Share.NotFound", (await Shares().GetSharedFileInfoAsync(token)).Error.Code);
        Assert.Equal("Share.NotFound", (await Shares().OpenSharedFileAsync(token)).Error.Code);

        await Files().RestoreFileAsync(file);
        Assert.True((await Shares().GetSharedFileInfoAsync(token)).IsSuccess);
    }

    [RequiresPgFact]
    public async Task CreateLink_ForTrashedFile_IsNotFound()
    {
        var file = await Upload(Files(), "s.txt");
        await Files().DeleteFileAsync(file);

        Assert.Equal("File.NotFound", (await Shares().CreateLinkAsync(new(file, null))).Error.Code);
    }

    [RequiresPgFact]
    public async Task ExpiredLink_DoesNotWork()
    {
        var (_, token, linkId) = await Shared(DateTime.UtcNow.AddDays(1));
        await using (var db = Pg.NewContext())
        {
            var l = await db.SharedLinks.SingleAsync(x => x.Id == linkId);
            l.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal("Share.NotFound", (await Shares().GetSharedFileInfoAsync(token)).Error.Code);
    }

    [RequiresPgFact]
    public async Task RevokedLink_DoesNotWork()
    {
        var (file, token, linkId) = await Shared();

        Assert.True((await Shares().RevokeLinkAsync(linkId)).IsSuccess);

        Assert.Equal("Share.NotFound", (await Shares().GetSharedFileInfoAsync(token)).Error.Code);
        Assert.Empty((await Shares().ListLinksAsync(file)).Value);
    }

    [RequiresPgFact]
    public async Task RemovingFileRow_CascadesToSharedLinks()
    {
        var (file, _, linkId) = await Shared();

        await using (var db = Pg.NewContext())
        {
            db.FileMetadatas.Remove(await db.FileMetadatas.SingleAsync(f => f.Id == file));
            await db.SaveChangesAsync();
        }

        await using var check = Pg.NewContext();
        Assert.False(await check.SharedLinks.AnyAsync(l => l.Id == linkId));
    }
}

public class PgQuotaTests(PgFixture pg) : PgTestBase(pg)
{
    [RequiresPgFact]
    public async Task Usage_CountsLiveAndTrashed_ForOwnerOnly()
    {
        await Upload(Files(), "live.bin", 100);
        var trashed = await Upload(Files(), "trash.bin", 50);
        await Files().DeleteFileAsync(trashed);
        var other = Pg.NewUser();
        await Upload(Files(other), "other.bin", 1000);

        var quota = new StorageQuotaService(Pg.NewContext(), 200);

        Assert.Equal(150, (await quota.GetUsageAsync(Owner)).UsedBytes);
        Assert.Equal(1000, (await quota.GetUsageAsync(other)).UsedBytes);
        Assert.Equal(0, (await quota.GetUsageAsync(Pg.NewUser())).UsedBytes);
    }

    [RequiresPgFact]
    public async Task EnsureWithinQuota_EnforcesLimit_AndUploadIsRejected()
    {
        await Upload(Files(limit: 200), "a.bin", 150);
        var quota = new StorageQuotaService(Pg.NewContext(), 200);

        Assert.True((await quota.EnsureWithinQuotaAsync(Owner, 50)).IsSuccess);
        Assert.Equal("Storage.QuotaExceeded", (await quota.EnsureWithinQuotaAsync(Owner, 51)).Error.Code);

        var rejected = await Files(limit: 200).UploadFileAsync(new(new MemoryStream(new byte[51]), "b.bin", "text/plain", 51));
        Assert.Equal("Storage.QuotaExceeded", rejected.Error.Code);
    }
}

public class PgFolderTests(PgFixture pg) : PgTestBase(pg)
{
    private async Task<Guid> Make(string name, Guid? parent = null, Guid? owner = null)
    {
        var r = await Folders(owner).CreateFolderAsync(new(name, parent));
        Assert.True(r.IsSuccess, r.Error.Code);
        return r.Value.Id;
    }

    [RequiresPgFact]
    public async Task DuplicateName_SameParent_ReturnsServiceError_NotException()
    {
        var parent = await Make("p");
        await Make("a", parent);
        await Make("a"); // root level too: the index ignores NULL parents, the service checks

        Assert.Equal("Folder.NameTaken", (await Folders().CreateFolderAsync(new("a", parent))).Error.Code);
        Assert.Equal("Folder.NameTaken", (await Folders().CreateFolderAsync(new("a", null))).Error.Code);
        Assert.True((await Folders(Pg.NewUser()).CreateFolderAsync(new("a", null))).IsSuccess);
    }

    [RequiresPgFact]
    public async Task Rename_AndMove_CollisionsAreServiceErrors()
    {
        var p1 = await Make("p1");
        var p2 = await Make("p2");
        await Make("x", p1);
        var y = await Make("y", p2);
        var x2 = await Make("x", p2);

        // rename y -> x collides with x2 under p2; keeping own name is fine
        Assert.Equal("Folder.NameTaken", (await Folders().UpdateFolderAsync(y, new("x", p2))).Error.Code);
        Assert.True((await Folders().UpdateFolderAsync(y, new("y", p2))).IsSuccess);
        // move x2 into p1 where an "x" exists
        Assert.Equal("Folder.NameTaken", (await Folders().UpdateFolderAsync(x2, new("x", p1))).Error.Code);
        Assert.True((await Folders().UpdateFolderAsync(x2, new("z", p1))).IsSuccess);
    }

    [RequiresPgFact]
    public async Task Cycle_IsRejected()
    {
        var a = await Make("a");
        var b = await Make("b", a);
        var c = await Make("c", b);

        Assert.Equal("Folder.Cycle", (await Folders().UpdateFolderAsync(a, new("a", a))).Error.Code);
        Assert.Equal("Folder.Cycle", (await Folders().UpdateFolderAsync(a, new("a", c))).Error.Code);
        Assert.True((await Folders().UpdateFolderAsync(c, new("c", a))).IsSuccess);
    }

    [RequiresPgFact]
    public async Task UniqueIndex_IgnoresSoftDeletedFolders_AndRejectsLiveDuplicates()
    {
        var parent = await Make("p");
        var first = await Make("a", parent);

        // the index itself (bypassing the service) rejects a live duplicate
        await using (var db = Pg.NewContext())
        {
            db.Folders.Add(new Folder { Id = Guid.NewGuid(), Name = "a", OwnerId = Owner, ParentId = parent });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = Pg.NewContext())
        {
            (await db.Folders.SingleAsync(f => f.Id == first)).IsDeleted = true;
            await db.SaveChangesAsync();
        }
        Assert.True((await Folders().CreateFolderAsync(new("a", parent))).IsSuccess);
    }

    [RequiresPgFact]
    public async Task CreatedAtUtc_RoundTripsAsUtc()
    {
        var id = await Make("t");
        await using var db = Pg.NewContext();
        var f = await db.Folders.AsNoTracking().SingleAsync(x => x.Id == id);

        Assert.Equal(DateTimeKind.Utc, f.CreatedAtUtc.Kind);
    }
}
