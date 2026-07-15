using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Hashtags;

public sealed class CreateHashtagHandler(
    IHashtagRepository repository,
    IValidator<CreateHashtagCommand> validator)
    : IRequestHandler<CreateHashtagCommand, HashtagResponse>
{
    public async Task<HashtagResponse> Handle(CreateHashtagCommand request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var name = HashtagNormalizer.Normalize(request.Name);
        var existing = await repository.GetByNameAsync(name, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var hashtag = new Hashtag
        {
            Name = name,
            PostCount = 0
        };

        await repository.AddAsync(hashtag, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        return Map(hashtag);
    }

    private static HashtagResponse Map(Hashtag hashtag) => new(
        hashtag.Id,
        hashtag.Name,
        hashtag.PostCount,
        hashtag.CreatedAt);
}

public sealed class SearchHashtagsHandler(
    IHashtagRepository repository,
    IValidator<SearchHashtagsQuery> validator)
    : IRequestHandler<SearchHashtagsQuery, HashtagSearchResponse>
{
    public async Task<HashtagSearchResponse> Handle(SearchHashtagsQuery request, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        return await repository.SearchAsync(
            request with
            {
                Keyword = HashtagNormalizer.Normalize(request.Keyword),
                Page = Math.Max(request.Page, 1),
                PageSize = Math.Clamp(request.PageSize, 1, 100)
            },
            cancellationToken);
    }
}
