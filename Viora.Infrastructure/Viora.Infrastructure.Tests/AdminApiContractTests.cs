using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Viora.Application.Admin;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class AdminApiContractTests
{
    [Fact]
    public void Admin_controller_requires_admin_role()
    {
        var authorize = Assert.Single(typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("api/admin", typeof(AdminController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("2", authorize.Roles);
    }

    [Fact]
    public void Admin_list_contracts_use_unified_paging_shape()
    {
        AssertProperties<AdminApiResponse<AdminDashboardResponse>>("Success", "Message", "Data");
        AssertProperties<AdminPagedResponse<AdminUserSummaryResponse>>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<AdminPagedResponse<AdminPostSummaryResponse>>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
        AssertProperties<AdminPagedResponse<AdminLogSummaryResponse>>("Page", "PageSize", "TotalItems", "TotalPages", "Items");
    }

    [Fact]
    public void Admin_controller_actions_return_wrapped_response_contracts()
    {
        var methods = typeof(AdminController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(AdminController))
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();

        Assert.All(methods, method =>
        {
            var returnType = method.ReturnType;
            Assert.True(returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>));

            var actionResultType = returnType.GetGenericArguments()[0];
            Assert.True(actionResultType.IsGenericType && actionResultType.GetGenericTypeDefinition() == typeof(ActionResult<>));

            var responseType = actionResultType.GetGenericArguments()[0];
            Assert.True(responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(AdminApiResponse<>));
        });
    }

    [Fact]
    public void Admin_controller_exposes_required_list_routes()
    {
        var methods = typeof(AdminController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(AdminController))
            .ToDictionary(method => method.Name);

        Assert.Equal("users", methods[nameof(AdminController.Users)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("identities", methods[nameof(AdminController.Identities)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("posts", methods[nameof(AdminController.Posts)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("videos", methods[nameof(AdminController.Videos)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("reports", methods[nameof(AdminController.Reports)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("hashtags", methods[nameof(AdminController.Hashtags)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("conversations", methods[nameof(AdminController.Conversations)].GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("logs", methods[nameof(AdminController.Logs)].GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
