using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viora.Application.Developers;

// Offline developer tooling: explicit public inventory only. No configuration,
// database, API, credentials or internal source paths enter generated output.
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Viora.Application"))) root = root.Parent;
if (root is null) throw new InvalidOperationException("Run inside the ANKT backend checkout.");
var reference = Path.GetFullPath(Path.Combine(root.FullName, "../viora/features/developer/docs/reference"));
var endpoints = new[] { "api-inventory.json", "partner-inventory.json" }.SelectMany(file => {
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(reference, file)));
    return document.RootElement.EnumerateArray().Select(value => value.Clone()).ToArray();
}).ToArray();
var assembly = typeof(MiniAppDto).Assembly;
var schemas = new SortedDictionary<string, object>();
var pending = new Queue<Type>();
foreach (var endpoint in endpoints)
foreach (var key in new[] { "request", "response" })
{
    var name = endpoint.GetProperty(key).GetString()?.Replace("[]", "");
    if (name is null || name is "string" or "text/plain attachment" || name.StartsWith('{')) continue;
    var type = assembly.GetType($"Viora.Application.Developers.{name}") ?? throw new InvalidOperationException($"Unknown documented DTO: {name}");
    pending.Enqueue(type);
}
string Describe(Type type)
{
    if (Nullable.GetUnderlyingType(type) is { } inner) return Describe(inner) + " | null";
    if (type == typeof(string)) return "string";
    if (type == typeof(Guid)) return "UUID";
    if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "ISO 8601 datetime";
    if (type == typeof(bool)) return "boolean";
    if (type.IsPrimitive || type == typeof(decimal)) return "number";
    if (type.IsEnum) return string.Join(" | ", Enum.GetNames(type));
    if (type.IsArray) return Describe(type.GetElementType()!) + "[]";
    if (type.IsGenericType && type.GetGenericArguments().Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        return Describe(type.GetGenericArguments()[0]) + "[]";
    if (type.Assembly == assembly && type.IsClass) pending.Enqueue(type);
    return type.Name;
}
var nullability = new NullabilityInfoContext();
while (pending.TryDequeue(out var type))
{
    if (schemas.ContainsKey(type.Name)) continue;
    var fields = new SortedDictionary<string, object>(); schemas[type.Name] = fields;
    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
        var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
        var shape = Describe(property.PropertyType);
        if (nullability.Create(property).ReadState == NullabilityState.Nullable && !shape.Contains("null")) shape += " | null";
        var length = property.GetCustomAttribute<StringLengthAttribute>();
        fields[name] = new { type = shape, jsonRequired = property.IsDefined(typeof(JsonRequiredAttribute)),
            validationRequired = property.IsDefined(typeof(RequiredAttribute)), minLength = length?.MinimumLength, maxLength = length?.MaximumLength };
    }
}
File.WriteAllText(Path.Combine(reference, "dto-schemas.json"), JsonSerializer.Serialize(schemas, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Generated {schemas.Count} public DTO schemas from the allowlisted endpoint contracts.");
