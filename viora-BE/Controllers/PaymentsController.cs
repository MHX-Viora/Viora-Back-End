using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Wallets;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("payos/webhook")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> PayOsWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            await paymentService.HandlePayOsWebhookAsync(payload, cancellationToken);
            return Ok(new { success = true });
        }
        catch (WalletValidationException exception)
        {
            return UnprocessableEntity(new { error = new { code = exception.Code, message = exception.Message } });
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { error = new { code = "INVALID_JSON", message = "Webhook không hợp lệ." } });
        }
    }
}
