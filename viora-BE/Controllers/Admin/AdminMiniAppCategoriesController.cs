using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.MiniApps;

namespace viora_BE.Controllers.Admin;

[ApiController, Authorize(Roles = "2"), Route("api/admin/mini-app-categories")]
public sealed class AdminMiniAppCategoriesController(IMiniAppManagementService service) : ControllerBase
{
    private Guid AccountId() => Guid.Parse(User.FindFirstValue("sub")!);
    [HttpGet]
    public Task<IReadOnlyList<MiniAppCategoryDto>> List(CancellationToken ct) => service.GetCategoriesAsync(true, ct);
    [HttpPost]
    public Task<MiniAppCategoryDto> Create(MiniAppCategoryInput input, CancellationToken ct) => service.SaveCategoryAsync(AccountId(), null, input, ct);
    [HttpPut("{id:guid}")]
    public Task<MiniAppCategoryDto> Update(Guid id, MiniAppCategoryInput input, CancellationToken ct) => service.SaveCategoryAsync(AccountId(), id, input, ct);
}
