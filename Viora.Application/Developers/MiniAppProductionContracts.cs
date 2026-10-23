namespace Viora.Application.Developers;

public sealed record MiniAppPublicItemDto(string Id, string Name, string? LogoUrl,
    string? Description, string Category, string Version);
public sealed record MiniAppPublicPageDto(IReadOnlyList<MiniAppPublicItemDto> Items, int Page, int PageSize, int Total);
public sealed record MiniAppPublicDetailDto(MiniAppPublicItemDto App, bool CanOpen, bool LoginAvailable,
    IReadOnlyList<string> Capabilities);

public sealed record MiniAppPublicCatalogQuery(int Page = 1, int PageSize = 20)
{
    public void Validate()
    {
        if (Page is < 1 or > 100000 || PageSize is < 1 or > 100)
            throw new DeveloperAuthException(400, "INVALID_CATALOG_PAGE", "Trang hoặc kích thước trang không hợp lệ.");
    }
}
