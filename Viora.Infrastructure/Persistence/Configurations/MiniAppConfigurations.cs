using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Configurations;

internal sealed class DeveloperConfiguration : IEntityTypeConfiguration<Developer>
{
    public void Configure(EntityTypeBuilder<Developer> builder)
    {
        builder.ToTable("Developers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.CompanyName).HasMaxLength(160);
        builder.Property(x => x.Email).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20);
        builder.Property(x => x.Website).HasMaxLength(2048);
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.AccountId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppConfiguration : IEntityTypeConfiguration<MiniApp>
{
    public void Configure(EntityTypeBuilder<MiniApp> builder)
    {
        builder.ToTable("MiniApps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.IconUrl).HasMaxLength(2048);
        builder.Property(x => x.CoverUrl).HasMaxLength(2048);
        builder.Property(x => x.WebUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.CallbackUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ClientId).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ClientSecretHash).IsRequired();
        builder.Property(x => x.AllowedDomains).HasColumnType("text[]");
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => x.ClientId).IsUnique();
        builder.HasIndex(x => x.DeveloperId);
        builder.HasIndex(x => x.Status);
        builder.HasOne(x => x.Developer).WithMany(x => x.MiniApps).HasForeignKey(x => x.DeveloperId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppPermissionConfiguration : IEntityTypeConfiguration<MiniAppPermission>
{
    public void Configure(EntityTypeBuilder<MiniAppPermission> builder)
    {
        builder.ToTable("MiniAppPermissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasData(MiniAppPermissionSeed.All);
    }
}

internal static class MiniAppPermissionSeed
{
    private static readonly DateTime SeededAt = new(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);
    public static readonly MiniAppPermission[] All =
    [
        Permission("11111111-1111-1111-1111-111111111101", "identity.login", "Đăng nhập ANKT", false),
        Permission("11111111-1111-1111-1111-111111111102", "profile.basic", "Hồ sơ cơ bản", false),
        Permission("11111111-1111-1111-1111-111111111103", "profile.email", "Địa chỉ email", true),
        Permission("11111111-1111-1111-1111-111111111104", "profile.phone", "Số điện thoại", true),
        Permission("11111111-1111-1111-1111-111111111105", "app.close", "Đóng Mini App", false),
        Permission("11111111-1111-1111-1111-111111111106", "app.open_url", "Mở liên kết ngoài", false),
        Permission("11111111-1111-1111-1111-111111111107", "app.theme", "Đọc giao diện", false),
    ];

    private static MiniAppPermission Permission(string id, string code, string name, bool sensitive) => new()
    {
        Id = Guid.Parse(id), Code = code, Name = name, IsSensitive = sensitive,
        Status = MiniAppPermissionStatus.Active, CreatedAt = SeededAt, UpdatedAt = SeededAt
    };
}

internal sealed class MiniAppPermissionMappingConfiguration : IEntityTypeConfiguration<MiniAppPermissionMapping>
{
    public void Configure(EntityTypeBuilder<MiniAppPermissionMapping> builder)
    {
        builder.ToTable("MiniAppPermissionMappings");
        builder.HasKey(x => new { x.MiniAppId, x.PermissionId });
        builder.HasOne(x => x.MiniApp).WithMany(x => x.PermissionMappings).HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Permission).WithMany(x => x.MiniAppMappings).HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppUserConsentConfiguration : IEntityTypeConfiguration<MiniAppUserConsent>
{
    public void Configure(EntityTypeBuilder<MiniAppUserConsent> builder)
    {
        builder.ToTable("MiniAppUserConsents");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.AccountId, x.MiniAppId, x.PermissionId }).IsUnique();
        builder.HasIndex(x => new { x.AccountId, x.MiniAppId });
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MiniAppLaunchCodeConfiguration : IEntityTypeConfiguration<MiniAppLaunchCode>
{
    public void Configure(EntityTypeBuilder<MiniAppLaunchCode> builder)
    {
        builder.ToTable("MiniAppLaunchCodes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.CodeHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MiniAppExternalIdentityConfiguration : IEntityTypeConfiguration<MiniAppExternalIdentity>
{
    public void Configure(EntityTypeBuilder<MiniAppExternalIdentity> builder)
    {
        builder.ToTable("MiniAppExternalIdentities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Subject).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.AccountId, x.MiniAppId }).IsUnique();
        builder.HasIndex(x => new { x.MiniAppId, x.Subject }).IsUnique();
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MiniApp).WithMany().HasForeignKey(x => x.MiniAppId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MiniAppAuditLogConfiguration : IEntityTypeConfiguration<MiniAppAuditLog>
{
    public void Configure(EntityTypeBuilder<MiniAppAuditLog> builder)
    {
        builder.ToTable("MiniAppAuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(500);
        builder.HasIndex(x => new { x.MiniAppId, x.CreatedAt });
    }
}

internal sealed class MiniAppLaunchLogConfiguration : IEntityTypeConfiguration<MiniAppLaunchLog>
{
    public void Configure(EntityTypeBuilder<MiniAppLaunchLog> builder)
    {
        builder.ToTable("MiniAppLaunchLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FailureReason).HasMaxLength(80);
        builder.HasIndex(x => new { x.MiniAppId, x.CreatedAt });
    }
}
