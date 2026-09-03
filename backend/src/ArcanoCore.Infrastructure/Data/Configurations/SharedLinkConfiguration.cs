using System;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArcanoCore.Infrastructure.Data.Configurations;

public class SharedLinkConfiguration : IEntityTypeConfiguration<SharedLink>
{
    public void Configure(EntityTypeBuilder<SharedLink> builder)
    {
        builder.ToTable("SharedLink");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Token)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.Token).IsUnique();

        // Creator
        builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional file target
        builder.HasOne(x => x.FileMetadata)
            .WithMany(f => f.SharedLinks)
            .HasForeignKey(x => x.FileMetadataId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optional folder target
        builder.HasOne(x => x.Folder)
            .WithMany(f => f.SharedLinks)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
