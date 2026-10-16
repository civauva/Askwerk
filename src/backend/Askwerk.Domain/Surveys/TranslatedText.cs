// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Askwerk.Domain.Surveys;

/// <summary>Human-readable text as a translation map, e.g. { "de": "…", "en": "…" } (data-model.md, rule 1).</summary>
/// <param name="texts">Text per language code.</param>
[JsonConverter(typeof(TranslatedTextJsonConverter))]
public sealed class TranslatedText(IDictionary<string, string> texts) : ReadOnlyDictionary<string, string>(texts)
{
    /// <summary>Whether the text exists and is not blank in the given language.</summary>
    /// <param name="language">Language code.</param>
    /// <returns>True when a non-blank text exists.</returns>
    public bool Has(string language) => TryGetValue(language, out var text) && !string.IsNullOrWhiteSpace(text);
}

/// <summary>Reads and writes <see cref="TranslatedText"/> as a plain JSON object.</summary>
public sealed class TranslatedTextJsonConverter : JsonConverter<TranslatedText>
{
    /// <inheritdoc />
    public override TranslatedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options) ?? []);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TranslatedText value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        JsonSerializer.Serialize(writer, new Dictionary<string, string>(value), options);
    }
}
