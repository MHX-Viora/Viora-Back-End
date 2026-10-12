using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

public sealed class DeveloperReauthConfiguration : IEntityTypeConfiguration<DeveloperReauth>
{
    public void Configure(EntityTypeBuilder<DeveloperReauth> builder)
    {
        builder.ToTable("DeveloperReauths");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Operation).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ProofHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.ProofHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MiniApp>().WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
    }
}
