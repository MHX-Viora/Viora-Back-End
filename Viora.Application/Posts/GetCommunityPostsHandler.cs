using MediatR;

namespace Viora.Application.Posts;

public sealed class GetCommunityPostsHandler(IPostFeedRepository repository)
    : IRequestHandler<GetCommunityPostsQuery, PostFeedResponse>
{
    public Task<PostFeedResponse> Handle(
        GetCommunityPostsQuery request,
        CancellationToken cancellationToken) =>
        repository.GetCommunityPostsAsync(
            request with
            {
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 100),
                Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim()
            },
            cancellationToken);
}
