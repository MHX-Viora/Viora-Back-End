using System.ComponentModel.DataAnnotations;
using FluentValidation;
using FluentValidationException = FluentValidation.ValidationException;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Viora.Application.Hashtags;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/hashtags")]
[Authorize]
public sealed class HashtagsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<HashtagResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        CreateHashtagRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(new CreateHashtagCommand(request.Name), cancellationToken);
            return Ok(response);
        }
        catch (FluentValidationException exception)
        {
            return BadRequestProblem("INVALID_HASHTAG", FirstError(exception));
        }
    }

    [HttpGet]
    [ProducesResponseType<HashtagSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Search(
        [FromQuery] string? keyword = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await mediator.Send(new SearchHashtagsQuery(keyword, page, pageSize), cancellationToken);
            return Ok(response);
        }
        catch (FluentValidationException exception)
        {
            return BadRequestProblem("INVALID_HASHTAG_SEARCH", FirstError(exception));
        }
    }

    private static string FirstError(FluentValidationException exception) =>
        exception.Errors.FirstOrDefault()?.ErrorMessage ?? "Du lieu khong hop le.";

    private ObjectResult BadRequestProblem(string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid hashtag request",
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status400BadRequest };
    }
}

public sealed record CreateHashtagRequest([param: Required, MaxLength(100)] string Name);
