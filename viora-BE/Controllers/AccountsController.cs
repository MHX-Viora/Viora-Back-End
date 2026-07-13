using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Viora.Application.Accounts;
using Viora.Domain.Entities;
using Viora.Application.Users;

namespace viora_BE.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(IAccountService accountService) : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";
    private const string RefreshTokenCookiePath = "/api/accounts/refresh-token";

    [HttpGet]
    [ProducesResponseType<PagedAccountResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedAccountResponse>> List(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        Ok(await accountService.ListAsync(page, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAsync(id, cancellationToken);
        return account is null
            ? NotFoundProblem("ACCOUNT_NOT_FOUND", "Account was not found.")
            : Ok(account);
    }

    [HttpPost("register")]
    [ProducesResponseType<RegisterAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterAccountResponse>> Register(
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await accountService.RegisterAsync(
                new RegisterAccountCommand(request.Identifier, request.Password),
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new RegisterAccountResponse("Register success"));
        }
        catch (AccountValidationException exception)
        {
            return ProblemResult(StatusCodes.Status400BadRequest, "Invalid account", exception.Message, exception.Code);
        }
        catch (AccountConflictException exception)
        {
            return ConflictProblem(exception);
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<LoginSuccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<LoginMessageResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<LoginMessageResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Login(
        LoginAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.LoginAsync(
            new LoginAccountCommand(request.Identifier, request.Password),
            cancellationToken);

        if (result.Outcome == LoginOutcome.Active)
        {
            SetRefreshTokenCookie(result.Tokens!.RefreshToken);
            return Ok(new LoginSuccessResponse(
                result.Status!.Value,
                result.Tokens!.AccessToken,
                result.User));
        }

        return result.Outcome switch
        {
            LoginOutcome.Banned or LoginOutcome.Deleted => StatusCode(
                StatusCodes.Status403Forbidden,
                new LoginMessageResponse(result.Status, result.Message!)),
            _ => Unauthorized(new LoginMessageResponse(null, result.Message!))
        };
    }

    [HttpPost("refresh-token")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<RefreshTokenSuccessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<LoginMessageResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> RefreshToken(
        CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken))
        {
            return Unauthorized(new LoginMessageResponse(null, "Refresh token không hợp lệ hoặc đã hết hạn."));
        }

        var result = await accountService.RefreshTokenAsync(
            new RefreshAccountTokenCommand(refreshToken),
            cancellationToken);

        if (result.Outcome != RefreshTokenOutcome.Active)
        {
            Response.Cookies.Delete(RefreshTokenCookieName, RefreshTokenCookieOptions());
            return Unauthorized(new LoginMessageResponse(null, result.Message!));
        }

        SetRefreshTokenCookie(result.Tokens!.RefreshToken);
        return Ok(new RefreshTokenSuccessResponse(result.Tokens.AccessToken));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AccountResponse>> Update(
        Guid id,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var account = await accountService.UpdateAsync(
                id,
                new UpdateAccountCommand(request.Email, request.Phone, request.Password),
                cancellationToken);
            return account is null
                ? NotFoundProblem("ACCOUNT_NOT_FOUND", "Account was not found.")
                : Ok(account);
        }
        catch (AccountConflictException exception)
        {
            return ConflictProblem(exception);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await accountService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    private ObjectResult ConflictProblem(AccountConflictException exception) =>
        ProblemResult(StatusCodes.Status409Conflict, "Account conflict", exception.Message, exception.Code);

    private ObjectResult NotFoundProblem(string code, string detail) =>
        ProblemResult(StatusCodes.Status404NotFound, "Account not found", detail, code);

    private static ObjectResult ProblemResult(int status, string title, string detail, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }

    private void SetRefreshTokenCookie(string refreshToken) =>
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, RefreshTokenCookieOptions());

    private static CookieOptions RefreshTokenCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshTokenCookiePath
    };
}

public sealed record RegisterAccountRequest(
    [param: Required, MaxLength(255)] string Identifier,
    [param: Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record RegisterAccountResponse(string Message);

public sealed record LoginAccountRequest(
    [param: Required, MaxLength(255)] string Identifier,
    [param: Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record LoginSuccessResponse(
    AccountStatus Status,
    string AccessToken,
    UserResponse? User);

public sealed record RefreshTokenSuccessResponse(string AccessToken);

public sealed record LoginMessageResponse(AccountStatus? Status, string Message);

public sealed record UpdateAccountRequest(
    [param: EmailAddress, MaxLength(255)] string? Email,
    [param: MaxLength(20)] string? Phone,
    [param: StringLength(128, MinimumLength = 8)] string? Password);
