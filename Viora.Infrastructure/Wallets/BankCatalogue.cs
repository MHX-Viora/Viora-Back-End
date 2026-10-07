using System.Text.Json;
using Viora.Application.Wallets;

namespace Viora.Infrastructure.Wallets;

// Snapshot from https://api.vietqr.io/v2/banks; transfer-supported banks only.
public static class BankCatalogue
{
    public static readonly IReadOnlyList<BankCatalogueItem> Items = Load();
    private static IReadOnlyList<BankCatalogueItem> Load()
    {
        using var stream = typeof(BankCatalogue).Assembly.GetManifestResourceStream("Viora.Infrastructure.Wallets.banks.json")!;
        return JsonSerializer.Deserialize<BankCatalogueItem[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
    public static BankCatalogueItem? Find(string code) => Items.FirstOrDefault(bank =>
        string.Equals(bank.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(bank.ShortName, code?.Trim(), StringComparison.OrdinalIgnoreCase) || bank.Bin == code?.Trim());
}
