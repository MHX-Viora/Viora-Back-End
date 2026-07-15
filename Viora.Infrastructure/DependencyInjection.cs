using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Persistence;
using Viora.Application.Accounts;
using Viora.Infrastructure.Persistence.Repositories;
using Viora.Infrastructure.Security;
using Microsoft.Extensions.Options;
using System.Text;
using Viora.Application.Users;
using Viora.Infrastructure.Media;
using Viora.Application.Posts;
using FluentValidation;

namespace Viora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing required configuration 'ConnectionStrings:DefaultConnection'. " +
                "Set it with the environment variable ConnectionStrings__DefaultConnection or User Secrets.");
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        var jwtOptions = new JwtOptions
        {
            Key = configuration["Jwt:Key"] ?? string.Empty,
            Issuer = configuration["Jwt:Issuer"] ?? "viora-BE",
            Audience = configuration["Jwt:Audience"] ?? "viora-client"
        };
        if (int.TryParse(configuration["Jwt:AccessTokenMinutes"], out var accessTokenMinutes))
        {
            jwtOptions.AccessTokenMinutes = accessTokenMinutes;
        }
        if (int.TryParse(configuration["Jwt:RefreshTokenDays"], out var refreshTokenDays))
        {
            jwtOptions.RefreshTokenDays = refreshTokenDays;
        }
        if (Encoding.UTF8.GetByteCount(jwtOptions.Key) < 32)
        {
            throw new InvalidOperationException(
                "Missing or invalid configuration 'Jwt:Key'. Set at least 32 UTF-8 bytes " +
                "in appsettings.json or with the environment variable Jwt__Key.");
        }
        services.AddSingleton(Options.Create(jwtOptions));
        services.AddSingleton<ITokenService, JwtTokenService>();
        var cloudinaryOptions = new CloudinaryOptions
        {
            CloudName = configuration["Cloudinary:CloudName"] ?? string.Empty,
            ApiKey = configuration["Cloudinary:ApiKey"] ?? string.Empty,
            ApiSecret = configuration["Cloudinary:ApiSecret"] ?? string.Empty
        };
        if (string.IsNullOrWhiteSpace(cloudinaryOptions.CloudName) ||
            string.IsNullOrWhiteSpace(cloudinaryOptions.ApiKey) ||
            string.IsNullOrWhiteSpace(cloudinaryOptions.ApiSecret))
        {
            throw new InvalidOperationException(
                "Missing Cloudinary configuration. Set Cloudinary:CloudName, Cloudinary:ApiKey, " +
                "and Cloudinary:ApiSecret with User Secrets or environment variables.");
        }
        services.AddSingleton(Options.Create(cloudinaryOptions));
        services.AddSingleton<IProfileImageStorage, CloudinaryProfileImageStorage>();
        services.AddSingleton<IMediaStorage, CloudinaryMediaStorage>();
        services.AddScoped<IValidator<CreatePostCommand>, CreatePostValidator>();
        services.AddScoped<IValidator<CreateReelCommand>, CreateReelValidator>();
        services.AddScoped<IValidator<ReactPostCommand>, ReactPostValidator>();
        services.AddScoped<IValidator<CreateCommentCommand>, CreateCommentValidator>();
        services.AddScoped<IValidator<ReplyCommentCommand>, ReplyCommentValidator>();
        services.AddScoped<IValidator<ReportPostCommand>, ReportPostValidator>();
        services.AddScoped<IValidator<GetPostCommentsQuery>, GetPostCommentsValidator>();
        services.AddScoped<IValidator<GetCommentRepliesQuery>, GetCommentRepliesValidator>();
        services.AddScoped<IValidator<GetShortVideosQuery>, GetShortVideosValidator>();
        services.AddScoped<IValidator<ToggleVideoReactionCommand>, ToggleVideoReactionValidator>();
        services.AddScoped<IValidator<ToggleVideoSaveCommand>, ToggleVideoSaveValidator>();
        services.AddScoped<IValidator<ShareVideoCommand>, ShareVideoValidator>();
        services.AddScoped<IValidator<CreateVideoCommentCommand>, CreateVideoCommentValidator>();
        services.AddScoped<IValidator<ReplyVideoCommentCommand>, ReplyVideoCommentValidator>();
        services.AddScoped<IValidator<GetVideoCommentsQuery>, GetVideoCommentsValidator>();
        services.AddScoped<IValidator<GetVideoRepliesQuery>, GetVideoRepliesValidator>();
        services.AddScoped<IValidator<DeleteVideoCommentCommand>, DeleteVideoCommentValidator>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IPostFeedRepository, PostFeedRepository>();
        services.AddScoped<IVideoFeedRepository, VideoFeedRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<IPostInteractionRepository, PostInteractionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}
