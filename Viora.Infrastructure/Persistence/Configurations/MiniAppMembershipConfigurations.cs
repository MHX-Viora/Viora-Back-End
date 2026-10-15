using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppMembershipConfiguration : IEntityTypeConfiguration<MiniAppMembership>
{
    public void Configure(EntityTypeBuilder<MiniAppMembership> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.MiniAppId, x.AccountId }).IsUnique();
        b.Property(x => x.Role).IsConcurrencyToken();
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppMemberInvitationConfiguration : IEntityTypeConfiguration<MiniAppMemberInvitation>
{
    public void Configure(EntityTypeBuilder<MiniAppMemberInvitation> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.AccountId, x.ExpiresAt });
        b.Property(x => x.AcceptedAt).IsConcurrencyToken();
        b.Property(x => x.CancelledAt).IsConcurrencyToken();
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
