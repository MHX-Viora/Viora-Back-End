using System.Reflection;
using System.Text.Json;
using Viora.Application.Developers;
using Xunit;

public sealed class DeveloperDocumentationContractTests
{
    private static JsonElement[] Endpoints() => new[] { "api-inventory.json", "partner-inventory.json" }.SelectMany(file => {
        using var document = JsonDocument.Parse(File.ReadAllText(ReferencePath(file)));
        return document.RootElement.EnumerateArray().Select(value => value.Clone()).ToArray();
    }).ToArray();
    private static string ReferencePath(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Viora.Application.Tests"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.GetFullPath(Path.Combine(root!.FullName, "../viora/features/developer/docs/reference", name));
    }

    [Fact]
    public void EveryDocumentedRequestHasExactlyTheRealSerializedFields()
    {
        using var schemas = JsonDocument.Parse(File.ReadAllText(ReferencePath("request-fields.json")));
        using var generated = JsonDocument.Parse(File.ReadAllText(ReferencePath("dto-schemas.json")));
        var assembly = typeof(MiniAppCreateRequest).Assembly;
        foreach (var endpoint in Endpoints())
        {
            var request = endpoint.GetProperty("request").GetString();
            if (request is null) continue;
            var type = assembly.GetType($"Viora.Application.Developers.{request}");
            Assert.NotNull(type);
            var actual = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).Order().ToArray();
            var documented = generated.RootElement.GetProperty(request).EnumerateObject().Select(field => field.Name).Order().ToArray();
            Assert.Equal(actual, documented);
            if (schemas.RootElement.TryGetProperty(request, out var semantic)) Assert.Equal(actual, semantic.EnumerateObject().Select(field => field.Name).Order().ToArray());
        }
    }

    [Fact]
    public void GeneratedResponseSchemasCannotDriftFromPublicDtoProperties()
    {
        using var generated = JsonDocument.Parse(File.ReadAllText(ReferencePath("dto-schemas.json")));
        foreach (var schema in generated.RootElement.EnumerateObject())
        {
            var type = typeof(MiniAppDto).Assembly.GetType($"Viora.Application.Developers.{schema.Name}");
            Assert.NotNull(type);
            var fields = type!.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).Order().ToArray();
            Assert.Equal(fields, schema.Value.EnumerateObject().Select(field => field.Name).Order().ToArray());
        }
    }

    [Fact]
    public void ResponseTypesResolveAndPublicReferenceCannotIncludePrivateHostOrAdminRoutes()
    {
        foreach (var endpoint in Endpoints())
        {
            var path = endpoint.GetProperty("path").GetString()!;
            Assert.DoesNotContain("/admin/", path);
            Assert.DoesNotContain("/internal/", path);
            Assert.DoesNotContain("mini-app-host", path);
            Assert.DoesNotContain("credential", path);
            var response = endpoint.GetProperty("response").GetString();
            if (response is null || response is "string[]" or "text/plain attachment" || response.StartsWith('{')) continue;
            Assert.NotNull(typeof(MiniAppDto).Assembly.GetType($"Viora.Application.Developers.{response.Replace("[]", "")}"));
        }
        Assert.Throws<DeveloperAuthException>(() => MiniAppInput.ValidateCreate(new("a", "other", Guid.NewGuid())));
        Assert.Throws<DeveloperAuthException>(() => new MiniAppPublicCatalogQuery(1, 101).Validate());
    }
}
