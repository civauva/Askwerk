// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using Askwerk.Domain.Surveys;

namespace Askwerk.Infrastructure.Templates;

/// <summary>One of Askwerk's own templates.</summary>
/// <param name="Id">Stable id, "askwerk:{name}".</param>
/// <param name="Names">Its name per language.</param>
/// <param name="Definition">The survey definition.</param>
public sealed record SystemTemplate(string Id, TranslatedText Names, SurveyDefinition Definition);

/// <summary>Askwerk's own survey templates (AWK-17, W10 C), embedded from Templates/*.json, the same for every organisation.</summary>
public static class SystemTemplates
{
    private static readonly Lazy<IReadOnlyList<SystemTemplate>> Loaded = new(Load);

    /// <summary>All of them, ordered by id.</summary>
    public static IReadOnlyList<SystemTemplate> All => Loaded.Value;

    /// <summary>The template with this id, or null.</summary>
    /// <param name="id">For example "askwerk:nps-short".</param>
    /// <returns>The template or null.</returns>
    public static SystemTemplate? Find(string id) => All.FirstOrDefault(template => template.Id == id);

    private static IReadOnlyList<SystemTemplate> Load()
    {
        const string prefix = "Askwerk.Infrastructure.Templates.";
        var assembly = typeof(SystemTemplates).Assembly;

        return [.. assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                var file = JsonSerializer.Deserialize<TemplateFile>(stream, SurveyDefinitionJson.Options)!;

                return new SystemTemplate($"askwerk:{name[prefix.Length..^".json".Length]}", file.Names, file.Definition);
            })];
    }

    private sealed record TemplateFile(TranslatedText Names, SurveyDefinition Definition);
}
