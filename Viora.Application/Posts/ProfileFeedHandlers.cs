using FluentValidation;
using MediatR;

namespace Viora.Application.Posts;

public sealed class GetProfileFeedHandler(
    IProfileFeedRepository repository,
    IValidator<GetProfileFeedQuery> validator)
    : IRequestHandler<GetProfileFeedQuery, PostFeedResponse>
{
    public async Task<PostFeedResponse> Handle(GetProfileFeedQuery request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new PostFeedResponse(Math.Max(request.Page, 1), Math.Clamp(request.PageSize, 1, 100), 0, 0, []);
        }

        return await repository.GetProfileFeedAsync(
            request with
            {
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 100)
            },
            cancellationToken);
    }
}
