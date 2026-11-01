using System;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArcanoCore.Infrastructure.Data.Configurations;

public class UserShareConfiguration : IEntityTypeConfiguration<UserShare>
{
    public void Configure(EntityTypeBuilder<UserShare> builder)
    {
        builder.ToTable("UserShare", t => t.HasCheckConstraint("CK_UserShare_OneTarget", "(\"FileId\" IS NULL) <> (\"FolderId\" IS NULL)"));

        builder.HasKey(x => x.Id);

        // Both users: restrict delete, like other owner FKs (account deletion removes the shares first).
        builder.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Restrict);

        // A purged file or folder takes its shares with it.
        builder.HasOne<FileMetadata>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Folder>().WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.RecipientId);
        builder.HasIndex(x => new { x.OwnerId, x.FileId });
        builder.HasIndex(x => new { x.OwnerId, x.FolderId });

        // NULLs are distinct in Postgres unique indexes, so uniqueness is one partial index per target kind.
        builder.HasIndex(x => new { x.OwnerId, x.RecipientId, x.FileId }).IsUnique().HasFilter("\"FileId\" IS NOT NULL");
        builder.HasIndex(x => new { x.OwnerId, x.RecipientId, x.FolderId }).IsUnique().HasFilter("\"FolderId\" IS NOT NULL");
    }
}
