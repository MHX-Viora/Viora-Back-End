using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Viora.Application.Utilities;

public sealed record SaveFeaturedBrand(
    [Required, MaxLength(160)] string Name,
    [Required, MaxLength(2048)] string LogoUrl,
    [Range(0, 10000)] int SortOrder,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] [Range(0, long.MaxValue)] long Version);

public sealed record FeaturedBrandItem(Guid Id, string Name, string LogoUrl, int SortOrder, bool IsActive, long Version);
public interface IFeaturedBrandRepository
{
    Task<IReadOnlyList<FeaturedBrandItem>> ListAsync(bool activeOnly, CancellationToken token);
    Task<FeaturedBrandItem> SaveAsync(Guid? id, SaveFeaturedBrand input, CancellationToken token);
    Task<bool> DeleteAsync(Guid id, long version, CancellationToken token);
}
public sealed class FeaturedBrandConflictException : Exception;

public static class FeaturedBrandValidation
{
    public static void Validate(SaveFeaturedBrand input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 160 || input.SortOrder is < 0 or > 10000 || input.Version < 0)
            throw new ArgumentException("Tên hoặc thứ tự thương hiệu không hợp lệ.");
        var value = input.LogoUrl;
        if (value is not { Length: > 0 and <= 2048 } || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Logo phải là URL HTTPS hợp lệ (tối đa 2048 ký tự).");
    }
}
