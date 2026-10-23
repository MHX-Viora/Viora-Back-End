using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppSandboxSessionConfiguration : IEntityTypeConfiguration<MiniAppSandboxSession>
{
    public void Configure(EntityTypeBuilder<MiniAppSandboxSession> b)
    {
        b.ToTable("MiniAppSandboxSessions",table=>
        {
            table.HasCheckConstraint("CK_MiniAppSandboxSessions_Environment","\"Environment\" = 0");
            table.HasCheckConstraint("CK_MiniAppSandboxSessions_Expiry","\"ExpiresAt\" > \"CreatedAt\"");
            table.HasCheckConstraint("CK_MiniAppSandboxSessions_DroppedDiagnostics","\"DroppedDiagnosticCount\" BETWEEN 0 AND 1000000");
            table.HasCheckConstraint("CK_MiniAppSandboxSessions_Actor",
                "(\"ActorType\" = 'Developer' AND \"ActorDeveloperAccountId\" IS NOT NULL AND \"ActorDeveloperAccountId\" = \"DeveloperAccountId\" AND \"SourceDeveloperSessionId\" IS NOT NULL AND \"ActorAdminAccountId\" IS NULL AND \"SourceSocialSessionId\" IS NULL) OR " +
                "(\"ActorType\" = 'Admin' AND \"ActorAdminAccountId\" IS NOT NULL AND \"SourceSocialSessionId\" IS NOT NULL AND \"ActorDeveloperAccountId\" IS NULL AND \"SourceDeveloperSessionId\" IS NULL)");
        });
        b.HasKey(x=>x.Id);
        b.Property(x=>x.ActorType).HasMaxLength(12).IsRequired(); b.Property(x=>x.Version).IsConcurrencyToken();
        b.Property(x=>x.ConfigurationFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x=>x.WebsiteUrl).HasMaxLength(2048).IsRequired(); b.Property(x=>x.PartnerOrigin).HasMaxLength(512).IsRequired();
        b.Property(x=>x.RuntimeCredentialHash).HasMaxLength(64);
        b.HasIndex(x=>x.RuntimeCredentialHash).IsUnique().HasFilter("\"RuntimeCredentialHash\" IS NOT NULL");
        b.HasIndex(x=>new{x.ActorDeveloperAccountId,x.RevokedAt,x.ExpiresAt});
        b.HasIndex(x=>new{x.ActorAdminAccountId,x.RevokedAt,x.ExpiresAt});
        b.HasIndex(x=>new{x.MiniAppId,x.CreatedAt}); b.HasIndex(x=>x.ExpiresAt);
        b.HasOne<MiniApp>().WithMany().HasForeignKey(x=>x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppOAuthClient>().WithMany().HasForeignKey(x=>x.OAuthClientId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x=>x.DeveloperAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperAccount>().WithMany().HasForeignKey(x=>x.ActorDeveloperAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x=>x.ActorAdminAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeveloperSession>().WithMany().HasForeignKey(x=>x.SourceDeveloperSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppSandboxLaunchGrantConfiguration : IEntityTypeConfiguration<MiniAppSandboxLaunchGrant>
{
    public void Configure(EntityTypeBuilder<MiniAppSandboxLaunchGrant> b)
    {
        b.ToTable("MiniAppSandboxLaunchGrants",table=>{
            table.HasCheckConstraint("CK_MiniAppSandboxLaunchGrants_Expiry","\"ExpiresAt\" > \"CreatedAt\"");
            table.HasCheckConstraint("CK_SandboxGrants_ExecutorBinding","(\"ExecutorRunId\" IS NULL AND \"ExecutorRunVersion\" IS NULL) OR (\"ExecutorRunId\" IS NOT NULL AND \"ExecutorRunVersion\" IS NOT NULL)");
        });
        b.HasKey(x=>x.Id); b.Property(x=>x.GrantHash).HasMaxLength(64).IsRequired();
        b.Property(x=>x.BrowserNonceHash).HasMaxLength(64).IsRequired(); b.Property(x=>x.Audience).HasMaxLength(512).IsRequired();
        b.Property(x=>x.Version).IsConcurrencyToken(); b.HasIndex(x=>x.GrantHash).IsUnique(); b.HasIndex(x=>x.ExpiresAt);
        b.HasOne<MiniAppSandboxSession>().WithMany().HasForeignKey(x=>x.SandboxSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppSandboxTestRun>().WithMany().HasForeignKey(x=>new{x.ExecutorRunId,x.SandboxSessionId})
            .HasPrincipalKey(x=>new{x.Id,x.SandboxSessionId}).OnDelete(DeleteBehavior.Restrict);
    }
}
