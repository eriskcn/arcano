using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<FileMetadata> FileMetadatas { get; }
    DbSet<Folder> Folders { get; }
    DbSet<FileTag> FileTags { get; }
    DbSet<SharedLink> SharedLinks { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
