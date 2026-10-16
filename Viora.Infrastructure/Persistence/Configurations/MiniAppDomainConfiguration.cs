using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;
internal sealed class MiniAppDomainConfiguration : IEntityTypeConfiguration<MiniAppDomainVerification>
{
    public void Configure(EntityTypeBuilder<MiniAppDomainVerification> b)
    {
        b.ToTable("MiniAppDomainVerifications"); b.HasKey(x => x.Id);
        b.Property(x => x.Hostname).HasMaxLength(253).IsRequired(); b.Property(x => x.VerificationMethod).HasMaxLength(24).IsRequired();
        b.Property(x => x.VerificationFileName).HasMaxLength(80).IsRequired(); b.HasIndex(x => x.VerificationFileName).IsUnique();
        b.Property(x => x.VerificationTokenHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.VerificationMaterialEncrypted).HasMaxLength(512).IsRequired(); b.Property(x => x.SecretKeyVersion).HasMaxLength(40).IsRequired();
        b.Property(x => x.LastErrorCode).HasMaxLength(64); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.Hostname); b.HasIndex(x => x.Status);
        b.HasIndex(x => x.MiniAppId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
