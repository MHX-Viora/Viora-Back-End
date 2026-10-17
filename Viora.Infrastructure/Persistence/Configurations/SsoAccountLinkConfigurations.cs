using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class SsoAccountLinkChallengeConfiguration : IEntityTypeConfiguration<SsoAccountLinkChallenge>
{
    public void Configure(EntityTypeBuilder<SsoAccountLinkChallenge> b)
    {
        b.ToTable("SsoAccountLinkChallenges"); b.HasKey(x => x.Id);
        b.Property(x => x.Subject).HasMaxLength(100).IsRequired();
        b.Property(x => x.PartnerAccountId).HasMaxLength(128).IsRequired();
        b.Property(x => x.ProofHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.ProofHash).IsUnique(); b.HasIndex(x => x.ExpiresAt);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppOAuthClient>().WithMany().HasForeignKey(x => x.OAuthClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppClientSecret>().WithMany().HasForeignKey(x => x.ClientSecretId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppAccountLinkConfiguration : IEntityTypeConfiguration<MiniAppAccountLink>
{
    public void Configure(EntityTypeBuilder<MiniAppAccountLink> b)
    {
        b.ToTable("MiniAppAccountLinks"); b.HasKey(x => x.Id);
        b.Property(x => x.Subject).HasMaxLength(100).IsRequired();
        b.Property(x => x.PartnerAccountId).HasMaxLength(128).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.MiniAppId, x.Environment, x.Subject }).IsUnique();
        b.HasIndex(x => new { x.MiniAppId, x.Environment, x.PartnerAccountId }).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.MiniAppId, x.Environment }).IsUnique();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SsoAccountLinkAuditConfiguration : IEntityTypeConfiguration<SsoAccountLinkAudit>
{
    public void Configure(EntityTypeBuilder<SsoAccountLinkAudit> b)
    {
        b.ToTable("SsoAccountLinkAudits", table => table.HasCheckConstraint("CK_SsoAccountLinkAudits_Action", "\"Action\" IN ('link', 'unlink', 'relink')"));
        b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(8).IsRequired();
        b.HasIndex(x => new { x.AccountId, x.CreatedAt });
        b.HasOne<MiniAppAccountLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict);
    }
}
