using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class UserProfileApiContractTests
{
    [Fact]
    public void Profile_controller_requires_bearer_authentication()
    {
        Assert.NotNull(typeof(UsersController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Contains(typeof(UsersController).GetMethods(), method => method.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.Contains(typeof(UsersController).GetMethods(), method => method.GetCustomAttribute<HttpPatchAttribute>() is not null);
        Assert.DoesNotContain(typeof(UsersController).GetMethods(), method => method.GetCustomAttribute<HttpPutAttribute>() is not null);
    }

    [Fact]
    public void Profile_exposes_only_one_multipart_post_and_one_multipart_patch()
    {
        var actions = typeof(UsersController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(UsersController))
            .Where(method => method.GetCustomAttribute<HttpPostAttribute>() is not null ||
                             method.GetCustomAttribute<HttpPatchAttribute>() is not null)
            .ToArray();

        Assert.Single(actions, method => method.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.Single(actions, method => method.GetCustomAttribute<HttpPatchAttribute>() is not null);
        Assert.All(actions, method =>
            Assert.Contains("multipart/form-data", method.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes));
        Assert.DoesNotContain(actions, method =>
            method.GetCustomAttribute<HttpPatchAttribute>()?.Template is "avatar" or "cover");
    }

    [Fact]
    public void Profile_create_contract_accepts_fields_and_files_but_not_urls()
    {
        var parameters = Assert.Single(typeof(SaveUserProfileRequest).GetConstructors()).GetParameters();

        Assert.NotEmpty(parameters.Single(x => x.Name!.Equals("displayName", StringComparison.OrdinalIgnoreCase))
            .GetCustomAttributes(typeof(ValidationAttribute), true));
        var gender = parameters.Single(x => x.Name!.Equals("gender", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(typeof(Gender?), gender.ParameterType);
        Assert.NotEmpty(gender.GetCustomAttributes(typeof(RequiredAttribute), true));
        Assert.Equal(typeof(IFormFile), parameters.Single(x => x.Name!.Equals("avatar", StringComparison.OrdinalIgnoreCase)).ParameterType);
        Assert.Equal(typeof(IFormFile), parameters.Single(x => x.Name!.Equals("cover", StringComparison.OrdinalIgnoreCase)).ParameterType);
        Assert.DoesNotContain(parameters, x => x.Name!.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Profile_patch_contract_accepts_partial_fields_and_files_but_not_urls()
    {
        var parameters = Assert.Single(typeof(UpdateUserProfileRequest).GetConstructors()).GetParameters();

        Assert.Equal(typeof(IFormFile), parameters.Single(x => x.Name!.Equals("avatar", StringComparison.OrdinalIgnoreCase)).ParameterType);
        Assert.Equal(typeof(IFormFile), parameters.Single(x => x.Name!.Equals("cover", StringComparison.OrdinalIgnoreCase)).ParameterType);
        Assert.DoesNotContain(parameters, x => x.Name!.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Authentication_success_contracts_do_not_expose_refresh_token()
    {
        Assert.DoesNotContain(
            typeof(LoginSuccessResponse).GetProperties(),
            property => property.Name.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(RefreshTokenSuccessResponse).GetProperties(),
            property => property.Name.Contains("RefreshToken", StringComparison.OrdinalIgnoreCase));
    }
}
