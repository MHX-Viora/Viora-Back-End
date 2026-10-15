using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class DeveloperAccountConfiguration : IEntityTypeConfiguration<DeveloperAccount>
{
    public void Configure(EntityTypeBuilder<DeveloperAccount> b)
    {
        b.ToTable("DeveloperAccounts"); b.HasKey(x => x.Id);
        b.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        b.Property(x => x.EmailNormalized).HasMaxLength(254).IsRequired();
        b.HasIndex(x => x.EmailNormalized).IsUnique();
        b.Property(x => x.AvatarUrl).HasMaxLength(2048);
        b.Property(x => x.TermsVersion).HasMaxLength(40);
        b.Property(x => x.SecurityVersion).IsConcurrencyToken();
    }
}
internal sealed class DeveloperSessionConfiguration : IEntityTypeConfiguration<DeveloperSession>
{
    public void Configure(EntityTypeBuilder<DeveloperSession> b)
    {
        b.ToTable("DeveloperSessions"); b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.DeviceInfo).HasMaxLength(256);
        b.HasIndex(x => new { x.DeveloperAccountId, x.RevokedAt, x.ExpiresAt });
        b.HasIndex(x => x.ExpiresAt);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class DeveloperActionTokenConfiguration : IEntityTypeConfiguration<DeveloperActionToken>
{
    public void Configure(EntityTypeBuilder<DeveloperActionToken> b)
    {
        b.ToTable("DeveloperActionTokens"); b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.DeveloperAccountId, x.Purpose, x.CreatedAt }); b.HasIndex(x => x.ExpiresAt);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class DeveloperExternalIdentityConfiguration : IEntityTypeConfiguration<DeveloperExternalIdentity>
{
    public void Configure(EntityTypeBuilder<DeveloperExternalIdentity> b)
    {
        b.ToTable("DeveloperExternalIdentities"); b.HasKey(x => x.Id);
        b.Property(x => x.Provider).HasMaxLength(40).IsRequired();
        b.Property(x => x.ProviderSubjectId).HasMaxLength(255).IsRequired();
        b.HasIndex(x => new { x.Provider, x.ProviderSubjectId }).IsUnique();
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class DeveloperAuditLogConfiguration : IEntityTypeConfiguration<DeveloperAuditLog>
{
    public void Configure(EntityTypeBuilder<DeveloperAuditLog> b)
    {
        b.ToTable("DeveloperAuditLogs"); b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(80); b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.UserAgent).HasMaxLength(256); b.Property(x => x.Metadata).HasMaxLength(1024);
        b.HasIndex(x => new { x.DeveloperAccountId, x.CreatedAt }); b.HasIndex(x => x.CreatedAt);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}
