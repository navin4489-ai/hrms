using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>
/// Serialises <see cref="TimeOnly"/> values as business times <c>HH:mm</c> exactly as the
/// contract specifies (docs/API_CONTRACT.md: "Times: HH:mm"), instead of the default HH:mm:ss.
/// Applied per DTO property so Program.cs stays untouched.
/// </summary>
public sealed class HhMmTimeOnlyConverter : JsonConverter<TimeOnly>
{
    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null) throw new JsonException("Expected a time value (HH:mm).");
        if (TimeOnly.TryParse(text, out var value)) return value;
        throw new JsonException($"'{text}' is not a valid time; expected HH:mm.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
}
