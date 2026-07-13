using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        builder.ToTable("Follows", table => table.HasCheckConstraint("CK_Follows_DifferentUsers", "\"FollowerId\" <> \"FollowingId\""));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.FollowerId, x.FollowingId }).IsUnique();
        builder.HasOne(x => x.Follower).WithMany().HasForeignKey(x => x.FollowerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Following).WithMany().HasForeignKey(x => x.FollowingId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendships", table => table.HasCheckConstraint("CK_Friendship_DifferentUsers", "\"RequesterUserId\" <> \"AddresseeUserId\""));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasDefaultValue(FriendshipStatus.Pending);
        builder.HasIndex(x => new { x.RequesterUserId, x.AddresseeUserId }).IsUnique();
        builder.HasOne(x => x.RequesterUser).WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AddresseeUser).WithMany().HasForeignKey(x => x.AddresseeUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
