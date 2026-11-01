using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

// Read-only user-to-user sharing. Every recipient-side call re-resolves the share and re-checks both users,
// the item and its place in the shared subtree; any failure is a plain NotFound so nothing outside is revealed.
public class UserShareService : IUserShareService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserDirectory _users;
    private readonly IStorageService _storage;
    private readonly IFolderService _folders;

    public UserShareService(IApplicationDbContext db, ICurrentUser currentUser, IUserDirectory users, IStorageService storage, IFolderService folders)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _storage = storage;
        _folders = folders;
    }

    private static readonly Error UnauthorizedError = new("UserShare.Unauthorized", "A valid user ID is required.");
    private static readonly Error NotFoundError = new("UserShare.NotFound", "Share not found.");
    // Deliberately one code for self, unknown, disabled and malformed recipients.
    private static readonly Error InvalidRecipientError = new("UserShare.InvalidRecipient", "This item cannot be shared with that recipient.");
    private static readonly Error InvalidTargetError = new("UserShare.InvalidTarget", "Exactly one of fileId or folderId is required.");

    private Guid Me => Guid.TryParse(_currentUser.UserId, out var id) ? id : Guid.Empty;

    public async Task<Result<(UserShareDto Share, bool Created)>> CreateAsync(UserShareRequestDto dto, CancellationToken cancellationToken = default)
    {
        var me = Me;
        if (me == Guid.Empty) return Result<(UserShareDto, bool)>.Failure(UnauthorizedError);
        if ((dto.FileId == null) == (dto.FolderId == null)) return Result<(UserShareDto, bool)>.Failure(InvalidTargetError);

        var email = EmailNormalizer.Normalize(dto.Email);
        if (email.IsFailure) return Result<(UserShareDto, bool)>.Failure(InvalidRecipientError);
        var recipient = await _users.FindByEmailAsync(email.Value, cancellationToken);
        if (recipient == null || recipient.IsDisabled || recipient.Id == me) return Result<(UserShareDto, bool)>.Failure(InvalidRecipientError);

        var item = await FindOwnedItemAsync(me, dto.FileId, dto.FolderId, cancellationToken);
        if (item == null) return Result<(UserShareDto, bool)>.Failure(NotFoundError);

        var existing = await FindShareAsync(me, recipient.Id, dto.FileId, dto.FolderId, cancellationToken);
        var created = existing == null;
        if (existing == null)
        {
            existing = new UserShare { Id = Guid.NewGuid(), OwnerId = me, RecipientId = recipient.Id, FileId = dto.FileId, FolderId = dto.FolderId };
            _db.UserShares.Add(existing);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost a race with an identical request: the unique index kept one row, return it.
                _db.UserShares.Entry(existing).State = EntityState.Detached;
                existing = await FindShareAsync(me, recipient.Id, dto.FileId, dto.FolderId, cancellationToken);
                if (existing == null) throw;
                created = false;
            }
        }

        return Result<(UserShareDto, bool)>.Success((new UserShareDto(existing.Id, item.Value.Kind, item.Value.Id, item.Value.Name, recipient.Email, existing.CreatedAtUtc), created));
    }

    public async Task<Result<IReadOnlyList<UserShareRecipientDto>>> ListForItemAsync(Guid? fileId, Guid? folderId, CancellationToken cancellationToken = default)
    {
        var me = Me;
        if (me == Guid.Empty) return Result<IReadOnlyList<UserShareRecipientDto>>.Failure(UnauthorizedError);
        if ((fileId == null) == (folderId == null)) return Result<IReadOnlyList<UserShareRecipientDto>>.Failure(InvalidTargetError);
        if (await FindOwnedItemAsync(me, fileId, folderId, cancellationToken) == null) return Result<IReadOnlyList<UserShareRecipientDto>>.Failure(NotFoundError);

        var shares = await _db.UserShares.AsNoTracking()
            .Where(s => s.OwnerId == me && s.FileId == fileId && s.FolderId == folderId)
            .OrderBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var users = await _users.GetByIdsAsync(shares.Select(s => s.RecipientId), cancellationToken);

        IReadOnlyList<UserShareRecipientDto> list = shares
            .Where(s => users.ContainsKey(s.RecipientId))
            .Select(s => new UserShareRecipientDto(s.Id, users[s.RecipientId].Email, s.CreatedAtUtc))
            .ToList();
        return Result<IReadOnlyList<UserShareRecipientDto>>.Success(list);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var me = Me;
        if (me == Guid.Empty) return Result.Failure(UnauthorizedError);

        var share = await _db.UserShares.FirstOrDefaultAsync(s => s.Id == id && (s.OwnerId == me || s.RecipientId == me), cancellationToken);
        if (share == null) return Result.Failure(NotFoundError);

        _db.UserShares.Remove(share);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<IncomingShareDto>>> ListIncomingAsync(CancellationToken cancellationToken = default)
    {
        var me = Me;
        if (me == Guid.Empty) return Result<IReadOnlyList<IncomingShareDto>>.Failure(UnauthorizedError);

        var shares = await _db.UserShares.AsNoTracking().Where(s => s.RecipientId == me).OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
        var fileIds = shares.Where(s => s.FileId != null).Select(s => s.FileId!.Value).ToList();
        var folderIds = shares.Where(s => s.FolderId != null).Select(s => s.FolderId!.Value).ToList();
        // Query filters drop trashed items.
        var files = await _db.FileMetadatas.AsNoTracking().Where(f => fileIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var folders = await _db.Folders.AsNoTracking().Where(f => folderIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var owners = await _users.GetByIdsAsync(shares.Select(s => s.OwnerId), cancellationToken);

        var result = new List<IncomingShareDto>();
        foreach (var s in shares)
        {
            if (!owners.TryGetValue(s.OwnerId, out var owner) || owner.IsDisabled) continue;
            if (s.FileId is { } fid && files.TryGetValue(fid, out var file) && file.OwnerId == s.OwnerId)
                result.Add(new IncomingShareDto(s.Id, "file", file.Id, file.FileName, file.FileSize, file.ContentType, owner.Email, s.CreatedAtUtc));
            else if (s.FolderId is { } did && folders.TryGetValue(did, out var folder) && folder.OwnerId == s.OwnerId)
                result.Add(new IncomingShareDto(s.Id, "folder", folder.Id, folder.Name, null, null, owner.Email, s.CreatedAtUtc));
        }

        return Result<IReadOnlyList<IncomingShareDto>>.Success(result);
    }

    public async Task<Result<SharedChildrenDto>> GetChildrenAsync(Guid shareId, Guid? folderId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var share = await ResolveAsync(shareId, cancellationToken);
        if (share.IsFailure || share.Value.FolderId is not { } root) return Result<SharedChildrenDto>.Failure(share.IsFailure ? share.Error : NotFoundError);

        var target = folderId ?? root;
        var owner = share.Value.OwnerId;
        if (!await IsInsideAsync(root, owner, target, cancellationToken)) return Result<SharedChildrenDto>.Failure(NotFoundError);

        var folders = await _db.Folders.AsNoTracking()
            .Where(f => f.OwnerId == owner && f.ParentId == target)
            .OrderBy(f => f.Name)
            .Select(f => new SharedFolderDto(f.Id, f.Name))
            .ToListAsync(cancellationToken);
        var files = await _db.FileMetadatas.AsNoTracking()
            .Where(f => f.OwnerId == owner && f.FolderId == target)
            .OrderBy(f => f.FileName).ThenBy(f => f.Id)
            .Skip(skip).Take(take)
            .Select(f => new SharedFileDto(f.Id, f.FileName, f.FileSize, f.ContentType, f.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Result<SharedChildrenDto>.Success(new SharedChildrenDto(folders, files));
    }

    public async Task<Result<(Stream Content, string ContentType, string FileName)>> DownloadFileAsync(Guid shareId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var share = await ResolveAsync(shareId, cancellationToken);
        if (share.IsFailure) return Result<(Stream, string, string)>.Failure(share.Error);

        var file = await _db.FileMetadatas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && f.OwnerId == share.Value.OwnerId, cancellationToken);
        var allowed = file != null && (share.Value.FileId == fileId
            || (share.Value.FolderId is { } root && file.FolderId != null && await IsInsideAsync(root, file.OwnerId, file.FolderId, cancellationToken)));
        if (!allowed) return Result<(Stream, string, string)>.Failure(NotFoundError);

        try
        {
            return Result<(Stream, string, string)>.Success((await _storage.DownloadAsync(file!.StorageKey, cancellationToken), file.ContentType, file.FileName));
        }
        catch (Exception)
        {
            return Result<(Stream, string, string)>.Failure(new Error("UserShare.DownloadFailed", "Download failed. Please try again."));
        }
    }

    public async Task<Result<string>> GetFolderZipNameAsync(Guid shareId, Guid folderId, CancellationToken cancellationToken = default)
    {
        var folder = await AuthorizeFolderAsync(shareId, folderId, cancellationToken);
        return folder.IsFailure ? Result<string>.Failure(folder.Error) : Result<string>.Success(folder.Value.Name + ".zip");
    }

    public async Task<Result> WriteFolderZipAsync(Guid shareId, Guid folderId, Stream output, CancellationToken cancellationToken = default)
    {
        var folder = await AuthorizeFolderAsync(shareId, folderId, cancellationToken);
        return folder.IsFailure ? Result.Failure(folder.Error) : await _folders.WriteZipForOwnerAsync(folder.Value.OwnerId, folderId, output, cancellationToken);
    }

    private async Task<Result<Folder>> AuthorizeFolderAsync(Guid shareId, Guid folderId, CancellationToken cancellationToken)
    {
        var share = await ResolveAsync(shareId, cancellationToken);
        if (share.IsFailure || share.Value.FolderId is not { } root) return Result<Folder>.Failure(share.IsFailure ? share.Error : NotFoundError);
        if (!await IsInsideAsync(root, share.Value.OwnerId, folderId, cancellationToken)) return Result<Folder>.Failure(NotFoundError);

        var folder = await _db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == share.Value.OwnerId, cancellationToken);
        return folder == null ? Result<Folder>.Failure(NotFoundError) : Result<Folder>.Success(folder);
    }

    // The share must be addressed to the caller, and both ends must still exist and be enabled.
    private async Task<Result<UserShare>> ResolveAsync(Guid shareId, CancellationToken cancellationToken)
    {
        var me = Me;
        if (me == Guid.Empty) return Result<UserShare>.Failure(UnauthorizedError);

        var share = await _db.UserShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shareId && s.RecipientId == me, cancellationToken);
        if (share == null) return Result<UserShare>.Failure(NotFoundError);

        var users = await _users.GetByIdsAsync([share.OwnerId, share.RecipientId], cancellationToken);
        if (!users.TryGetValue(share.OwnerId, out var owner) || owner.IsDisabled
            || !users.TryGetValue(share.RecipientId, out var recipient) || recipient.IsDisabled)
        {
            return Result<UserShare>.Failure(NotFoundError);
        }

        return Result<UserShare>.Success(share);
    }

    // Walks up from folderId through live folders of the owner; true when it reaches rootId.
    private async Task<bool> IsInsideAsync(Guid rootId, Guid ownerId, Guid? folderId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        while (folderId is { } current && visited.Add(current))
        {
            var row = await _db.Folders.AsNoTracking()
                .Where(f => f.Id == current && f.OwnerId == ownerId)
                .Select(f => new { f.ParentId })
                .FirstOrDefaultAsync(cancellationToken);
            if (row == null) return false;
            if (current == rootId) return true;
            folderId = row.ParentId;
        }

        return false;
    }

    private async Task<(Guid Id, string Kind, string Name)?> FindOwnedItemAsync(Guid owner, Guid? fileId, Guid? folderId, CancellationToken cancellationToken)
    {
        if (fileId is { } fid)
        {
            var name = await _db.FileMetadatas.AsNoTracking().Where(f => f.Id == fid && f.OwnerId == owner).Select(f => f.FileName).FirstOrDefaultAsync(cancellationToken);
            return name == null ? null : (fid, "file", name);
        }

        var folderName = await _db.Folders.AsNoTracking().Where(f => f.Id == folderId && f.OwnerId == owner).Select(f => f.Name).FirstOrDefaultAsync(cancellationToken);
        return folderName == null ? null : (folderId!.Value, "folder", folderName);
    }

    private Task<UserShare?> FindShareAsync(Guid owner, Guid recipient, Guid? fileId, Guid? folderId, CancellationToken cancellationToken) =>
        _db.UserShares.FirstOrDefaultAsync(s => s.OwnerId == owner && s.RecipientId == recipient && s.FileId == fileId && s.FolderId == folderId, cancellationToken);
}
