using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Viora.Application.Utilities;

public sealed record SaveUtilityBanner(
    [Required, MaxLength(160)] string Title,
    [Required, MaxLength(2048)] string ImageUrl,
    [MaxLength(2048)] string? TargetUrl,
    [Range(0, 10000)] int SortOrder,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] [Range(0, long.MaxValue)] long Version);

public sealed record UtilityBannerItem(Guid Id, string Title, string ImageUrl, string? TargetUrl, int SortOrder, bool IsActive, long Version);
public interface IUtilityBannerRepository
{
    Task<IReadOnlyList<UtilityBannerItem>> ListAsync(bool activeOnly, CancellationToken token);
    Task<UtilityBannerItem> SaveAsync(Guid? id, SaveUtilityBanner input, CancellationToken token);
    Task<bool> DeleteAsync(Guid id, long version, CancellationToken token);
}
public sealed class UtilityBannerConflictException : Exception;

public static class UtilityBannerValidation
{
    public static void Validate(SaveUtilityBanner input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160 || input.SortOrder is < 0 or > 10000 || input.Version < 0)
            throw new ArgumentException("Tiêu đề hoặc thứ tự banner không hợp lệ.");
        if (!Https(input.ImageUrl)) throw new ArgumentException("Ảnh banner phải là URL HTTPS hợp lệ (tối đa 2048 ký tự).");
        if (!string.IsNullOrWhiteSpace(input.TargetUrl) && !Https(input.TargetUrl) && !InternalPath(input.TargetUrl))
            throw new ArgumentException("Liên kết phải là HTTPS hoặc đường dẫn trong ứng dụng.");
    }

    private static bool Https(string? value) => value is { Length: > 0 and <= 2048 }
        && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl) && !value.Contains('\\')
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    private static bool InternalPath(string value) => value.Length <= 2048 && value.StartsWith('/')
        && !value.StartsWith("//") && !value.Contains('\\') && !value.Any(char.IsControl) && !value.Any(char.IsWhiteSpace)
        && !value.Contains('%');
}
