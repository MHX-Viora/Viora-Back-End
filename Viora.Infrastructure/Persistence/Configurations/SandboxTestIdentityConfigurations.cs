using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class SandboxIdentityTemplateConfiguration : IEntityTypeConfiguration<SandboxIdentityTemplate>
{
    public void Configure(EntityTypeBuilder<SandboxIdentityTemplate> b)
    {
        b.ToTable("SandboxIdentityTemplates"); b.HasKey(x => x.Id);
        b.Property(x => x.FullName).HasMaxLength(100); b.Property(x => x.Nickname).HasMaxLength(100);
        b.HasIndex(x => x.AccountId).IsUnique();
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class SandboxTestIdentityConfiguration : IEntityTypeConfiguration<SandboxTestIdentity>
{
    public void Configure(EntityTypeBuilder<SandboxTestIdentity> b)
    {
        b.ToTable("SandboxTestIdentities"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.DeveloperAccountId, x.MiniAppId }).IsUnique(); b.HasIndex(x => x.AccountId);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Developer).WithMany().HasForeignKey(x => x.DeveloperAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class SandboxIdentitySessionConfiguration : IEntityTypeConfiguration<SandboxIdentitySession>
{
    public void Configure(EntityTypeBuilder<SandboxIdentitySession> b)
    {
        b.ToTable("SandboxIdentitySessions"); b.HasKey(x => x.Id);
        b.Property(x => x.LaunchHash).HasMaxLength(64); b.Property(x => x.BrowserHash).HasMaxLength(64);
        b.HasIndex(x => x.LaunchHash).IsUnique(); b.HasIndex(x => x.BrowserHash).IsUnique();
        b.HasIndex(x => new { x.DeveloperSessionId, x.ExpiresAt });
        b.HasOne(x => x.Identity).WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DeveloperSession).WithMany().HasForeignKey(x => x.DeveloperSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.OAuthClient).WithMany().HasForeignKey(x => x.OAuthClientId).OnDelete(DeleteBehavior.Restrict);
    }
}
