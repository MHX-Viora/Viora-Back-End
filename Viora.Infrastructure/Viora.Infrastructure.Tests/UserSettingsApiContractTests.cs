using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Users;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class UserSettingsApiContractTests
{
    [Fact]
    public void Controller_exposes_authenticated_get_and_patch_routes()
    {
        Assert.NotNull(typeof(UserSettingsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/user-settings", typeof(UserSettingsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("", typeof(UserSettingsController).GetMethod(nameof(UserSettingsController.Get))!.GetCustomAttribute<HttpGetAttribute>()!.Template ?? "");
        Assert.Equal("", typeof(UserSettingsController).GetMethod(nameof(UserSettingsController.Update))!.GetCustomAttribute<HttpPatchAttribute>()!.Template ?? "");
    }

    [Fact]
    public void Contracts_do_not_accept_or_return_user_id()
    {
        AssertProperties<UserSettingsResponse>("IsPrivate", "AllowMessageEveryone", "AllowComment", "AllowMention", "Language", "Theme");
        AssertProperties<UpdateUserSettingsRequest>("IsPrivate", "AllowMessageEveryone", "AllowComment", "AllowMention", "Language", "Theme");
        Assert.DoesNotContain(typeof(UpdateUserSettingsRequest).GetProperties(), property => property.Name.Contains("UserId", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(UserSettingsResponse).GetProperties(), property => property.Name.Contains("UserId", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_validator_rejects_blank_language(string language)
    {
        var validator = new UpdateUserSettingsValidator();

        var result = await validator.ValidateAsync(new UpdateUserSettingsCommand(
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            language,
            null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Update_validator_rejects_unknown_theme()
    {
        var validator = new UpdateUserSettingsValidator();

        var result = await validator.ValidateAsync(new UpdateUserSettingsCommand(
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            null,
            "neon"));

        Assert.False(result.IsValid);
    }

    private static void AssertProperties<T>(params string[] expected) =>
        Assert.Equal(expected.Order(), typeof(T).GetProperties().Select(x => x.Name).Order());
}
