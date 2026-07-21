using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Viora.Infrastructure.Realtime;

namespace viora_BE.Controllers;

[ApiController]
[Authorize]
[Route("api/dev/push-test")]
public sealed class DevPushDiagnosticsController(
    IHostEnvironment environment,
    IDeviceTokenRepository deviceTokenRepository,
    IFirebaseMessagingClientFactory firebaseMessagingClientFactory,
    ILogger<DevPushDiagnosticsController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<DevPushTestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Send(
        [FromBody] DevPushTestRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (request.UserId.HasValue == !string.IsNullOrWhiteSpace(request.TokenSuffix))
        {
            return BadRequest(new { message = "Provide exactly one of userId or tokenSuffix." });
        }

        var client = firebaseMessagingClientFactory.CreateClient();
        if (client is null)
        {
            logger.LogError(
                "Dev push test failed because Firebase app is not configured. FirebaseProjectId: {FirebaseProjectId}.",
                firebaseMessagingClientFactory.ProjectId ?? "unknown");
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { message = "Firebase app is not configured.", firebaseProjectId = firebaseMessagingClientFactory.ProjectId });
        }

        var tokens = request.UserId.HasValue
            ? await deviceTokenRepository.GetActiveByUserIdAsync(request.UserId.Value, cancellationToken)
            : await deviceTokenRepository.GetActiveByTokenSuffixAsync(request.TokenSuffix!.Trim(), cancellationToken);

        var validTokens = tokens
            .Where(token => !string.IsNullOrWhiteSpace(token.Token))
            .ToArray();

        logger.LogInformation(
            "Dev push test dispatch started. UserId: {UserId}, TokenSuffix: {TokenSuffix}, ActiveTokenCount: {ActiveTokenCount}, FirebaseProjectId: {FirebaseProjectId}.",
            request.UserId,
            request.TokenSuffix,
            tokens.Count,
            firebaseMessagingClientFactory.ProjectId ?? "unknown");

        var results = new List<DevPushTestTokenResult>();
        foreach (var token in validTokens)
        {
            var suffix = GetTokenSuffix(token.Token);
            var message = new PushMessage(
                token.UserId,
                request.Title ?? "Viora test push",
                request.Body ?? "Firebase diagnostic test",
                new Dictionary<string, string>
                {
                    ["type"] = "dev_push_test",
                    ["diagnostic"] = "true",
                    ["userId"] = token.UserId.ToString()
                });

            try
            {
                var messageId = await client.SendAsync(message, token.Token, cancellationToken);
                logger.LogInformation(
                    "Dev push test sent. UserId: {UserId}, TokenSuffix: {TokenSuffix}, FirebaseProjectId: {FirebaseProjectId}, FirebaseMessageId: {FirebaseMessageId}.",
                    token.UserId,
                    suffix,
                    firebaseMessagingClientFactory.ProjectId ?? "unknown",
                    messageId);
                results.Add(new DevPushTestTokenResult(token.UserId, suffix, true, messageId, null, false));
            }
            catch (FirebasePushTokenInvalidException exception)
            {
                await deviceTokenRepository.DeactivateAsync(token.Token, cancellationToken);
                logger.LogWarning(
                    exception,
                    "Dev push test token invalid and deactivated. UserId: {UserId}, TokenSuffix: {TokenSuffix}, FirebaseProjectId: {FirebaseProjectId}, MessagingErrorCode: {MessagingErrorCode}.",
                    token.UserId,
                    suffix,
                    firebaseMessagingClientFactory.ProjectId ?? "unknown",
                    exception.FirebaseError);
                results.Add(new DevPushTestTokenResult(token.UserId, suffix, false, null, exception.FirebaseError, true));
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Dev push test failed. UserId: {UserId}, TokenSuffix: {TokenSuffix}, FirebaseProjectId: {FirebaseProjectId}, ErrorType: {ErrorType}.",
                    token.UserId,
                    suffix,
                    firebaseMessagingClientFactory.ProjectId ?? "unknown",
                    exception.GetType().Name);
                results.Add(new DevPushTestTokenResult(token.UserId, suffix, false, null, exception.GetType().Name, false));
            }
        }

        return Ok(new DevPushTestResponse(
            firebaseMessagingClientFactory.ProjectId,
            tokens.Count,
            validTokens.Length,
            results));
    }

    private static string GetTokenSuffix(string token) =>
        token.Length <= 8 ? token : token[^8..];
}

public sealed record DevPushTestRequest(
    Guid? UserId,
    [property: StringLength(64, MinimumLength = 4)] string? TokenSuffix,
    string? Title,
    string? Body);

public sealed record DevPushTestResponse(
    string? FirebaseProjectId,
    int ActiveTokenCount,
    int ValidTokenCount,
    IReadOnlyList<DevPushTestTokenResult> Results);

public sealed record DevPushTestTokenResult(
    Guid UserId,
    string TokenSuffix,
    bool Success,
    string? FirebaseMessageId,
    string? MessagingErrorCode,
    bool Deactivated);
