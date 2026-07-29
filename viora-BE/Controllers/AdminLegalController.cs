using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Legal;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/admin/legal")]
[Authorize(Roles = "2")]
public sealed class AdminLegalController(ILegalDocumentRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken token) => Ok(await repository.GetAllAsync(token));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken token)
    {
        var value = await repository.GetByIdAsync(id, token);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveLegalDocumentRequest request, CancellationToken token)
    {
        if (!TryAdmin(out var adminId)) return Unauthorized();
        var result = await repository.CreateAsync(adminId, request, token);
        return Result(result, created: true);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> CreateVersion(Guid id, SaveLegalDocumentRequest request, CancellationToken token)
    {
        if (!TryAdmin(out var adminId)) return Unauthorized();
        return Result(await repository.CreateVersionAsync(adminId, id, request, token), created: true);
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken token)
    {
        if (!TryAdmin(out var adminId)) return Unauthorized();
        return Result(await repository.PublishAsync(adminId, id, token));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken token) =>
        Result(await repository.DeleteDraftAsync(id, token));

    private bool TryAdmin(out Guid id) => Guid.TryParse(User.FindFirstValue("user_id"), out id);

    private IActionResult Result(LegalMutationResult result, bool created = false)
    {
        if (result.Success)
            return created && result.Document is not null
                ? CreatedAtAction(nameof(Detail), new { id = result.Document.Id }, result.Document)
                : Ok(result.Document ?? (object)new { success = true });
        var status = result.Error switch
        {
            LegalMutationError.NotFound => 404,
            LegalMutationError.Conflict => 409,
            _ => 400
        };
        return Problem(statusCode: status, title: "Legal document mutation failed.", detail: result.Message);
    }
}
