using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArcanoCore.Application.Common.Interfaces;
using ArcanoCore.Domain.Common;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArcanoCore.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser<Guid>, IdentityRole<Guid>, Guid>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<FileMetadata> FileMetadatas => Set<FileMetadata>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<SharedLink> SharedLinks => Set<SharedLink>();
    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    // Starring/moving a file must not change its place in the "recent" view.
                    if (entry.Entity is FileMetadata && !entry.Properties.Any(p => p.IsModified
                            && p.Metadata.Name is not (nameof(FileMetadata.IsStarred) or nameof(FileMetadata.FolderId))))
                        break;
                    if (entry.Entity is Folder && !entry.Properties.Any(p => p.IsModified
                            && p.Metadata.Name is not nameof(Folder.IsStarred)))
                        break;
                    entry.Entity.LastModifiedAtUtc = DateTime.UtcNow;
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
