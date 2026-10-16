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

internal sealed class MiniAppPairwiseSubjectConfiguration : IEntityTypeConfiguration<MiniAppPairwiseSubject>
{
    public void Configure(EntityTypeBuilder<MiniAppPairwiseSubject> b)
    {
        b.ToTable("MiniAppPairwiseSubjects"); b.HasKey(x => x.Id);
        b.Property(x => x.Subject).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.AccountId, x.MiniAppId, x.Environment }).IsUnique();
        b.HasIndex(x => new { x.MiniAppId, x.Environment, x.Subject }).IsUnique();
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppUserConsentConfiguration : IEntityTypeConfiguration<MiniAppUserConsent>
{
    public void Configure(EntityTypeBuilder<MiniAppUserConsent> b)
    {
        b.ToTable("MiniAppUserConsents"); b.HasKey(x => x.Id);
        b.Property(x => x.GrantedScopes).HasMaxLength(128).IsRequired(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.AccountId, x.OAuthClientId }).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppOAuthClient>().WithMany().HasForeignKey(x => x.OAuthClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreAuthorization>()
            .WithMany().HasForeignKey(x => x.AuthorizationId).OnDelete(DeleteBehavior.SetNull);
    }
}
