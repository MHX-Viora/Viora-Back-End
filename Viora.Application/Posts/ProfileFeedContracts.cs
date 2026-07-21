using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Posts;

public enum ProfileFeedKind
{
    ReactedPosts,
    ReactedReels,
    SavedPosts,
    SavedReels
}

public sealed record GetProfileFeedQuery(Guid UserId, ProfileFeedKind Kind, int Page, int PageSize)
    : IRequest<PostFeedResponse>;

public sealed class GetProfileFeedValidator : AbstractValidator<GetProfileFeedQuery>
{
    public GetProfileFeedValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Kind).Must(Enum.IsDefined);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public interface IProfileFeedRepository
{
    Task<PostFeedResponse> GetProfileFeedAsync(GetProfileFeedQuery query, CancellationToken cancellationToken);
}
