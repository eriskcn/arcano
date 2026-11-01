using System;
using ArcanoCore.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArcanoCore.Infrastructure.Data.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfile");
        builder.HasKey(x => x.UserId);

        builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InviteKeyConfiguration : IEntityTypeConfiguration<InviteKey>
{
    public void Configure(EntityTypeBuilder<InviteKey> builder)
    {
        builder.ToTable("InviteKey");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(x => x.KeyHash).IsUnique();
        // Two redeems of the same key race on this column; the loser's update fails.
        builder.Property(x => x.UsedAtUtc).IsConcurrencyToken();

        builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(x => x.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
