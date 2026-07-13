using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        Assert.Contains(typeof(UsersController).GetMethods(), method => method.GetCustomAttribute<HttpPutAttribute>() is not null);
    }

    [Fact]
    public void Profile_request_validation_is_on_record_constructor_parameters()
    {
        var constructor = Assert.Single(typeof(SaveUserProfileRequest).GetConstructors());

        Assert.All(
            constructor.GetParameters(),
            parameter => Assert.NotEmpty(parameter.GetCustomAttributes(typeof(ValidationAttribute), inherit: true)));
    }

    [Fact]
    public void Login_success_contract_does_not_expose_refresh_token()
    {
        Assert.DoesNotContain(
            typeof(LoginSuccessResponse).GetProperties(),
            property => property.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase));
    }
}
