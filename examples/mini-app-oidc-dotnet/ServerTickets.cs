using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;

// Demo only: replace with a shared, encrypted ticket store before multi-instance production.
public sealed class ServerTickets : ITicketStore
{
    private readonly ConcurrentDictionary<string, AuthenticationTicket> tickets = new();
    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        foreach (var entry in tickets.Where(entry => entry.Value.Properties.ExpiresUtc <= DateTimeOffset.UtcNow)) tickets.TryRemove(entry.Key, out _);
        if (tickets.Count >= 10000) throw new InvalidOperationException("Session capacity reached.");
        var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)); tickets[key] = ticket; return Task.FromResult(key);
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket) { tickets[key] = ticket; return Task.CompletedTask; }
    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        tickets.TryGetValue(key, out var ticket);
        if (ticket?.Properties.ExpiresUtc <= DateTimeOffset.UtcNow) { tickets.TryRemove(key, out _); ticket = null; }
        return Task.FromResult(ticket);
    }
    public Task RemoveAsync(string key) { tickets.TryRemove(key, out _); return Task.CompletedTask; }
}
