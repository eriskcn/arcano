using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Application.Common.Models;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Services;

public class FolderService : IFolderService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUser _currentUser;

    public FolderService(IApplicationDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    // Guid.Empty when the current user ID is missing or not a valid GUID.
    private Guid CurrentOwnerId => Guid.TryParse(_currentUser.UserId, out var ownerId) ? ownerId : Guid.Empty;

    public async Task<Result<FolderDto>> CreateFolderAsync(FolderRequestDto dto, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<FolderDto>.Failure(new Error("Folder.Unauthorized", "A valid user ID is required."));
        }

        var name = NormalizeName(dto.Name);
        if (name == null)
        {
            return Result<FolderDto>.Failure(InvalidNameError);
        }

        var error = await ValidateLocationAsync(ownerId, name, dto.ParentId, null, cancellationToken);
        if (error != null)
        {
            return Result<FolderDto>.Failure(error);
        }

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = name,
            OwnerId = ownerId,
            ParentId = dto.ParentId
        };

        await _dbContext.Folders.AddAsync(folder, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<FolderDto>.Success(ToDto(folder));
    }

    public async Task<Result<IReadOnlyList<FolderDto>>> ListFoldersAsync(Guid? parentId, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<IReadOnlyList<FolderDto>>.Failure(new Error("Folder.Unauthorized", "A valid user ID is required."));
        }

        var folders = await _dbContext.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && f.ParentId == parentId)
            .OrderBy(f => f.Name)
            .Select(f => new FolderDto(f.Id, f.Name, f.ParentId, f.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<FolderDto>>.Success(folders);
    }

    public async Task<Result<FolderDto>> UpdateFolderAsync(Guid id, FolderRequestDto dto, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<FolderDto>.Failure(new Error("Folder.Unauthorized", "A valid user ID is required."));
        }

        var name = NormalizeName(dto.Name);
        if (name == null)
        {
            return Result<FolderDto>.Failure(InvalidNameError);
        }

        var folder = await FindOwnedFolderAsync(id, ownerId, cancellationToken);
        if (folder == null)
        {
            return Result<FolderDto>.Failure(NotFoundError);
        }

        var error = await ValidateLocationAsync(ownerId, name, dto.ParentId, id, cancellationToken);
        if (error != null)
        {
            return Result<FolderDto>.Failure(error);
        }

        folder.Name = name;
        folder.ParentId = dto.ParentId;
        folder.LastModifiedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<FolderDto>.Success(ToDto(folder));
    }

    public async Task<Result> DeleteFolderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result.Failure(new Error("Folder.Unauthorized", "A valid user ID is required."));
        }

        var folder = await FindOwnedFolderAsync(id, ownerId, cancellationToken);
        if (folder == null)
        {
            return Result.Failure(NotFoundError);
        }

        var hasChildren = await _dbContext.Folders.AnyAsync(f => f.ParentId == id, cancellationToken);
        var hasFiles = await _dbContext.FileMetadatas.AnyAsync(f => f.FolderId == id, cancellationToken);
        if (hasChildren || hasFiles)
        {
            return Result.Failure(new Error("Folder.NotEmpty", "Folder must be empty before it can be deleted."));
        }

        folder.IsDeleted = true;
        folder.LastModifiedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static readonly Error InvalidNameError = new("Folder.InvalidName", "Folder name must be 1 to 255 characters.");
    private static readonly Error NotFoundError = new("Folder.NotFound", "Folder not found.");

    // Returns the trimmed name, or null when it is empty or too long.
    private static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 255 ? null : trimmed;
    }

    // Checks the parent, the cycle (when moving an existing folder) and name uniqueness. Returns null when valid.
    private async Task<Error?> ValidateLocationAsync(Guid ownerId, string name, Guid? parentId, Guid? folderId, CancellationToken cancellationToken)
    {
        if (parentId is { } parent)
        {
            if (!await _dbContext.Folders.AnyAsync(f => f.Id == parent && f.OwnerId == ownerId, cancellationToken))
            {
                return NotFoundError;
            }

            if (folderId is { } id)
            {
                // Walk up from the new parent; hitting the folder itself means it would become its own ancestor.
                var visited = new HashSet<Guid>();
                Guid? ancestorId = parent;
                while (ancestorId is { } current && visited.Add(current))
                {
                    if (current == id)
                    {
                        return new Error("Folder.Cycle", "A folder cannot be moved into itself or one of its descendants.");
                    }

                    ancestorId = await _dbContext.Folders
                        .Where(f => f.Id == current)
                        .Select(f => f.ParentId)
                        .FirstOrDefaultAsync(cancellationToken);
                }
            }
        }

        // The unique index does not cover root folders (NULL ParentId), so the name is checked in code for all levels.
        var duplicate = await _dbContext.Folders.AnyAsync(
            f => f.OwnerId == ownerId && f.ParentId == parentId && f.Name == name && f.Id != folderId,
            cancellationToken);

        return duplicate ? new Error("Folder.NameTaken", "A folder with this name already exists here.") : null;
    }

    private async Task<Folder?> FindOwnedFolderAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        await _dbContext.Folders.FirstOrDefaultAsync(
            f => f.Id == id && f.OwnerId == ownerId && !f.IsDeleted,
            cancellationToken);

    private static FolderDto ToDto(Folder folder) =>
        new(folder.Id, folder.Name, folder.ParentId, folder.CreatedAtUtc);
}
