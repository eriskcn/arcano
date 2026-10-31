using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
    private readonly IStorageService _storage;

    // Bounds the work of one zip request.
    private const int MaxZipFiles = 5000;

    public FolderService(IApplicationDbContext dbContext, ICurrentUser currentUser, IStorageService storage)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _storage = storage;
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

        var now = DateTime.UtcNow;
        var tree = await CollectTreeAsync(folder, ownerId, cancellationToken);
        foreach (var f in tree)
        {
            f.IsDeleted = true;
            f.LastModifiedAtUtc = now;
        }

        var ids = tree.Select(f => f.Id).ToList();
        var files = await _dbContext.FileMetadatas
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted && f.FolderId != null && ids.Contains(f.FolderId.Value))
            .ToListAsync(cancellationToken);
        foreach (var file in files)
        {
            file.IsDeleted = true;
            file.DeletedAtUtc = now;
            file.LastModifiedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // The folder plus all live descendants, found level by level.
    private async Task<List<Folder>> CollectTreeAsync(Folder root, Guid ownerId, CancellationToken cancellationToken)
    {
        var tree = new List<Folder> { root };
        var level = new List<Guid> { root.Id };
        while (level.Count > 0)
        {
            var children = await _dbContext.Folders
                .Where(f => f.OwnerId == ownerId && !f.IsDeleted && f.ParentId != null && level.Contains(f.ParentId.Value))
                .ToListAsync(cancellationToken);
            tree.AddRange(children);
            level = children.Select(f => f.Id).ToList();
        }

        return tree;
    }

    public async Task<Result<string>> GetZipNameAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result<string>.Failure(new Error("Folder.Unauthorized", "A valid user ID is required."));
        }

        var folder = await FindOwnedFolderAsync(id, ownerId, cancellationToken);
        return folder == null ? Result<string>.Failure(NotFoundError) : Result<string>.Success(folder.Name + ".zip");
    }

    public async Task<Result> WriteZipAsync(Guid id, Stream output, CancellationToken cancellationToken = default)
    {
        var ownerId = CurrentOwnerId;
        if (ownerId == Guid.Empty)
        {
            return Result.Failure(new Error("Folder.Unauthorized", "A valid user ID is required."));
        }

        var root = await FindOwnedFolderAsync(id, ownerId, cancellationToken);
        if (root == null)
        {
            return Result.Failure(NotFoundError);
        }

        var tree = await CollectTreeAsync(root, ownerId, cancellationToken);
        var ids = tree.Select(f => f.Id).ToList();
        var files = await _dbContext.FileMetadatas
            .AsNoTracking()
            .Where(f => f.OwnerId == ownerId && !f.IsDeleted && f.FolderId != null && ids.Contains(f.FolderId.Value))
            .OrderBy(f => f.FileName)
            .ToListAsync(cancellationToken);
        if (files.Count > MaxZipFiles)
        {
            return Result.Failure(new Error("Folder.TooLarge", $"Folder holds more than {MaxZipFiles} files."));
        }

        // Relative directory path per folder, with sibling names made unique.
        var paths = new Dictionary<Guid, string> { [root.Id] = "" };
        var used = new Dictionary<string, HashSet<string>>();
        foreach (var folder in tree.Skip(1).OrderBy(f => f.Name))
        {
            var parentPath = paths[folder.ParentId!.Value];
            paths[folder.Id] = parentPath + UniqueName(used, parentPath, SanitizeName(folder.Name)) + "/";
        }

        await using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var path in paths.Values.Where(p => p.Length > 0))
        {
            zip.CreateEntry(path);
        }

        foreach (var file in files)
        {
            var dir = paths[file.FolderId!.Value];
            var entry = zip.CreateEntry(dir + UniqueName(used, dir, SanitizeName(file.FileName)), CompressionLevel.Fastest);
            await using var target = entry.Open();
            await using var source = await _storage.DownloadAsync(file.StorageKey, cancellationToken);
            await source.CopyToAsync(target, cancellationToken);
        }

        return Result.Success();
    }

    // Drops path separators, control characters and dot-only names so an entry cannot escape the archive.
    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(char.IsControl(c) || c == '/' || c == '\\' ? '_' : c);
        }

        var clean = sb.ToString().Trim();
        return clean.Trim('.').Length == 0 ? "_" : clean;
    }

    private static string UniqueName(Dictionary<string, HashSet<string>> used, string dir, string name)
    {
        if (!used.TryGetValue(dir, out var names))
        {
            used[dir] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var candidate = name;
        for (var n = 2; !names.Add(candidate); n++)
        {
            var dot = name.LastIndexOf('.');
            candidate = dot > 0 ? $"{name[..dot]} ({n}){name[dot..]}" : $"{name} ({n})";
        }

        return candidate;
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
