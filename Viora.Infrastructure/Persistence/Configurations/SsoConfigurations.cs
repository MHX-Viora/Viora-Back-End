using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class AnktSsoSessionConfiguration : IEntityTypeConfiguration<AnktSsoSession>
{
    public void Configure(EntityTypeBuilder<AnktSsoSession> b)
    {
        b.ToTable("AnktSsoSessions"); b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.SocialSessionId }); b.HasIndex(x => x.ExpiresAt);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SsoLoginTransactionConfiguration : IEntityTypeConfiguration<SsoLoginTransaction>
{
    public void Configure(EntityTypeBuilder<SsoLoginTransaction> b)
    {
        b.ToTable("SsoLoginTransactions"); b.HasKey(x => x.Id);
        b.Property(x => x.BrowserHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.ReturnUrl).HasMaxLength(8192).IsRequired(); b.HasIndex(x => x.ExpiresAt);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
