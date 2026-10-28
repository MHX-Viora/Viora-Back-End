using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppProductionHostSessionConfiguration : IEntityTypeConfiguration<MiniAppProductionHostSession>
{
    public void Configure(EntityTypeBuilder<MiniAppProductionHostSession> b)
    {
        b.ToTable("MiniAppProductionHostSessions", t =>
        {
            t.HasCheckConstraint("CK_ProductionHost_Actor", "(\"AccountId\" IS NULL AND \"SourceSocialSessionId\" IS NULL) OR (\"AccountId\" IS NOT NULL AND \"SourceSocialSessionId\" IS NOT NULL)");
            t.HasCheckConstraint("CK_ProductionHost_Platform", "\"Platform\" IN ('Web','Android','iOS')");
            t.HasCheckConstraint("CK_ProductionHost_Expiry", "\"ExpiresAt\" > \"CreatedAt\" AND (\"ClosedAt\" IS NULL OR \"ClosedAt\" >= \"CreatedAt\")");
            t.HasCheckConstraint("CK_ProductionHost_Hashes", string.Join(" AND ", new[] { "SnapshotHash", "PolicyFingerprint", "BrowserBindingHash", "BrowserNonceHash", "RequestHash" }
                .Select(p => $"length(\"{p}\") = 64 AND \"{p}\" = lower(\"{p}\")")));
            t.HasCheckConstraint("CK_ProductionHost_RuntimeHash", "\"RuntimeCredentialHash\" IS NULL OR (length(\"RuntimeCredentialHash\") = 64 AND \"RuntimeCredentialHash\" = lower(\"RuntimeCredentialHash\"))");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Platform).HasMaxLength(12);
        foreach (var name in new[] { "SnapshotHash", "PolicyFingerprint", "BrowserBindingHash", "BrowserNonceHash", "RequestHash", "RuntimeCredentialHash" })
            b.Property<string>(name).HasMaxLength(64);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.BindingId, x.RequestId }).IsUnique();
        b.HasIndex(x => x.RuntimeCredentialHash).IsUnique().HasFilter("\"RuntimeCredentialHash\" IS NOT NULL");
        b.HasIndex(x => new { x.BindingId, x.ClosedAt, x.ExpiresAt });
        b.HasIndex(x => new { x.MiniAppId, x.ExpiresAt });
        b.HasIndex(x => x.ExpiresAt);
        b.HasOne<MiniAppPublication>().WithMany()
            .HasForeignKey(x => new { x.PublicationId, x.MiniAppId, x.MiniAppVersionId, x.ApprovedReviewRequestId })
            .HasPrincipalKey(x => new { x.Id, x.MiniAppId, x.MiniAppVersionId, x.ApprovedReviewRequestId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in b.Metadata.GetProperties().Where(p => p.Name is not ("Version" or "LastActivityAt" or "ClosedAt" or "RuntimeCredentialHash")))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

internal sealed class MiniAppProductionLaunchGrantConfiguration : IEntityTypeConfiguration<MiniAppProductionLaunchGrant>
{
    public void Configure(EntityTypeBuilder<MiniAppProductionLaunchGrant> b)
    {
        b.ToTable("MiniAppProductionLaunchGrants", t =>
        {
            t.HasCheckConstraint("CK_ProductionGrant_Hashes", "length(\"GrantHash\") = 64 AND \"GrantHash\" = lower(\"GrantHash\") AND length(\"BrowserNonceHash\") = 64 AND \"BrowserNonceHash\" = lower(\"BrowserNonceHash\")");
            t.HasCheckConstraint("CK_ProductionGrant_Expiry", "\"ExpiresAt\" > \"CreatedAt\" AND (\"ConsumedAt\" IS NULL OR \"ConsumedAt\" >= \"CreatedAt\")");
            t.HasCheckConstraint("CK_ProductionGrant_Receipt", "(\"ConsumedAt\" IS NOT NULL OR \"ReceiptProtected\" IS NOT NULL) AND (\"ReceiptProtected\" IS NULL OR length(\"ReceiptProtected\") BETWEEN 1 AND 16384)");
            t.HasCheckConstraint("CK_ProductionGrant_Audience", "length(\"Audience\") BETWEEN 1 AND 512");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.GrantHash).HasMaxLength(64);
        b.Property(x => x.BrowserNonceHash).HasMaxLength(64);
        b.Property(x => x.Audience).HasMaxLength(512);
        b.Property(x => x.ReceiptProtected).HasMaxLength(16384);
        b.HasIndex(x => x.GrantHash).IsUnique();
        b.HasIndex(x => x.HostSessionId).IsUnique();
        b.HasIndex(x => x.ExpiresAt);
        b.HasOne<MiniAppProductionHostSession>().WithMany().HasForeignKey(x => new { x.HostSessionId, x.BindingId })
            .HasPrincipalKey(x => new { x.Id, x.BindingId }).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in b.Metadata.GetProperties().Where(p => p.Name is not ("Version" or "ConsumedAt" or "ReceiptProtected")))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
