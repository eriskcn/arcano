using System;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArcanoCore.Infrastructure.Data.Configurations;

public class FileMetadataConfiguration : IEntityTypeConfiguration<FileMetadata>
{
    public void Configure(EntityTypeBuilder<FileMetadata> builder)
    {
        builder.ToTable("FileMetadata");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.StorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(x => x.StorageKey).IsUnique();

        builder.Property(x => x.IsDeleted)
            .HasDefaultValue(false);

        builder.Property(x => x.IsStarred)
            .HasDefaultValue(false);

        builder.HasQueryFilter(x => !x.IsDeleted);

        // Owner: restrict delete (don't cascade-delete files when user deleted)
        builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional folder
        builder.HasOne(x => x.Folder)
            .WithMany(f => f.Files)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.SetNull);

        // Tags (one-to-many; FileTag owns the FK)
        builder.HasMany(x => x.Tags)
            .WithOne(t => t.FileMetadata)
            .HasForeignKey(t => t.FileMetadataId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
