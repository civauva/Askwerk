// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text.Json;

namespace Askwerk.Application.Exports;

/// <summary>
/// Askwerk JSON: the codebook and every response with its values as codes, numbers as numbers. The same
/// vocabulary as the public API (agents/extensibility.md); written as it is read, so large exports stay small in memory.
/// </summary>
public sealed class JsonExporter : IExporter
{
    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Json;

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public string Extension => ".json";

    /// <inheritdoc />
    public async Task WriteAsync(ExportData data, ExportOptions options, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        await using var json = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var variables = data.Codebook.Variables;

        json.WriteStartObject();
        json.WriteStartObject("survey");
        json.WriteString("id", data.SurveyId);
        json.WriteString("title", data.Title);
        json.WriteEndObject();
        json.WriteString("exportedAt", ExportTable.Time(data.ExportedAt));
        json.WriteString("language", data.Codebook.Language);
        json.WriteStartArray("variables");

        foreach (var variable in variables)
        {
            json.WriteStartObject();
            json.WriteString("name", variable.Name);
            json.WriteString("label", variable.Label);
            json.WriteString("type", variable.Numeric ? "number" : "text");
            json.WriteString("question", variable.QuestionKey);
            json.WriteStartArray("values");

            foreach (var value in variable.Values)
            {
                json.WriteStartObject();
                json.WriteString("code", value.Code);
                json.WriteString("label", value.Label);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("responses");

        await foreach (var response in data.Responses.WithCancellation(cancellationToken))
        {
            json.WriteStartObject();
            json.WriteString("id", response.Id);
            json.WriteString("status", response.Status);
            json.WriteNumber("version", response.Version);
            json.WriteString("language", response.Language);
            json.WriteString("startedAt", ExportTable.Time(response.StartedAt));

            if (response.CompletedAt is { } completed)
            {
                json.WriteString("completedAt", ExportTable.Time(completed));
            }
            else
            {
                json.WriteNull("completedAt");
            }

            json.WriteStartObject("values");

            foreach (var variable in variables)
            {
                if (data.Codebook.Cell(variable, response.Answers.GetValueOrDefault(variable.QuestionKey), labels: false) is not { } cell)
                {
                    continue;
                }

                if (variable.Numeric && decimal.TryParse(cell, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    json.WriteNumber(variable.Name, number);
                }
                else
                {
                    json.WriteString(variable.Name, cell);
                }
            }

            json.WriteEndObject();
            json.WriteEndObject();

            if (json.BytesPending > 64 * 1024)
            {
                await json.FlushAsync(cancellationToken);
            }
        }

        json.WriteEndArray();
        json.WriteEndObject();
        await json.FlushAsync(cancellationToken);
    }
}
