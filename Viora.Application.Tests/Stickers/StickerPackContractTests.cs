using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Stickers;

public sealed class StickerPackContractTests
{
    [Fact]
    public void Sticker_pack_has_no_manual_sort_order()
    {
        Assert.Null(typeof(StickerPack).GetProperty("SortOrder"));
    }
}
