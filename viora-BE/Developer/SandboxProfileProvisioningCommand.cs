using System.Text.Json;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Viora.Infrastructure.Media;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

// Explicit operator command, never called during web-host startup or migration.
public static class SandboxProfileProvisioningCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, string output, CancellationToken ct = default)
    {
        var options = configuration.GetSection("Cloudinary").Get<CloudinaryOptions>() ?? new();
        if (new[] { options.CloudName, options.ApiKey, options.ApiSecret }.Any(string.IsNullOrWhiteSpace))
        { Console.Error.WriteLine("Cloudinary configuration is missing; no upload performed."); return 1; }
        try
        {
            var cloud = new Cloudinary(new Account(options.CloudName, options.ApiKey, options.ApiSecret)); cloud.Api.Secure = true;
            async Task<string> Asset(string name)
            {
                var id = "ankt/sandbox/" + name + "-v1";
                var existing = await cloud.GetResourceAsync(new GetResourceParams(id), ct);
                if (existing.Error == null && SandboxTestOptions.CloudinaryUrl(existing.SecureUrl)) return existing.SecureUrl;
                if (existing.StatusCode != System.Net.HttpStatusCode.NotFound)
                    throw new InvalidOperationException("Cannot verify existing Cloudinary asset; upload aborted.");
                using var asset = typeof(SandboxProfileProvisioningCommand).Assembly.GetManifestResourceStream("Ankt.SandboxProfile." + name)
                    ?? throw new InvalidOperationException("Packaged Sandbox profile asset is missing.");
                var uploaded = await cloud.UploadAsync(new ImageUploadParams
                { File = new FileDescription(name + ".svg", asset), PublicId = id, Overwrite = false, UniqueFilename = false, UseFilename = false }, ct);
                var url = uploaded.SecureUrl?.AbsoluteUri;
                if (uploaded.Error != null || !SandboxTestOptions.CloudinaryUrl(url)) throw new InvalidOperationException("Cloudinary upload did not return a valid asset URL.");
                return url!;
            }
            var avatar = await Asset("avatar"); var cover = await Asset("cover");
            var result = new { avatarUrl = avatar, coverUrl = cover };
            var target = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
            Console.WriteLine("Sandbox profile assets verified on Cloudinary; URL manifest written to " + target);
            return 0;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        { Console.Error.WriteLine("Sandbox asset provisioning failed (" + e.GetType().Name + "). No success or placeholder URLs were recorded."); return 1; }
    }
}
