using FluentValidation;
using MediatR;

namespace Viora.Application.Posts;

public sealed class GetShortVideosHandler(
    IVideoFeedRepository repository,
    IValidator<GetShortVideosQuery> validator) : IRequestHandler<GetShortVideosQuery, Result<VideoFeedResponse>>
{
    public async Task<Result<VideoFeedResponse>> Handle(
        GetShortVideosQuery request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<VideoFeedResponse>.Failure(
                PostInteractionError.Invalid,
                ReactPostHandler.FirstError(validation));
        }

        var response = await repository.GetShortVideosAsync(request, cancellationToken);
        return Result<VideoFeedResponse>.Success(response);
    }
}
