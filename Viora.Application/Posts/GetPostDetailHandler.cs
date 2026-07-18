using MediatR;

namespace Viora.Application.Posts;

public sealed class GetPostDetailHandler(IPostFeedRepository repository)
    : IRequestHandler<GetPostDetailQuery, Result<PostDetailResponse>>
{
    public Task<Result<PostDetailResponse>> Handle(
        GetPostDetailQuery request,
        CancellationToken cancellationToken) =>
        repository.GetPostDetailAsync(request, cancellationToken);
}
