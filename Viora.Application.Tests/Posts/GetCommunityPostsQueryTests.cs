using Viora.Application.Posts;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Application.Tests.Posts;

public sealed class GetCommunityPostsQueryTests
{
    [Fact]
    public void SupportsAnOptionalPostTypeFilter()
    {
        var query = new GetCommunityPostsQuery(1, 10, null, null, null, PostType.Article);

        Assert.Equal(PostType.Article, query.PostType);
    }
}
