using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts", table => table.HasCheckConstraint(
            "CK_Accounts_EmailOrPhone",
            "\"Email\" IS NOT NULL OR \"Phone\" IS NOT NULL"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(255);
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.PasswordHash).IsRequired();
        builder.Property(x => x.Role).HasDefaultValue(AccountRole.User);
        builder.Property(x => x.Status).HasDefaultValue(AccountStatus.Active).HasSentinel(AccountStatus.Active);
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.Phone).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Gender).HasDefaultValue(Gender.Unknown);
        builder.Property(x => x.IsVerified).HasDefaultValue(false);
        builder.Property(x => x.IdentityStatus).HasDefaultValue(UserIdentityState.NotVerified);
        builder.HasIndex(x => x.AccountId).IsUnique();
        builder.HasOne(x => x.Account).WithOne(x => x.User).HasForeignKey<User>(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> builder)
    {
        builder.ToTable("UserIdentity");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IdentityNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Birthday).HasColumnType("date");
        builder.Property(x => x.FrontImageUrl).IsRequired();
        builder.Property(x => x.BackImageUrl).IsRequired();
        builder.Property(x => x.Status).HasDefaultValue(IdentitySubmissionStatus.Pending).HasSentinel(IdentitySubmissionStatus.Pending);
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"Status\" = 1").HasDatabaseName("UX_UserIdentity_UserId_Pending");
        builder.HasOne(x => x.User).WithMany(x => x.IdentitySubmissions).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("UserSettings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IsPrivate).HasDefaultValue(false);
        builder.Property(x => x.AllowMessageEveryone).HasDefaultValue(true);
        builder.Property(x => x.AllowComment).HasDefaultValue(true);
        builder.Property(x => x.AllowMention).HasDefaultValue(true);
        builder.Property(x => x.Language).HasMaxLength(20).HasDefaultValue("vi");
        builder.Property(x => x.Theme).HasMaxLength(20).HasDefaultValue("light");
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne(x => x.User).WithOne(x => x.Settings).HasForeignKey<UserSettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
