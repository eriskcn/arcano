using System;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArcanoCore.Infrastructure.Data.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("Folder");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(x => x.IsDeleted)
            .HasDefaultValue(false);

        builder.HasQueryFilter(x => !x.IsDeleted);

        // Owner
        builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing parent/child
        builder.HasOne(x => x.Parent)
            .WithMany(f => f.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite unique: same owner cannot have two live folders with the same name under the same parent
        builder.HasIndex(x => new { x.OwnerId, x.ParentId, x.Name })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
    }
}
