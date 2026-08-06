using System.Data;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Accounts;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class GoogleLoginRepository(AppDbContext dbContext)
    : IGoogleLoginRepository
{
    private const string GoogleProvider = "google.com";

    public async Task<Account> ResolveAccountAsync(
        GoogleVerifiedIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var externalLogin = await dbContext.ExternalLogins
            .Include(login => login.Account)
            .ThenInclude(account => account.User)
            .SingleOrDefaultAsync(
                login => login.Provider == GoogleProvider &&
                    login.ProviderSubject == identity.ProviderSubject,
                cancellationToken);
        if (externalLogin is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return externalLogin.Account;
        }

        var account = await dbContext.Accounts
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.Email == identity.Email,
                cancellationToken);
        if (account is null)
        {
            account = new Account
            {
                Email = identity.Email,
                PasswordHash = null,
                Role = AccountRole.User,
                Status = AccountStatus.Active
            };
            dbContext.Accounts.Add(account);
        }

        dbContext.ExternalLogins.Add(new ExternalLogin
        {
            Account = account,
            Provider = GoogleProvider,
            ProviderSubject = identity.ProviderSubject
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    public async Task CompleteLoginAsync(
        Account account,
        RefreshToken refreshToken,
        DateTime loginAt,
        CancellationToken cancellationToken)
    {
        account.LastLoginAt = loginAt;
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
