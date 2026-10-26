using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppBridgeConfiguration : IEntityTypeConfiguration<MiniAppBridgeSession>
{
    public void Configure(EntityTypeBuilder<MiniAppBridgeSession> b)
    {
        b.ToTable("MiniAppBridgeSessions"); b.HasKey(x => x.Id);
        b.Property(x => x.ClientId).HasMaxLength(100); b.Property(x => x.Origin).HasMaxLength(512);
        b.Property(x => x.Policy).HasMaxLength(256); b.Property(x => x.CodeHash).HasMaxLength(64);
        b.Property(x => x.AccessHash).HasMaxLength(64); b.Property(x => x.CodeChallenge).HasMaxLength(43);
        b.HasIndex(x => x.CodeHash).IsUnique(); b.HasIndex(x => x.AccessHash).IsUnique();
        b.HasIndex(x => new { x.ActorId, x.SourceSessionId, x.ExpiresAt });
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
