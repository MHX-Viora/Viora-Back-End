using FluentValidation;
using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Hashtags;

public sealed record CreateHashtagCommand(string Name) : IRequest<HashtagResponse>;

public sealed record SearchHashtagsQuery(string? Keyword, int Page, int PageSize)
    : IRequest<HashtagSearchResponse>;

public sealed record HashtagResponse(
    Guid Id,
    string Name,
    int PostCount,
    DateTime CreatedAt);

public sealed record HashtagSearchResponse(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<HashtagSearchItemResponse> Items);

public sealed record HashtagSearchItemResponse(
    Guid Id,
    string Name,
    int PostCount);

public sealed class CreateHashtagValidator : AbstractValidator<CreateHashtagCommand>
{
    public CreateHashtagValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(HashtagNormalizer.Normalize(name)))
            .WithMessage("Hashtag name is required.");
    }
}

public sealed class SearchHashtagsValidator : AbstractValidator<SearchHashtagsQuery>
{
    public SearchHashtagsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).GreaterThan(0);
    }
}

public interface IHashtagRepository
{
    Task<Hashtag?> GetByNameAsync(string name, CancellationToken cancellationToken);
    Task AddAsync(Hashtag hashtag, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<HashtagSearchResponse> SearchAsync(SearchHashtagsQuery query, CancellationToken cancellationToken);
}

internal static class HashtagNormalizer
{
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimStart('#').Trim().ToLowerInvariant();
}
