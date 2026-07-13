using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using viora_BE.Controllers;
using viora_BE.OpenApi;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class SwaggerAuthorizationOperationFilterTests
{
    [Theory]
    [InlineData(nameof(AccountsController.Login))]
    [InlineData(nameof(AccountsController.Register))]
    public void Public_account_operations_do_not_require_bearer_token(string methodName)
    {
        var operation = Apply(typeof(AccountsController), methodName);

        Assert.Empty(operation.Security);
    }

    [Fact]
    public void Profile_operations_require_bearer_token()
    {
        var operation = Apply(typeof(UsersController), nameof(UsersController.Create));

        Assert.Single(operation.Security);
    }

    private static OpenApiOperation Apply(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName)!;
        var operation = new OpenApiOperation();
        var context = new OperationFilterContext(
            new ApiDescription(),
            null!,
            new SchemaRepository(),
            method);

        new AuthorizeOperationFilter().Apply(operation, context);
        return operation;
    }
}
