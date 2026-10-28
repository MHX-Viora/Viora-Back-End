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
