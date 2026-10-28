using System.Text.Json;
using System.Text.Json.Serialization;
using Viora.Application.Developers;

namespace viora_BE.Production;

internal static class MiniAppProductionRequest
{
    public static async Task<T> ReadAsync<T>(HttpContext http, int maximum, CancellationToken ct)
    {
        if (!http.Request.HasJsonContentType()) throw new DeveloperAuthException(415, "JSON_REQUIRED", "Yêu cầu cần nội dung JSON.");
        if (http.Request.ContentLength > maximum) throw TooLarge();
        var bytes = new byte[maximum + 1]; var count = 0;
        while (count < bytes.Length)
        {
            var read = await http.Request.Body.ReadAsync(bytes.AsMemory(count), ct);
            if (read == 0) break; count += read;
        }
        if (count > maximum) throw TooLarge();
        try
        {
            using var json = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 4 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw Invalid();
            Unique(json.RootElement);
            return json.RootElement.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { PropertyNameCaseInsensitive = false, MaxDepth = 4, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject()) { if (!keys.Add(property.Name)) throw Invalid(); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    private static DeveloperAuthException Invalid() => new(400, "INVALID_RUNTIME_REQUEST", "Yêu cầu Mini App không hợp lệ.");
    private static DeveloperAuthException TooLarge() => new(413, "RUNTIME_REQUEST_TOO_LARGE", "Yêu cầu Mini App vượt giới hạn.");
}
