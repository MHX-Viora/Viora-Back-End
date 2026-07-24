using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Hashtags;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class HashtagApiContractTests
{
    [Fact]
    public void Hashtags_controller_exposes_authenticated_routes()
    {
        Assert.NotNull(typeof(HashtagsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/hashtags", typeof(HashtagsController).GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = typeof(HashtagsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(HashtagsController))
            .ToDictionary(method => method.Name);

        Assert.NotNull(methods[nameof(HashtagsController.Create)].GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(methods[nameof(HashtagsController.Search)].GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void Hashtag_response_contracts_match_client_shape()
    {
        AssertProperties<HashtagResponse>("Id", "Name", "PostCount", "CreatedAt");
        AssertProperties<HashtagSearchResponse>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<HashtagSearchItemResponse>("Id", "Name", "PostCount");
    }

    [Fact]
    public async Task Create_hashtag_validator_rejects_empty_normalized_name()
    {
        var validator = new CreateHashtagValidator();

        var result = await validator.ValidateAsync(new CreateHashtagCommand("  #  "));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Create_hashtag_validator_accepts_hash_prefixed_name()
    {
        var validator = new CreateHashtagValidator();

        var result = await validator.ValidateAsync(new CreateHashtagCommand("  #Travel  "));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Search_hashtags_validator_rejects_invalid_paging()
    {
        var validator = new SearchHashtagsValidator();

        var result = await validator.ValidateAsync(new SearchHashtagsQuery("tra", 0, 0));

        Assert.False(result.IsValid);
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
