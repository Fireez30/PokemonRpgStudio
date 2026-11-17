using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokemonRpgStudio.Core.Json;

/// <summary>
/// Reads either a JSON string or an array of strings into a list (the source data mixes both,
/// e.g. <c>"pokemon_types": "Fire"</c> vs <c>"pokemon_types": ["Fire", "Flying"]</c>).
/// Always writes an array.
/// </summary>
public sealed class StringOrListConverter : JsonConverter<List<string>>
{
    public override bool HandleNull => true;

    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return new List<string>();
            case JsonTokenType.String:
            {
                var s = reader.GetString() ?? "";
                return string.IsNullOrWhiteSpace(s) ? new List<string>() : new List<string> { s };
            }
            case JsonTokenType.StartArray:
            {
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                        list.Add(reader.GetString() ?? "");
                    else if (reader.TokenType != JsonTokenType.Null)
                        list.Add(JsonDocument.ParseValue(ref reader).RootElement.ToString());
                }
                return list;
            }
            default:
                return new List<string> { JsonDocument.ParseValue(ref reader).RootElement.ToString() };
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteStringValue(v);
        writer.WriteEndArray();
    }
}

/// <summary>
/// Move lists of a sheet. Only the move names are written; the details (type, roll, effect…) are
/// read from moves.json at run time so the data can be updated. Older files (and the website's
/// files) store full move objects: their <c>name</c> is kept.
/// </summary>
public sealed class MoveNameListConverter : JsonConverter<List<string>>
{
    // A null list in the file must become an empty list, not null.
    public override bool HandleNull => true;

    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        if (reader.TokenType == JsonTokenType.Null) return list;
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            reader.Skip();
            return list;
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    if (reader.GetString() is { Length: > 0 } s) list.Add(s.Trim());
                    break;
                case JsonTokenType.StartObject:
                    using (var doc = JsonDocument.ParseValue(ref reader))
                        if (doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                            && name.GetString() is { Length: > 0 } n)
                            list.Add(n.Trim());
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteStringValue(v);
        writer.WriteEndArray();
    }
}

/// <summary>Reads a number or a numeric string ("87.5", "") into a nullable double.</summary>
public sealed class FlexibleNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            }
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteStringValue("");
        else writer.WriteNumberValue(value.Value);
    }
}

/// <summary>Reads a number or numeric string into an int (defaults to 0).</summary>
public sealed class FlexibleIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var i) ? i : (int)Math.Round(reader.GetDouble());
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : 0;
            }
            default:
                reader.Skip();
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>Reads a number or numeric string ("12.5") into a double (defaults to 0).</summary>
public sealed class FlexibleDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetDouble();
            case JsonTokenType.String:
                return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            default:
                reader.Skip();
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

/// <summary>Reads any scalar (string, number, bool) as a string; null stays null.</summary>
public sealed class AnyToStringConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            JsonTokenType.Number => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

/// <summary>
/// Level values in sheet files are sometimes strings ("45") and sometimes numbers.
/// Reads both, writes a number when possible.
/// </summary>
public sealed class LevelStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Number => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            JsonTokenType.Null => "",
            _ => SkipAndEmpty(ref reader),
        };
    }

    private static string SkipAndEmpty(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) writer.WriteNumberValue(i);
        else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) writer.WriteNumberValue(d);
        else writer.WriteStringValue(value);
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Compact variant used for large data files.</summary>
    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };
}
