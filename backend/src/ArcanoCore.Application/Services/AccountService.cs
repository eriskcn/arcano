using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

public class AccountService : IAccountService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IStorageService _storageService;
    private readonly IMultipartStorage _multipart;

    public AccountService(IApplicationDbContext dbContext, IStorageService storageService, IMultipartStorage multipart)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _multipart = multipart;
    }

    public async Task DeleteUserDataAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var files = await _dbContext.FileMetadatas.IgnoreQueryFilters().Where(f => f.OwnerId == ownerId).ToListAsync(cancellationToken);
        var fileIds = files.Select(f => f.Id).ToList();
        var links = await _dbContext.SharedLinks.IgnoreQueryFilters()
            .Where(l => l.CreatedById == ownerId || (l.FileMetadataId != null && fileIds.Contains(l.FileMetadataId.Value)))
            .ToListAsync(cancellationToken);
        var folders = await _dbContext.Folders.IgnoreQueryFilters().Where(f => f.OwnerId == ownerId).ToListAsync(cancellationToken);
        var sessions = await _dbContext.UploadSessions.Where(s => s.OwnerId == ownerId).ToListAsync(cancellationToken);

        _dbContext.SharedLinks.RemoveRange(links);
        _dbContext.FileMetadatas.RemoveRange(files);
        _dbContext.Folders.RemoveRange(folders);
        _dbContext.UploadSessions.RemoveRange(sessions);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Rows are gone, so a storage failure only orphans an object; do not fail the user.
        foreach (var file in files)
        {
            try { await _storageService.DeleteAsync(file.StorageKey, cancellationToken); } catch (Exception) { }
        }
        foreach (var session in sessions)
        {
            try { await _multipart.AbortAsync(session.StorageKey, session.StorageUploadId, cancellationToken); } catch (Exception) { }
        }
    }
}
