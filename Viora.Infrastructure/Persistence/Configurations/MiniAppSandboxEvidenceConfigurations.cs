using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal static class SandboxCatalogConstraint
{
    internal static string Values<T>(string column) where T:struct,Enum =>
        $"\"{column}\" IN ({string.Join(',',Enum.GetValues<T>().Select(value=>Convert.ToInt32(value)))})";
}

internal sealed class MiniAppSandboxTestRunConfiguration : IEntityTypeConfiguration<MiniAppSandboxTestRun>
{
    public void Configure(EntityTypeBuilder<MiniAppSandboxTestRun> b)
    {
        b.ToTable("MiniAppSandboxTestRuns",t=>
        {
            t.HasCheckConstraint("CK_SandboxRuns_Environment","\"Environment\" = 0");
            t.HasCheckConstraint("CK_SandboxRuns_Status",SandboxCatalogConstraint.Values<SandboxTestRunStatus>("Status"));
            t.HasCheckConstraint("CK_SandboxRuns_Summary",SandboxCatalogConstraint.Values<SandboxMessageCode>("SummaryCode"));
            t.HasCheckConstraint("CK_SandboxRuns_Fingerprint","char_length(\"ConfigurationFingerprint\") = 64");
            t.HasCheckConstraint("CK_SandboxRuns_Times","\"DeadlineAt\" > \"CreatedAt\" AND (\"StartedAt\" IS NULL OR \"StartedAt\" >= \"CreatedAt\") AND (\"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"CreatedAt\") AND (\"StartedAt\" IS NULL OR \"CompletedAt\" IS NULL OR \"CompletedAt\" >= \"StartedAt\")");
            t.HasCheckConstraint("CK_SandboxRuns_Completion","(\"Status\" IN (0,1) AND \"CompletedAt\" IS NULL) OR (\"Status\" IN (2,3,4) AND \"CompletedAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_SandboxRuns_Started","\"Status\" <> 1 OR \"StartedAt\" IS NOT NULL");
        });
        b.HasKey(x=>x.Id);b.Property(x=>x.Version).IsConcurrencyToken();
        b.Property(x=>x.ConfigurationFingerprint).HasMaxLength(64).IsRequired();
        b.HasAlternateKey(x=>new{x.Id,x.SandboxSessionId});
        b.HasOne<MiniAppSandboxSession>().WithMany().HasForeignKey(x=>new{x.SandboxSessionId,x.ConfigurationFingerprint})
            .HasPrincipalKey(x=>new{x.Id,x.ConfigurationFingerprint}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.SandboxSessionId).IsUnique().HasFilter("\"Status\" IN (0,1)");
        b.HasIndex(x=>new{x.SandboxSessionId,x.CreatedAt});b.HasIndex(x=>new{x.Status,x.DeadlineAt});b.HasIndex(x=>x.CompletedAt);
    }
}

