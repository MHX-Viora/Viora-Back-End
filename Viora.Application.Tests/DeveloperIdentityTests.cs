using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viora.Application.Developers;
using Viora.Domain.Developers;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;
using Xunit;

public sealed class DeveloperIdentityTests
{
    private const string Password = "A long developer password 2026!";
    private static readonly DeveloperRequestContext Context = new("127.0.0.1", "Identity integration test");

    [Fact]
    public async Task RegisterVerifyLoginAndRevokeUsesRealDatabaseAndPasswordHasher()
    {
        await using var f = await Fixture.Create();
        await f.Register();
        var account = await f.Db.DeveloperAccounts.SingleAsync();
        Assert.NotEqual(Password, account.PasswordHash);
        Assert.Equal(DeveloperStatus.PendingVerification, account.Status);
        Assert.Equal("DEVELOPER@EXAMPLE.TEST", account.EmailNormalized);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.LoginAsync(new(account.EmailNormalized, Password), Context, default));
        var token = f.Mail.Token;
        Assert.DoesNotContain(token, (await f.Db.DeveloperActionTokens.SingleAsync()).TokenHash);
        await f.Service.VerifyEmailAsync(token, Context, default);
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.VerifyEmailAsync(token, Context, default));
        var login = await f.Service.LoginAsync(new(account.EmailNormalized, Password, true), Context, default);
        Assert.NotEqual(login.SessionToken, (await f.Db.DeveloperSessions.SingleAsync()).TokenHash);
        Assert.NotNull(await f.Service.AuthenticateAsync(login.SessionToken, default));
        var session = await f.Db.DeveloperSessions.SingleAsync();
        var refresh = await f.Service.RefreshAsync(session.Id, default);
        Assert.True(refresh.ExpiresAt <= session.AbsoluteExpiresAt);
        await f.Service.LogoutAsync(session.Id, false, Context, default);
        Assert.Null(await f.Service.AuthenticateAsync(login.SessionToken, default));
        Assert.Empty(await f.Db.Accounts.ToListAsync());
    }

    [Fact]
    public async Task DuplicateRegistrationIsNeutralAndDoesNotOverwriteAccount()
    {
        await using var f = await Fixture.Create();
        await f.Register(); var original = (await f.Db.DeveloperAccounts.SingleAsync()).PasswordHash;
        await f.Register();
        Assert.Single(await f.Db.DeveloperAccounts.ToListAsync());
        Assert.Equal(original, (await f.Db.DeveloperAccounts.SingleAsync()).PasswordHash);
        Assert.Equal(1, f.Mail.Count);
    }

    [Theory]
    [InlineData("bad", Password, true)]
    [InlineData("developer@example.test", "weak", true)]
    [InlineData("developer@example.test", Password, false)]
    public async Task RegistrationRejectsInvalidInput(string email, string password, bool terms)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.RegisterAsync(new("Developer", email, password, password, terms), Context, default));
        Assert.Empty(await f.Db.DeveloperAccounts.ToListAsync());
    }

    [Fact]
    public async Task ExpiredAndWrongPurposeTokensCannotVerifyOrReset()
    {
        await using var f = await Fixture.Create(); await f.Register();
        var raw = f.Mail.Token;
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.ResetPasswordAsync(new(raw, Password, Password), Context, default));
        await f.Db.DeveloperActionTokens.ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.VerifyEmailAsync(raw, Context, default));
    }

    [Fact]
    public async Task PasswordResetIsSingleUseAndRevokesAllSessions()
    {
        await using var f = await Fixture.Create(); var login = await f.ActiveLogin();
        await f.Service.SendEmailActionAsync("developer@example.test", true, Context, default);
        var token = f.Mail.Token; const string next = "Another developer password 2026!";
        await f.Service.ResetPasswordAsync(new(token, next, next), Context, default);
        Assert.Null(await f.Service.AuthenticateAsync(login.SessionToken, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.ResetPasswordAsync(new(token, Password, Password), Context, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.LoginAsync(new("developer@example.test", Password), Context, default));
        Assert.NotNull(await f.Service.LoginAsync(new("developer@example.test", next), Context, default));
    }

    [Fact]
    public async Task WrongPasswordLockoutAndSuspensionRejectLoginAndExistingSessions()
    {
        await using var f = await Fixture.Create(); var login = await f.ActiveLogin();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.LoginAsync(new("developer@example.test", "incorrect password"), Context, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.LoginAsync(new("developer@example.test", Password), Context, default));
        await f.Db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, DeveloperStatus.Suspended));
        Assert.Null(await f.Service.AuthenticateAsync(login.SessionToken, default));
    }

    [Fact]
    public async Task PasswordChangeRequiresCurrentPasswordAndRevokesOtherSessions()
    {
        await using var f = await Fixture.Create(); var login = await f.ActiveLogin();
        var id = login.Account.Id; const string next = "Changed developer password 2026!";
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.ChangePasswordAsync(id, new("wrong", next, next), Context, default));
        await f.Service.ChangePasswordAsync(id, new(Password, next, next), Context, default);
        Assert.Null(await f.Service.AuthenticateAsync(login.SessionToken, default));
        Assert.NotNull(await f.Service.LoginAsync(new("developer@example.test", next), Context, default));
    }

    [Fact]
    public async Task ProfileAndSessionOwnershipAreEnforced()
    {
        await using var f = await Fixture.Create(); var login = await f.ActiveLogin();
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.UpdateProfileAsync(login.Account.Id, new("Dev", "javascript:alert(1)"), Context, default));
        var changed = await f.Service.UpdateProfileAsync(login.Account.Id, new("New Developer", "https://example.test/avatar.png"), Context, default);
        Assert.Equal("New Developer", changed.FullName);
        Assert.Empty(await f.Service.SessionsAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        var session = await f.Db.DeveloperSessions.SingleAsync();
        await f.Service.RevokeSessionAsync(Guid.NewGuid(), session.Id, Context, default);
        Assert.NotNull(await f.Service.AuthenticateAsync(login.SessionToken, default));
        await f.Service.RevokeSessionAsync(login.Account.Id, session.Id, Context, default);
        Assert.Null(await f.Service.AuthenticateAsync(login.SessionToken, default));
    }

    [Fact]
    public async Task ExpiredSessionsAndLastLoginMethodAreProtected()
    {
        await using var f = await Fixture.Create(); var login = await f.ActiveLogin();
        var session = await f.Db.DeveloperSessions.AsNoTracking().SingleAsync();
        await f.Db.DeveloperSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null(await f.Service.AuthenticateAsync(login.SessionToken, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.RefreshAsync(session.Id, default));
        await f.Db.DeveloperAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.PasswordHash, (string?)null));
        var identity = new DeveloperExternalIdentity { DeveloperAccountId = login.Account.Id, Provider = "ankt", ProviderSubjectId = "test-only-subject" };
        f.Db.Add(identity); await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.UnlinkAsync(login.Account.Id, identity.Id, Password, Context, default));
        Assert.Equal("LAST_LOGIN_METHOD", error.Code);
        Assert.Single(await f.Db.DeveloperExternalIdentities.ToListAsync());
    }

    [Fact]
    public async Task CurrentPasswordReauthenticationSharesPersistentAccountLockout()
    {
        await using var f = await Fixture.Create(); var login = await f.ActiveLogin();
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.ChangePasswordAsync(login.Account.Id, new("wrong", Password, Password), Context, default));
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.ChangePasswordAsync(login.Account.Id, new(Password, Password, Password), Context, default));
        Assert.True((await f.Db.DeveloperAccounts.AsNoTracking().SingleAsync()).LockedUntil > DateTime.UtcNow);
    }

    [Fact]
    public async Task EmailResendsAreCooledDownAndUnknownEmailIsNeutral()
    {
        await using var f = await Fixture.Create(); await f.Register();
        await f.Service.SendEmailActionAsync("developer@example.test", false, Context, default);
        await f.Service.SendEmailActionAsync("absent@example.test", true, Context, default);
        Assert.Equal(1, f.Mail.Count);
    }

    [Fact]
    public async Task PasswordChangeCannotClearALockPersistedAfterPasswordWasChecked()
    {
        var race = new LockBeforePasswordUpdate();
        await using var f = await Fixture.Create(race); var login = await f.ActiveLogin();
        race.Enabled = true;
        await Assert.ThrowsAsync<DeveloperAuthException>(() => f.Service.ChangePasswordAsync(login.Account.Id,
            new(Password, "New password for race test", "New password for race test"), Context, default));
        Assert.NotNull(await f.Service.AuthenticateAsync(login.SessionToken, default));
    }

    // Deterministically interleave a concurrent lock immediately before the password CAS.
    private sealed class LockBeforePasswordUpdate : DbCommandInterceptor
    {
        public bool Enabled;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled && command.CommandText.StartsWith("UPDATE \"DeveloperAccounts\"") && command.CommandText.Contains("SET \"UpdatedAt\""))
            {
                Enabled = false;
                await using var update = command.Connection!.CreateCommand(); update.Transaction = command.Transaction;
                update.CommandText = "UPDATE \"DeveloperAccounts\" SET \"LockedUntil\" = $until, \"FailedLoginAttempts\" = 5";
                var parameter = update.CreateParameter(); parameter.ParameterName = "$until"; parameter.Value = DateTime.UtcNow.AddMinutes(15); update.Parameters.Add(parameter);
                await update.ExecuteNonQueryAsync(ct);
            }
            return result;
        }
    }

    private sealed class Mail : IDeveloperEmailSender
    {
        public bool IsConfigured => true;
        public int Count;
        public string Token = "";
        public Task SendAsync(string recipient, string subject, string text, CancellationToken ct)
        {
            Count++; Token = text.Split("#token=")[1].Split('\n')[0].Trim(); return Task.CompletedTask;
        }
    }
    private sealed class Fixture(SqliteConnection connection, AppDbContext db, Mail mail, DeveloperIdentityService service) : IAsyncDisposable
    {
        public AppDbContext Db => db;
        public Mail Mail => mail;
        public DeveloperIdentityService Service => service;
        public static async Task<Fixture> Create(DbCommandInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var db = new AppDbContext(options.Options);
            await db.Database.EnsureCreatedAsync(); var mail = new Mail();
            var service = new DeveloperIdentityService(db, mail, Options.Create(new DeveloperAuthOptions { PortalUrl = "https://mxh.ankt.vn/developers" }), TimeProvider.System, NullLogger<DeveloperIdentityService>.Instance);
            return new(connection, db, mail, service);
        }
        public Task Register() => Service.RegisterAsync(new("Developer Test", "developer@example.test", Password, Password, true), Context, default);
        public async Task<DeveloperLoginResult> ActiveLogin() { await Register(); await Service.VerifyEmailAsync(Mail.Token, Context, default); return await Service.LoginAsync(new("developer@example.test", Password), Context, default); }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
