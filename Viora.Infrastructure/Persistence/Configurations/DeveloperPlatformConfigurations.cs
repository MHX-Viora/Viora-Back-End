using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;
namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class DeveloperApplicationConfiguration : IEntityTypeConfiguration<DeveloperApplication>
{
    public void Configure(EntityTypeBuilder<DeveloperApplication> b)
    {
        b.ToTable("DeveloperApplications"); b.HasKey(x=>x.Id);
        b.Property(x=>x.ConfigurationJson).HasColumnType("text"); b.Property(x=>x.Status).HasMaxLength(32).IsConcurrencyToken(); b.Property(x=>x.TermsVersion).HasMaxLength(80);
        b.HasIndex(x=>new{x.DeveloperId,x.Version}).IsUnique();
        b.HasOne(x=>x.Developer).WithMany().HasForeignKey(x=>x.DeveloperId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.SubmittedByAccount).WithMany().HasForeignKey(x=>x.SubmittedByAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class DeveloperReviewHistoryConfiguration : IEntityTypeConfiguration<DeveloperReviewHistory>
{
    public void Configure(EntityTypeBuilder<DeveloperReviewHistory> b)
    {
        b.ToTable("DeveloperReviewHistory"); b.HasKey(x=>x.Id);
        b.Property(x=>x.PreviousStatus).HasMaxLength(32); b.Property(x=>x.Status).HasMaxLength(32); b.Property(x=>x.Reason).HasMaxLength(1000); b.Property(x=>x.InternalNotes).HasMaxLength(2000);
        b.HasIndex(x=>new{x.DeveloperId,x.CreatedAt});
        b.HasOne(x=>x.Developer).WithMany().HasForeignKey(x=>x.DeveloperId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.ReviewerAccount).WithMany().HasForeignKey(x=>x.ReviewerAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class DeveloperEmailVerificationConfiguration : IEntityTypeConfiguration<DeveloperEmailVerification>
{
    public void Configure(EntityTypeBuilder<DeveloperEmailVerification> b)
    {
        b.ToTable("DeveloperEmailVerifications"); b.HasKey(x=>x.Id);
        b.Property(x=>x.Email).HasMaxLength(255); b.Property(x=>x.CodeHash).HasMaxLength(64); b.Property(x=>x.Attempts).IsConcurrencyToken(); b.Property(x=>x.VerifiedAt).IsConcurrencyToken();
        b.HasIndex(x=>new{x.AccountId,x.CreatedAt}); b.HasIndex(x=>new{x.AccountId,x.RequestWindow}).IsUnique(); b.HasIndex(x=>x.ExpiresAt);
        b.HasOne(x=>x.Account).WithMany().HasForeignKey(x=>x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MiniAppTechnicalTestConfiguration : IEntityTypeConfiguration<MiniAppTechnicalTest>
{
    public void Configure(EntityTypeBuilder<MiniAppTechnicalTest> b)
    {
        b.ToTable("MiniAppTechnicalTests"); b.HasKey(x=>x.Id); b.Property(x=>x.ConfigurationHash).HasMaxLength(64); b.Property(x=>x.ResultJson).HasColumnType("text");
        b.HasIndex(x=>new{x.MiniAppId,x.CreatedAt});
        b.HasOne(x=>x.MiniApp).WithMany().HasForeignKey(x=>x.MiniAppId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.TestedByAccount).WithMany().HasForeignKey(x=>x.TestedByAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
