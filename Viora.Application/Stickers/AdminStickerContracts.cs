using Viora.Domain.Entities;

namespace Viora.Application.Stickers;

public sealed record AdminStickerPackResponse(
    Guid Id, string Name, string? Description, string ThumbnailUrl, decimal Price,
    bool IsFeatured, bool IsActive, int SortOrder, DateTime? AvailableFrom,
    DateTime? AvailableUntil, int StickerCount, int OwnerCount, int UsageCount,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed record SaveStickerPackRequest(
    string Name, string? Description, string ThumbnailUrl, decimal Price,
    bool IsFeatured, bool IsActive, int SortOrder, DateTime? AvailableFrom,
    DateTime? AvailableUntil);

public sealed record CreateStickerPackRequest(
    string Name, string? Description, decimal Price,
    bool IsFeatured, bool IsActive, int SortOrder, DateTime? AvailableFrom,
    DateTime? AvailableUntil, StickerUploadFile Thumbnail);

public sealed record SaveStickerRequest(
    string Name, string ImageUrl, string? ThumbnailUrl, StickerFormat Format,
    int SortOrder, bool IsActive);

public sealed record StickerUploadFile(Stream Content, string FileName, string ContentType, long Length);

public interface IStickerMediaStorage
{
    Task<string> UploadAsync(Guid packId, StickerUploadFile file, CancellationToken token);
}

public interface IAdminStickerService
{
    Task<IReadOnlyList<AdminStickerPackResponse>> GetPacksAsync(CancellationToken token);
    Task<StickerPackDetailResponse?> GetPackAsync(Guid packId, CancellationToken token);
    Task<AdminStickerPackResponse> CreatePackAsync(CreateStickerPackRequest request, CancellationToken token);
    Task<AdminStickerPackResponse?> UpdatePackAsync(Guid packId, SaveStickerPackRequest request, CancellationToken token);
    Task<bool> SetPackActiveAsync(Guid packId, bool isActive, CancellationToken token);
    Task<StickerResponse?> CreateStickerAsync(Guid packId, SaveStickerRequest request, CancellationToken token);
    Task<StickerResponse?> UpdateStickerAsync(Guid stickerId, SaveStickerRequest request, CancellationToken token);
    Task<bool> SetStickerActiveAsync(Guid stickerId, bool isActive, CancellationToken token);
    Task<string> UploadThumbnailAsync(Guid packId, StickerUploadFile file, CancellationToken token);
    Task<string> UploadAsync(Guid packId, StickerUploadFile file, CancellationToken token);
}
