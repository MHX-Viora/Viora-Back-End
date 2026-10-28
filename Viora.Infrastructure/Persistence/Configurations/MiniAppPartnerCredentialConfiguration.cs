using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Developers;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class MiniAppPartnerCredentialConfiguration : IEntityTypeConfiguration<MiniAppPartnerCredential>
{
    public void Configure(EntityTypeBuilder<MiniAppPartnerCredential> b)
    {
        b.ToTable("MiniAppPartnerCredentials"); b.HasKey(x => x.Id);
        b.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.SecretEncrypted).HasMaxLength(1024).IsRequired();
        b.Property(x => x.SecretKeyVersion).HasMaxLength(40).IsRequired();
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.MiniAppId, x.Environment }).IsUnique();
        b.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
