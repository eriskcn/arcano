using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<FileMetadata> FileMetadatas { get; }
    DbSet<Folder> Folders { get; }
    DbSet<SharedLink> SharedLinks { get; }
    DbSet<UploadSession> UploadSessions { get; }
    DbSet<UserProfile> UserProfiles { get; }
    DbSet<InviteKey> InviteKeys { get; }
    // Default so contexts that do not touch sharing need not declare it.
    DbSet<UserShare> UserShares => throw new System.NotSupportedException();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
