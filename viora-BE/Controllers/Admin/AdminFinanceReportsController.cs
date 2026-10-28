using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Wallets;

namespace viora_BE.Controllers.Admin;

[ApiController]
[Authorize(Roles = "2")]
[Route("api/admin/finance")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminFinanceReportsController(IAdminFinanceService finance) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<ActionResult<FinanceOverview>> Overview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        try { return Ok(await finance.GetOverviewAsync(from, to, cancellationToken)); }
        catch (WalletValidationException error) { return Invalid(error); }
    }

    [HttpGet("revenue")]
    public async Task<ActionResult<AdminPage<RevenueItem>>> Revenue([FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? source = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        try { return Ok(await finance.GetRevenueAsync(from, to, source, page, pageSize, cancellationToken)); }
        catch (WalletValidationException error) { return Invalid(error); }
    }

    [HttpGet("revenue/export")]
    public async Task<IActionResult> Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? source = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var period = FinancePeriod.Create(from, to);
            var csv = await finance.ExportRevenueAsync(from, to, source, cancellationToken);
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", $"doanh-thu-{period.From:yyyy-MM-dd}-{period.To:yyyy-MM-dd}.csv");
        }
        catch (WalletValidationException error) { return Invalid(error); }
    }

    [HttpGet("reconciliation")]
    public Task<AdminPage<FinanceReconciliationIssue>> Reconciliation([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        finance.GetReconciliationAsync(page, pageSize, cancellationToken);

    private UnprocessableEntityObjectResult Invalid(WalletValidationException error) =>
        UnprocessableEntity(new { error = new { code = error.Code, message = error.Message } });
}
