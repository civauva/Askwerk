// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Askwerk.Domain.Surveys;

/// <summary>JSON settings for definitions: camelCase properties and kebab-case enum values, as in packages/schema.</summary>
public static class SurveyDefinitionJson
{
    /// <summary>The options every reader and writer of a definition uses.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
    };
}
