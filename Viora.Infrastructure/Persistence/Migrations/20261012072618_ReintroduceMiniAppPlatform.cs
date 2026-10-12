using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Viora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReintroduceMiniAppPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeveloperEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestWindow = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    DeliverySucceeded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperEmailVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperEmailVerifications_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Developers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TradingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BusinessRegistrationNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RepresentativeName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RegistrationVersion = table.Column<int>(type: "integer", nullable: false),
                    DraftApplicationJson = table.Column<string>(type: "text", nullable: true),
                    RegistrationStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Website = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Developers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Developers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Icon = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppHistoryStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClearedBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppHistoryStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppHistoryStates_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppLaunchLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppLaunchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSensitive = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TermsAcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperApplications_Accounts_SubmittedByAccountId",
                        column: x => x.SubmittedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperApplications_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperMemberships_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperMemberships_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperReviewHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ReviewerAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InternalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperReviewHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperReviewHistory_Accounts_ReviewerAccountId",
                        column: x => x.ReviewerAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperReviewHistory_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    CatalogSearchText = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    CatalogCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HiddenFromCatalog = table.Column<bool>(type: "boolean", nullable: false),
                    SupportEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PrivacyPolicyUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TermsUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DevelopmentUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    AgeRating = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReleaseRegions = table.Column<string[]>(type: "text[]", nullable: false),
                    IntegrationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SsoMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AutoLogin = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CoverUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    WebUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CallbackUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DeveloperId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "text", nullable: false),
                    SecretRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthenticationMode = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    ClientAuthenticationMethod = table.Column<short>(type: "smallint", nullable: false),
                    CallbackUrls = table.Column<string[]>(type: "text[]", nullable: false),
                    AllowedOrigins = table.Column<string[]>(type: "text[]", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    PendingVersion = table.Column<int>(type: "integer", nullable: true),
                    PublishedConfigurationJson = table.Column<string>(type: "text", nullable: true),
                    AllowedDomains = table.Column<string[]>(type: "text[]", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniApps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniApps_Developers_DeveloperId",
                        column: x => x.DeveloperId,
                        principalTable: "Developers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniApps_MiniAppCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "MiniAppCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeveloperReauths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operation = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProofHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeveloperReauths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeveloperReauths_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeveloperReauths_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppBanners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppBanners", x => x.Id);
                    table.CheckConstraint("CK_MiniAppBanner_Target", "(\"MiniAppId\" IS NULL) <> (\"CategoryId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_MiniAppBanners_MiniAppCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "MiniAppCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppBanners_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppExternalIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppExternalIdentities_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppExternalIdentities_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppFavorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppFavorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppFavorites_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppFavorites_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppPermissionMappings",
                columns: table => new
                {
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPermissionMappings", x => new { x.MiniAppId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_MiniAppPermissionMappings_MiniAppPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "MiniAppPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppPermissionMappings_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppPlacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppPlacements_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppReports_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppReports_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppRuntimeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppRuntimeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppRuntimeSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppRuntimeSessions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppTechnicalTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestedByAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppTechnicalTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppTechnicalTests_Accounts_TestedByAccountId",
                        column: x => x.TestedByAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppTechnicalTests_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppUserConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppUserConsents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_MiniAppPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "MiniAppPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppUserConsents_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppVerifiedDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    ChallengeToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppVerifiedDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppVerifiedDomains_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TechnicalTestJson = table.Column<string>(type: "text", nullable: true),
                    TestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppVersions_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MiniAppLaunchCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    MiniAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RedirectUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    StateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CodeChallenge = table.Column<string>(type: "character varying(43)", maxLength: 43, nullable: true),
                    RuntimeSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MiniAppLaunchCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_MiniAppRuntimeSessions_RuntimeSessionId",
                        column: x => x.RuntimeSessionId,
                        principalTable: "MiniAppRuntimeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MiniAppLaunchCodes_MiniApps_MiniAppId",
                        column: x => x.MiniAppId,
                        principalTable: "MiniApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MiniAppPermissions",
                columns: new[] { "Id", "Code", "CreatedAt", "Description", "IsSensitive", "Name", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), "identity.login", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Đăng nhập ANKT", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111102"), "profile.basic", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Hồ sơ cơ bản", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111103"), "profile.email", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Địa chỉ email", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111104"), "profile.phone", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Số điện thoại", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111105"), "app.close", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Đóng Mini App", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111106"), "app.open_url", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Mở liên kết ngoài", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("11111111-1111-1111-1111-111111111107"), "app.theme", new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "Đọc giao diện", (short)1, new DateTime(2026, 8, 20, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApplications_DeveloperId_Version",
                table: "DeveloperApplications",
                columns: new[] { "DeveloperId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperApplications_SubmittedByAccountId",
                table: "DeveloperApplications",
                column: "SubmittedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperEmailVerifications_AccountId_CreatedAt",
                table: "DeveloperEmailVerifications",
                columns: new[] { "AccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperEmailVerifications_AccountId_RequestWindow",
                table: "DeveloperEmailVerifications",
                columns: new[] { "AccountId", "RequestWindow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperEmailVerifications_ExpiresAt",
                table: "DeveloperEmailVerifications",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperMemberships_AccountId",
                table: "DeveloperMemberships",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperMemberships_DeveloperId_AccountId",
                table: "DeveloperMemberships",
                columns: new[] { "DeveloperId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_AccountId",
                table: "DeveloperReauths",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_ExpiresAt",
                table: "DeveloperReauths",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_MiniAppId",
                table: "DeveloperReauths",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReauths_ProofHash",
                table: "DeveloperReauths",
                column: "ProofHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReviewHistory_DeveloperId_CreatedAt",
                table: "DeveloperReviewHistory",
                columns: new[] { "DeveloperId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperReviewHistory_ReviewerAccountId",
                table: "DeveloperReviewHistory",
                column: "ReviewerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Developers_AccountId",
                table: "Developers",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Developers_Email",
                table: "Developers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Developers_Status",
                table: "Developers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppAuditLogs_MiniAppId_CreatedAt",
                table: "MiniAppAuditLogs",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBanners_CategoryId",
                table: "MiniAppBanners",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBanners_IsActive_SortOrder",
                table: "MiniAppBanners",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppBanners_MiniAppId",
                table: "MiniAppBanners",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppCategories_IsActive_SortOrder",
                table: "MiniAppCategories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppCategories_Slug",
                table: "MiniAppCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppExternalIdentities_AccountId_MiniAppId",
                table: "MiniAppExternalIdentities",
                columns: new[] { "AccountId", "MiniAppId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppExternalIdentities_MiniAppId_Subject",
                table: "MiniAppExternalIdentities",
                columns: new[] { "MiniAppId", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppFavorites_AccountId_MiniAppId",
                table: "MiniAppFavorites",
                columns: new[] { "AccountId", "MiniAppId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppFavorites_AccountId_SortOrder",
                table: "MiniAppFavorites",
                columns: new[] { "AccountId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppFavorites_MiniAppId",
                table: "MiniAppFavorites",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppHistoryStates_AccountId",
                table: "MiniAppHistoryStates",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_AccountId",
                table: "MiniAppLaunchCodes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_CodeHash",
                table: "MiniAppLaunchCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_ExpiresAt",
                table: "MiniAppLaunchCodes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_MiniAppId",
                table: "MiniAppLaunchCodes",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchCodes_RuntimeSessionId",
                table: "MiniAppLaunchCodes",
                column: "RuntimeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppLaunchLogs_MiniAppId_CreatedAt",
                table: "MiniAppLaunchLogs",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPermissionMappings_PermissionId",
                table: "MiniAppPermissionMappings",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPermissions_Code",
                table: "MiniAppPermissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPlacements_IsActive_SortOrder",
                table: "MiniAppPlacements",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppPlacements_MiniAppId",
                table: "MiniAppPlacements",
                column: "MiniAppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReports_AccountId",
                table: "MiniAppReports",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppReports_MiniAppId_CreatedAt",
                table: "MiniAppReports",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_AccountId",
                table: "MiniAppRuntimeSessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_ExpiresAt",
                table: "MiniAppRuntimeSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_MiniAppId",
                table: "MiniAppRuntimeSessions",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppRuntimeSessions_TokenHash",
                table: "MiniAppRuntimeSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_CatalogCategoryId_Status",
                table: "MiniApps",
                columns: new[] { "CatalogCategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_CatalogName",
                table: "MiniApps",
                column: "CatalogName");

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_CategoryId",
                table: "MiniApps",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_ClientId",
                table: "MiniApps",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_DeveloperId",
                table: "MiniApps",
                column: "DeveloperId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_Slug",
                table: "MiniApps",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_Status",
                table: "MiniApps",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MiniApps_Status_HiddenFromCatalog_PublishedAt",
                table: "MiniApps",
                columns: new[] { "Status", "HiddenFromCatalog", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppTechnicalTests_MiniAppId_CreatedAt",
                table: "MiniAppTechnicalTests",
                columns: new[] { "MiniAppId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppTechnicalTests_TestedByAccountId",
                table: "MiniAppTechnicalTests",
                column: "TestedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_AccountId_MiniAppId",
                table: "MiniAppUserConsents",
                columns: new[] { "AccountId", "MiniAppId" });

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_AccountId_MiniAppId_PermissionId",
                table: "MiniAppUserConsents",
                columns: new[] { "AccountId", "MiniAppId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_MiniAppId",
                table: "MiniAppUserConsents",
                column: "MiniAppId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppUserConsents_PermissionId",
                table: "MiniAppUserConsents",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVerifiedDomains_MiniAppId_Host",
                table: "MiniAppVerifiedDomains",
                columns: new[] { "MiniAppId", "Host" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MiniAppVersions_MiniAppId_Version",
                table: "MiniAppVersions",
                columns: new[] { "MiniAppId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeveloperApplications");

            migrationBuilder.DropTable(
                name: "DeveloperEmailVerifications");

            migrationBuilder.DropTable(
                name: "DeveloperMemberships");

            migrationBuilder.DropTable(
                name: "DeveloperReauths");

            migrationBuilder.DropTable(
                name: "DeveloperReviewHistory");

            migrationBuilder.DropTable(
                name: "MiniAppAuditLogs");

            migrationBuilder.DropTable(
                name: "MiniAppBanners");

            migrationBuilder.DropTable(
                name: "MiniAppExternalIdentities");

            migrationBuilder.DropTable(
                name: "MiniAppFavorites");

            migrationBuilder.DropTable(
                name: "MiniAppHistoryStates");

            migrationBuilder.DropTable(
                name: "MiniAppLaunchCodes");

            migrationBuilder.DropTable(
                name: "MiniAppLaunchLogs");

            migrationBuilder.DropTable(
                name: "MiniAppPermissionMappings");

            migrationBuilder.DropTable(
                name: "MiniAppPlacements");

            migrationBuilder.DropTable(
                name: "MiniAppReports");

            migrationBuilder.DropTable(
                name: "MiniAppTechnicalTests");

            migrationBuilder.DropTable(
                name: "MiniAppUserConsents");

            migrationBuilder.DropTable(
                name: "MiniAppVerifiedDomains");

            migrationBuilder.DropTable(
                name: "MiniAppVersions");

            migrationBuilder.DropTable(
                name: "MiniAppRuntimeSessions");

            migrationBuilder.DropTable(
                name: "MiniAppPermissions");

            migrationBuilder.DropTable(
                name: "MiniApps");

            migrationBuilder.DropTable(
                name: "Developers");

            migrationBuilder.DropTable(
                name: "MiniAppCategories");
        }
    }
}