internal sealed class MiniAppSandboxTestResultConfiguration : IEntityTypeConfiguration<MiniAppSandboxTestResult>
{
    public void Configure(EntityTypeBuilder<MiniAppSandboxTestResult> b)
    {
        b.ToTable("MiniAppSandboxTestResults",t=>
        {
            t.HasCheckConstraint("CK_SandboxResults_Key",SandboxCatalogConstraint.Values<SandboxCheckKey>("TestKey"));
            t.HasCheckConstraint("CK_SandboxResults_Status",SandboxCatalogConstraint.Values<SandboxCheckStatus>("Status"));
            t.HasCheckConstraint("CK_SandboxResults_Message",SandboxCatalogConstraint.Values<SandboxMessageCode>("MessageCode"));
            t.HasCheckConstraint("CK_SandboxResults_Source",SandboxCatalogConstraint.Values<SandboxEvidenceSource>("EvidenceSource"));
            t.HasCheckConstraint("CK_SandboxResults_Duration","\"DurationMs\" BETWEEN 0 AND 1800000");
            t.HasCheckConstraint("CK_SandboxResults_PassedEvidence","\"Status\" <> 0 OR (\"EvidenceAt\" IS NOT NULL AND \"EvidenceSource\" IN (0,3,4))");
            t.HasCheckConstraint("CK_SandboxResults_RuntimeProof",$"\"Status\" <> 0 OR \"TestKey\" NOT IN ({(int)SandboxCheckKey.Runtime},{(int)SandboxCheckKey.Bridge}) OR \"EvidenceSource\" = {(int)SandboxEvidenceSource.TrustedBrowser}");
            t.HasCheckConstraint("CK_SandboxResults_ProtocolProof",$"\"Status\" <> 0 OR \"TestKey\" NOT IN ({(int)SandboxCheckKey.Consent},{(int)SandboxCheckKey.Token},{(int)SandboxCheckKey.UserInfo}) OR \"EvidenceSource\" = {(int)SandboxEvidenceSource.ServerProtocol}");
        });
        b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.TestRunId,x.TestKey});b.Property(x=>x.Version).IsConcurrencyToken();
        b.HasOne<MiniAppSandboxTestRun>().WithMany().HasForeignKey(x=>x.TestRunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppSandboxDiagnosticConfiguration : IEntityTypeConfiguration<MiniAppSandboxDiagnostic>
{
    public void Configure(EntityTypeBuilder<MiniAppSandboxDiagnostic> b)
    {
        b.ToTable("MiniAppSandboxDiagnostics",t=>
        {
            t.HasCheckConstraint("CK_SandboxDiagnostics_Level",SandboxCatalogConstraint.Values<SandboxDiagnosticLevel>("Level"));
            t.HasCheckConstraint("CK_SandboxDiagnostics_Category",SandboxCatalogConstraint.Values<SandboxDiagnosticCategory>("Category"));
            t.HasCheckConstraint("CK_SandboxDiagnostics_Code",SandboxCatalogConstraint.Values<SandboxMessageCode>("Code"));
            t.HasCheckConstraint("CK_SandboxDiagnostics_Source",SandboxCatalogConstraint.Values<SandboxEvidenceSource>("EvidenceSource"));
            t.HasCheckConstraint("CK_SandboxDiagnostics_Count","\"Count\" IS NULL OR \"Count\" BETWEEN 0 AND 1000000");
        });
        b.HasKey(x=>x.Id);
        b.HasOne<MiniAppSandboxSession>().WithMany().HasForeignKey(x=>x.SandboxSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MiniAppSandboxTestRun>().WithMany().HasForeignKey(x=>new{x.TestRunId,x.SandboxSessionId})
            .HasPrincipalKey(x=>new{x.Id,x.SandboxSessionId}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>new{x.SandboxSessionId,x.CreatedAt,x.Id});b.HasIndex(x=>x.CreatedAt);
    }
}

internal sealed class MiniAppSandboxProtocolEvidenceConfiguration : IEntityTypeConfiguration<MiniAppSandboxProtocolEvidence>
{
    public void Configure(EntityTypeBuilder<MiniAppSandboxProtocolEvidence> b)
    {
        b.ToTable("MiniAppSandboxProtocolEvidence",t=>
        {
            t.HasCheckConstraint("CK_SandboxProtocol_Key",$"\"TestKey\" IN ({(int)SandboxCheckKey.Consent},{(int)SandboxCheckKey.Token},{(int)SandboxCheckKey.UserInfo})");
            t.HasCheckConstraint("CK_SandboxProtocol_Stage",SandboxCatalogConstraint.Values<SandboxProtocolStage>("Stage"));
            t.HasCheckConstraint("CK_SandboxProtocol_Outcome",SandboxCatalogConstraint.Values<SandboxProtocolOutcome>("Outcome"));
            t.HasCheckConstraint("CK_SandboxProtocol_EventKey","char_length(\"EventKey\") = 64");
            t.HasCheckConstraint("CK_SandboxProtocol_Binding",$"(\"Stage\" = 0 AND \"TestKey\" = {(int)SandboxCheckKey.Consent}) OR (\"Stage\" = 1 AND \"TestKey\" = {(int)SandboxCheckKey.Token}) OR (\"Stage\" = 2 AND \"TestKey\" = {(int)SandboxCheckKey.UserInfo})");
        });
        b.HasKey(x=>x.Id);b.Property(x=>x.EventKey).HasMaxLength(64).IsRequired();b.HasIndex(x=>x.EventKey).IsUnique();
        b.Property(x=>x.AuthorizationId).HasMaxLength(100);b.Property(x=>x.TokenId).HasMaxLength(100);
        b.HasOne<MiniAppSandboxTestResult>().WithMany().HasForeignKey(x=>new{x.TestRunId,x.TestKey})
            .HasPrincipalKey(x=>new{x.TestRunId,x.TestKey}).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x=>x.CreatedAt);
    }
}
