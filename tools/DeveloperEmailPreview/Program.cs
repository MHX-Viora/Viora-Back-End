using System.Text;
using Viora.Infrastructure.Developers;

// Offline preview only: no .env, credentials, database or network delivery.
var directory = Path.GetFullPath(args.Length > 0 ? args[0] : ".codex-tmp/email-preview");
Directory.CreateDirectory(directory);
const string syntheticToken = "SYNTHETIC_PREVIEW_ONLY_NOT_A_REAL_TOKEN_12345";
var examples = new Dictionary<string, Viora.Application.Developers.DeveloperEmailContent>
{
    ["verification"] = DeveloperEmailTemplates.Verification("Nguyễn Minh An", $"https://mxh.ankt.vn/developers/verify-email#token={syntheticToken}", 2026),
    ["reset"] = DeveloperEmailTemplates.PasswordReset("Nguyễn Minh An", $"http://localhost:8081/developers/reset-password#token={syntheticToken}", 2026),
    ["long-name"] = DeveloperEmailTemplates.Verification(new string('A', 120), $"https://mxh.ankt.vn/developers/verify-email#token={syntheticToken}", 2026)
};
foreach (var (name, content) in examples)
{
    await File.WriteAllTextAsync(Path.Combine(directory, name + ".html"), content.Html, new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(directory, name + ".txt"), content.Text, new UTF8Encoding(false));
    using var message = DeveloperEmailMessage.Create("mxh@baochi.online", "preview@example.test", "ANKT Developer — bản xem trước", content);
    await message.WriteToAsync(Path.Combine(directory, name + ".eml"));
}
Console.WriteLine($"Synthetic email previews: {directory}");
