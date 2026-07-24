using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Notifications;
using Viora.Domain.Entities;
using viora_BE.Controllers;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class NotificationApiContractTests
{
    [Fact]
    public void Notifications_controller_exposes_authenticated_routes()
    {
        Assert.NotNull(typeof(NotificationsController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/notifications", typeof(NotificationsController).GetCustomAttribute<RouteAttribute>()!.Template);

        var methods = typeof(NotificationsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(NotificationsController))
            .ToDictionary(method => method.Name);

        Assert.NotNull(methods[nameof(NotificationsController.List)].GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal("{id:guid}/read", methods[nameof(NotificationsController.MarkRead)].GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("read-all", methods[nameof(NotificationsController.MarkAllRead)].GetCustomAttribute<HttpPutAttribute>()!.Template);
    }

    [Fact]
    public void Notifications_list_query_parameters_match_contract()
    {
        var action = typeof(NotificationsController).GetMethod(nameof(NotificationsController.List))!;
        var parameters = action.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.Equal(typeof(int), parameters["page"].ParameterType);
        Assert.Equal(1, parameters["page"].DefaultValue);
        Assert.Equal(typeof(int), parameters["pageSize"].ParameterType);
        Assert.Equal(20, parameters["pageSize"].DefaultValue);
        Assert.Equal(typeof(bool?), parameters["isRead"].ParameterType);
        Assert.Equal(typeof(NotificationType?), parameters["type"].ParameterType);
    }

    [Fact]
    public void Notification_response_contract_has_only_expected_fields()
    {
        AssertProperties<NotificationListResponse>(
            "Page",
            "PageSize",
            "TotalItems",
            "TotalPages",
            "UnreadCount",
            "Items");
        AssertProperties<NotificationItemResponse>(
            "Id",
            "Type",
            "Title",
            "Content",
            "ImageUrl",
            "IsRead",
            "CreatedAt",
            "Sender",
            "Reference");
        AssertProperties<NotificationSenderResponse>("Id", "DisplayName", "AvatarUrl", "IsVerified");
        AssertProperties<NotificationReferenceResponse>("Id", "Type");
        AssertProperties<MarkNotificationReadResponse>("IsRead");
        AssertProperties<MarkAllNotificationsReadResponse>("UpdatedCount");
    }

    [Fact]
    public void Notification_actions_document_expected_status_codes()
    {
        var markRead = typeof(NotificationsController).GetMethod(nameof(NotificationsController.MarkRead))!;
        Assert.Contains(
            markRead.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound && attribute.Type == typeof(ProblemDetails));

        var markAllRead = typeof(NotificationsController).GetMethod(nameof(NotificationsController.MarkAllRead))!;
        Assert.Contains(
            markAllRead.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK && attribute.Type == typeof(MarkAllNotificationsReadResponse));
    }

    private static void AssertProperties<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).Order().ToArray();
        Assert.Equal(names.Order().ToArray(), properties);
    }
}
