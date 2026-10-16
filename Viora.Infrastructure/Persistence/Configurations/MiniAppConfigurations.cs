using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppConfiguration : IEntityTypeConfiguration<MiniApp>
{
    public void Configure(EntityTypeBuilder<MiniApp> b)
    {
        b.ToTable("MiniApps"); b.HasKey(x => x.Id);
        b.Property(x => x.AppId).HasMaxLength(48).IsRequired(); b.HasIndex(x => x.AppId).IsUnique();
        b.Property(x => x.Name).HasMaxLength(120).IsRequired(); b.Property(x => x.Category).HasMaxLength(32).IsRequired();
        b.Property(x => x.ShortDescription).HasMaxLength(300); b.Property(x => x.Description).HasMaxLength(10000);
        b.Property(x => x.SupportEmail).HasMaxLength(254);
        foreach (var field in new[] { "LogoUrl", "WebsiteUrl", "PrivacyPolicyUrl", "TermsOfServiceUrl" }) b.Property<string>(field).HasMaxLength(2048);
        b.Property(x => x.CreateFingerprint).HasMaxLength(64).IsRequired(); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.DeveloperAccountId, x.CreateRequestId }).IsUnique();
        b.HasIndex(x => new { x.DeveloperAccountId, x.UpdatedAt }); b.HasIndex(x => x.Status);
        b.HasOne(x => x.Developer).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppClientConfiguration : IEntityTypeConfiguration<MiniAppOAuthClient>
{
    public void Configure(EntityTypeBuilder<MiniAppOAuthClient> b)
    {
        b.ToTable("MiniAppOAuthClients"); b.HasKey(x => x.Id);
        b.Property(x => x.ClientKey).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.ClientKey).IsUnique();
        b.Property(x => x.ClientType).HasMaxLength(24).IsRequired(); b.HasIndex(x => x.MiniAppId).IsUnique();
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppSecretConfiguration : IEntityTypeConfiguration<MiniAppClientSecret>
{
    public void Configure(EntityTypeBuilder<MiniAppClientSecret> b)
    {
        b.ToTable("MiniAppClientSecrets"); b.HasKey(x => x.Id);
        b.Property(x => x.SecretEncrypted).HasMaxLength(512).IsRequired(); b.Property(x => x.SecretKeyVersion).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.OAuthClientId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        b.HasOne(x => x.OAuthClient).WithMany().HasForeignKey(x => x.OAuthClientId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppRedirectConfiguration : IEntityTypeConfiguration<MiniAppRedirectUri>
{
    public void Configure(EntityTypeBuilder<MiniAppRedirectUri> b)
    {
        b.ToTable("MiniAppRedirectUris"); b.HasKey(x => x.Id); b.Property(x => x.RedirectUri).HasMaxLength(1024).IsRequired();
        b.HasIndex(x => new { x.MiniAppId, x.RedirectUri }).IsUnique();
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppAuditConfiguration : IEntityTypeConfiguration<MiniAppAuditLog>
{
    public void Configure(EntityTypeBuilder<MiniAppAuditLog> b)
    {
        b.ToTable("MiniAppAuditLogs"); b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.Metadata).HasMaxLength(1024).IsRequired(); b.HasIndex(x => new { x.MiniAppId, x.CreatedAt });
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorDeveloperId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppLogoConfiguration : IEntityTypeConfiguration<MiniAppLogoUpload>
{
    public void Configure(EntityTypeBuilder<MiniAppLogoUpload> b)
    {
        b.ToTable("MiniAppLogoUploads"); b.HasKey(x => x.Id); b.Property(x => x.PublicId).HasMaxLength(200).IsRequired();
        b.Property(x => x.Url).HasMaxLength(2048); b.HasIndex(x => x.ExpiresAt);
        b.HasOne(x => x.Developer).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
