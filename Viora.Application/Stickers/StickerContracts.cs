using Viora.Domain.Entities;

namespace Viora.Application.Stickers;

public enum StickerPackFilter { All, Featured, Free, Paid, Owned, Usable }

public sealed record StickerPackSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    string ThumbnailUrl,
    decimal Price,
    bool IsFree,
    bool IsFeatured,
    int StickerCount,
    bool IsOwned,
    bool CanUse);

public sealed record StickerResponse(
    Guid Id,
    Guid StickerPackId,
    string Name,
    string ImageUrl,
    string? ThumbnailUrl,
    StickerFormat Format,
    int SortOrder)
{
    public bool IsActive { get; init; } = true;
}

public sealed record StickerPackDetailResponse(
    StickerPackSummaryResponse Pack,
    bool IsOwned,
    bool CanUse,
    IReadOnlyList<StickerResponse> Stickers);

public sealed record StickerPackListResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<StickerPackSummaryResponse> Items);

public interface IStickerService
{
    Task<StickerPackListResponse> GetPacksAsync(Guid userId, StickerPackFilter filter, int page, int pageSize, CancellationToken token);
    Task<StickerPackDetailResponse?> GetPackAsync(Guid userId, Guid packId, CancellationToken token);
}
