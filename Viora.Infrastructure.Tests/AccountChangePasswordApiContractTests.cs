using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Accounts;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class AccountChangePasswordApiContractTests
{
    [Fact]
    public void Change_password_endpoint_contract_is_declared()
    {
        var method = typeof(AccountsController).GetMethod(nameof(AccountsController.ChangePassword))!;

        Assert.Equal("/api/account/change-password", method.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.NotNull(method.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(typeof(Task<ActionResult<ChangePasswordMessageResponse>>), method.ReturnType);

        var statuses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Order()
            .ToArray();
        Assert.Equal([200, 400, 401, 404], statuses);
    }

    [Fact]
    public void Change_password_request_and_response_do_not_expose_hashes()
    {
        AssertProperties<ChangePasswordRequest>("CurrentPassword", "NewPassword", "ConfirmPassword");
        AssertProperties<ChangePasswordMessageResponse>("Message");
        Assert.DoesNotContain(
            typeof(ChangePasswordMessageResponse).GetProperties(),
            property => property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Change_password_validator_enforces_required_rules()
    {
        var validator = new ChangePasswordValidator();

        var result = validator.Validate(new ChangePasswordCommand(
            Guid.NewGuid(),
            "",
            "Password1",
            "Mismatch1"));

        Assert.Contains(result.Errors, error => error.ErrorMessage == "Mật khẩu hiện tại không được để trống.");
        Assert.Contains(result.Errors, error => error.ErrorMessage == "Xác nhận mật khẩu không khớp.");
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
