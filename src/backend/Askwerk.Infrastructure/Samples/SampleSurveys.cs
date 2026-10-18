// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using System.Text.Json;
using Askwerk.Domain.Surveys;
using Askwerk.Domain.Themes;
using SkiaSharp;

namespace Askwerk.Infrastructure.Samples;

/// <summary>One of Askwerk's sample surveys (W26): a complete survey with its own look and sample answers.</summary>
/// <param name="Id">Stable id, e.g. "cafe".</param>
/// <param name="Order">Place in the list.</param>
/// <param name="Names">Its name per language.</param>
/// <param name="Kind">What it shows, e.g. "Graphics-rich", per language.</param>
/// <param name="Features">What to look for in it, per language.</param>
/// <param name="Look">The id of its look in packages/schema/presets.</param>
/// <param name="Responses">How many sample answers it comes with.</param>
/// <param name="Texts">Sample text answers by question id, per language.</param>
/// <param name="Definition">The survey; pictures are written "{picture:name}".</param>
/// <param name="Title">Seeded demos (AWK-36): the survey's internal name in the list.</param>
public sealed record SampleSurvey(
    string Id,
    int Order,
    TranslatedText Names,
    TranslatedText Kind,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Features,
    string Look,
    int Responses,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>> Texts,
    SurveyDefinition Definition,
    TranslatedText? Title = null);

/// <summary>A picture a sample survey shows, drawn here.</summary>
/// <param name="Name">Its name in the definition.</param>
/// <param name="Png">The PNG bytes.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record SamplePicture(string Name, byte[] Png, int Width, int Height);

/// <summary>
/// Askwerk's sample surveys and looks (W26), embedded from Samples/*.json and packages/schema/presets/*.json. The
/// pictures are drawn, and the sample answers made up, at the moment a sample is created.
/// </summary>
public static class SampleSurveys
{
    private const string PicturePrefix = "{picture:";

    private static readonly Lazy<IReadOnlyList<SampleSurvey>> Loaded = new(() => Load("Askwerk.Infrastructure.Samples."));
    private static readonly Lazy<IReadOnlyList<SampleSurvey>> LoadedDemos = new(() => Load("Askwerk.Infrastructure.Demos."));
    private static readonly Lazy<IReadOnlyDictionary<string, ThemeTokens>> LoadedLooks = new(LoadLooks);

    /// <summary>All of them, in their order.</summary>
    public static IReadOnlyList<SampleSurvey> All => Loaded.Value;

    /// <summary>The demos every organisation is seeded with (AWK-36), in their order.</summary>
    public static IReadOnlyList<SampleSurvey> Demos => LoadedDemos.Value;

    /// <summary>Askwerk's looks by id (W26).</summary>
    public static IReadOnlyDictionary<string, ThemeTokens> Looks => LoadedLooks.Value;

    /// <summary>The sample with this id, or null.</summary>
    /// <param name="id">For example "cafe".</param>
    /// <returns>The sample or null.</returns>
    public static SampleSurvey? Find(string id) => All.FirstOrDefault(sample => sample.Id == id);

    /// <summary>The names of the pictures the sample shows.</summary>
    /// <param name="sample">The sample.</param>
    /// <returns>Picture names, each once.</returns>
    public static IReadOnlyList<string> PictureNames(SampleSurvey sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var json = JsonSerializer.Serialize(sample.Definition, SurveyDefinitionJson.Options);
        var names = new List<string>();

        for (var at = json.IndexOf(PicturePrefix, StringComparison.Ordinal); at >= 0; at = json.IndexOf(PicturePrefix, at + 1, StringComparison.Ordinal))
        {
            var end = json.IndexOf('}', at);
            var name = json[(at + PicturePrefix.Length)..end];

            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>The definition with the pictures' files filled in.</summary>
    /// <param name="sample">The sample.</param>
    /// <param name="assets">The file of each picture by name.</param>
    /// <returns>The definition.</returns>
    public static SurveyDefinition DefinitionWith(SampleSurvey sample, IReadOnlyDictionary<string, Guid> assets)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(assets);
        var json = JsonSerializer.Serialize(sample.Definition, SurveyDefinitionJson.Options);

        foreach (var (name, asset) in assets)
        {
            json = json.Replace($"{PicturePrefix}{name}}}", asset.ToString(), StringComparison.Ordinal);
        }

        return JsonSerializer.Deserialize<SurveyDefinition>(json, SurveyDefinitionJson.Options)!;
    }

    /// <summary>A picture drawn for the samples: cakes, drinks, a café garden, a team.</summary>
    /// <param name="name">Its name.</param>
    /// <returns>The picture.</returns>
    public static SamplePicture Picture(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == "peaks")
        {
            return new SamplePicture(name, Pdf.PdfScenes.Mountains(800, 600), 800, 600);
        }

        var wide = name is "cafe" or "garden" or "museum" or "hall";
        var (width, height) = wide ? (1600, 600) : (800, 600);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        Draw(canvas, name, width, height);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return new SamplePicture(name, data.ToArray(), width, height);
    }

    /// <summary>
    /// A sample's drawing covering a picture of any shape (AWK-74): scaled until it fills the picture, centred, the
    /// edges cut. Askwerk's illustrations are these drawings at 16:10.
    /// </summary>
    /// <param name="name">The drawing's name.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The drawn image.</returns>
    public static SKImage Covering(string name, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name == "peaks")
        {
            return SKImage.FromEncodedData(Pdf.PdfScenes.Mountains(width, height));
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;

        var (sceneWidth, sceneHeight) = name is "cafe" or "garden" or "museum" or "hall" ? (1600f, 600f) : (800f, 600f);
        var scale = Math.Max(width / sceneWidth, height / sceneHeight);
        canvas.Translate((width - (sceneWidth * scale)) / 2, (height - (sceneHeight * scale)) / 2);
        canvas.Scale(scale);
        Draw(canvas, name, (int)sceneWidth, (int)sceneHeight);

        return surface.Snapshot();
    }

    /// <summary>
    /// Made-up answers for a sample (W26): plausible, the same for the same seed, and only for the questions a
    /// respondent with these answers would see.
    /// </summary>
    /// <param name="sample">The sample.</param>
    /// <param name="definition">Its definition.</param>
    /// <param name="random">The source of chance.</param>
    /// <returns>The language and the answers by question id.</returns>
    public static (string Language, Dictionary<string, AnswerValue> Answers) Answers(SampleSurvey sample, SurveyDefinition definition, Random random)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        var language = random.NextDouble() < 0.6 ? definition.DefaultLanguage : definition.Languages.FirstOrDefault(code => code != definition.DefaultLanguage) ?? definition.DefaultLanguage;
        var answers = new Dictionary<string, AnswerValue>(StringComparer.Ordinal);

        foreach (var question in definition.Pages.SelectMany(page => page.Questions))
        {
            if (Answer(sample, question, language, random) is { } value)
            {
                answers[question.Id] = value;
            }
        }

        var shown = SurveyLogic.Route(definition, answers).SelectMany(page => page.Questions).Select(question => question.Id).ToHashSet(StringComparer.Ordinal);

        return (language, answers.Where(pair => shown.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static AnswerValue? Answer(SampleSurvey sample, Question question, string language, Random random)
    {
        var codes = question.Options?.Select(option => option.Code).ToList() ?? [];
        var pool = sample.Texts.TryGetValue(question.Id, out var texts) && texts.TryGetValue(language, out var list) ? list : null;
        // Leaning to the upper end, as satisfied respondents do.
        int High(int min, int max) => max - (int)Math.Floor(Math.Pow(random.NextDouble(), 1.8) * (max - min + 1));
        string Pick(IReadOnlyList<string> from) => from[random.Next(from.Count)];

        switch (question.Type)
        {
            case QuestionType.SingleChoice or QuestionType.Dropdown when codes.Count > 0:
                if (question.Other is { } other && random.NextDouble() < 0.06)
                {
                    return new AnswerValue(Option: other.Code, Other: language == "de" ? "Etwas anderes" : "Something else");
                }

                // Earlier options a little more often.
                return new AnswerValue(Option: codes[Math.Min(codes.Count - 1, (int)Math.Floor(Math.Pow(random.NextDouble(), 1.3) * codes.Count))]);
            case QuestionType.MultipleChoice when codes.Count > 0:
                return new AnswerValue(Options: [.. codes.OrderBy(_ => random.Next()).Take(1 + random.Next(Math.Min(3, codes.Count)))]);
            case QuestionType.YesNo:
                return new AnswerValue(Option: random.NextDouble() < 0.72 ? question.Yes?.Code ?? "1" : question.No?.Code ?? "0");
            case QuestionType.Rating or QuestionType.Nps:
                var min = (int)(question.Scale?.Min ?? (question.Type == QuestionType.Nps ? 0 : 1));
                var max = (int)(question.Scale?.Max ?? (question.Type == QuestionType.Nps ? 10 : 5));

                return question.DontKnow == true && random.NextDouble() < 0.05 ? new AnswerValue(DontKnow: true) : new AnswerValue(Number: High(min, max));
            case QuestionType.Slider:
                var low = question.Scale?.Min ?? 0;
                var high = question.Scale?.Max ?? 100;
                var step = question.Scale?.Step ?? 1;
                var steps = (int)((high - low) / step);
                var middle = (random.NextDouble() + random.NextDouble()) / 2;

                return new AnswerValue(Number: low + (step * (int)Math.Round(middle * steps)));
            case QuestionType.Matrix when codes.Count > 0 && question.Rows is { Count: > 0 } rows:
                return new AnswerValue(Rows: rows.ToDictionary(row => row.Code, _ => codes[Math.Max(0, High(0, codes.Count - 1))], StringComparer.Ordinal));
            case QuestionType.Ranking when codes.Count > 0:
                return new AnswerValue(Order: [.. codes.OrderBy(_ => random.Next())]);
            case QuestionType.ShortText or QuestionType.LongText when pool is { Count: > 0 } && (question.Required || random.NextDouble() < 0.45):
                return new AnswerValue(Text: Pick(pool));
            case QuestionType.Number:
                return new AnswerValue(Number: 1 + random.Next(question.Id == "years" ? 25 : 40));
            case QuestionType.Date when question.Mode is null or DateMode.Date:
                return new AnswerValue(Date: DateTime.UtcNow.Date.AddDays(-random.Next(question.Id == "start" ? 3000 : 60)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            default:
                return null;
        }
    }

    private static void Draw(SKCanvas canvas, string name, int width, int height)
    {
        using var paint = new SKPaint { IsAntialias = true };
        SKColor C(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        void Fill(uint rgb) { paint.Style = SKPaintStyle.Fill; paint.Color = C(rgb); }
        void Plate(float cx, float cy)
        {
            Fill(0xe9e2d6);
            canvas.DrawOval(cx, cy + 70, 260, 70, paint);
            Fill(0xffffff);
            canvas.DrawOval(cx, cy + 60, 230, 58, paint);
        }

        void Cup(float cx, float cy, uint drink)
        {
            Fill(0xffffff);
            canvas.DrawRoundRect(cx - 120, cy - 60, 240, 190, 30, 30, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 22;
            paint.Color = C(0xffffff);
            canvas.DrawOval(cx + 140, cy + 20, 40, 50, paint);
            Fill(drink);
            canvas.DrawOval(cx, cy - 52, 108, 22, paint);
        }

        switch (name)
        {
            case "cafe":
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), [C(0xf6d9a8), C(0xd98c4f), C(0x7a3f22)], SKShaderTileMode.Clamp))
                {
                    paint.Shader = shader;
                    canvas.DrawRect(0, 0, width, height, paint);
                    paint.Shader = null;
                }

                Cup(width / 2f, 330, 0x6b3a1f);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 14;
                paint.StrokeCap = SKStrokeCap.Round;
                paint.Color = C(0xfff6e8);

                for (var i = -1; i <= 1; i++)
                {
                    using var builder = new SKPathBuilder();
                    var x = (width / 2f) + (i * 60);
                    builder.MoveTo(x, 230);
                    builder.CubicTo(x - 40, 190, x + 40, 150, x, 100);
                    using var steam = builder.Detach();
                    canvas.DrawPath(steam, paint);
                }

                break;
            case "garden":
                using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height), [C(0xbfdcea), C(0xeef3e2)], SKShaderTileMode.Clamp))
                {
                    paint.Shader = sky;
                    canvas.DrawRect(0, 0, width, height, paint);
                    paint.Shader = null;
                }

                Fill(0x9cc08f);
                canvas.DrawOval(width * 0.25f, height + 80, width * 0.6f, 300, paint);
                Fill(0x6f9a7e);
                canvas.DrawOval(width * 0.8f, height + 60, width * 0.55f, 280, paint);

                foreach (var x in new[] { 180f, 520f, 1100f, 1420f })
                {
                    Fill(0x6b4a2f);
                    canvas.DrawRect(x - 12, 220, 24, 160, paint);
                    Fill(0x3f7a52);
                    canvas.DrawCircle(x, 200, 95, paint);
                }

                foreach (var x in new[] { 360f, 800f, 1240f })
                {
                    Fill(0xffffff);
                    canvas.DrawOval(x, 470, 90, 22, paint);
                    Fill(0xd2440f);
                    canvas.DrawRect(x - 8, 470, 16, 70, paint);
                }

                break;
            case "museum" or "hall":
                Fill(name == "museum" ? 0xe8dcc8u : 0xf3eee6u);
                canvas.DrawRect(0, 0, width, height, paint);
                Fill(name == "museum" ? 0xbcd6e4u : 0xd9cbb5u);
                canvas.DrawRect(0, 0, width, name == "museum" ? 200 : 120, paint);
                Fill(0xf8f4ec);
                canvas.DrawRect(320, 170, 960, 380, paint);

                if (name == "hall")
                {
                    // Inside: a chequered floor between columns, paintings on the back wall.
                    for (var x = 0; x < width; x += 80)
                    {
                        for (var y = 440; y < height; y += 40)
                        {
                            Fill(((x / 80) + (y / 40)) % 2 == 0 ? 0xd9cfc0u : 0xf3eee6u);
                            canvas.DrawRect(x, y, 80, 40, paint);
                        }
                    }

                    foreach (var x in new[] { 420f, 700f, 980f })
                    {
                        Fill(0xb08a3e);
                        canvas.DrawRect(x, 230, 200, 140, paint);
                        Fill(x == 700f ? 0x9fc3d6u : 0xe9b44cu);
                        canvas.DrawRect(x + 14, 244, 172, 112, paint);
                    }

                    break;
                }

                using (var roof = new SKPathBuilder())
                {
                    roof.MoveTo(280, 180);
                    roof.LineTo(800, 60);
                    roof.LineTo(1320, 180);
                    roof.Close();
                    using var path = roof.Detach();
                    Fill(0xd9cfc0);
                    canvas.DrawPath(path, paint);
                }

                for (var i = 0; i < 6; i++)
                {
                    Fill(0xffffff);
                    canvas.DrawRect(380 + (i * 170), 200, 50, 330, paint);
                    Fill(0xc9bda9);
                    canvas.DrawRect(370 + (i * 170), 190, 70, 18, paint);
                }

                Fill(0x2f6f4f);
                canvas.DrawRect(740, 400, 120, 150, paint);
                Fill(0x8a7f70);
                canvas.DrawRect(0, 550, width, 50, paint);
                break;
            case "painting":
                Fill(0xece4d6);
                canvas.DrawRect(0, 0, width, height, paint);
                Fill(0xb08a3e);
                canvas.DrawRect(200, 110, 400, 330, paint);
                Fill(0x9fc3d6);
                canvas.DrawRect(225, 135, 350, 280, paint);
                Fill(0xe9b44c);
                canvas.DrawCircle(470, 210, 40, paint);
                Fill(0x4c7a52);
                canvas.DrawOval(330, 400, 180, 70, paint);
                Fill(0x30584a);
                canvas.DrawOval(500, 410, 120, 60, paint);
                break;
            case "vase":
                Fill(0xe9eef0);
                canvas.DrawRect(0, 0, width, height, paint);
                Fill(0x3e6f9e);
                canvas.DrawOval(400, 330, 140, 160, paint);
                canvas.DrawRect(350, 140, 100, 80, paint);
                Fill(0xf2e6cf);
                canvas.DrawRect(262, 310, 276, 24, paint);
                Fill(0xc9c2b4);
                canvas.DrawRect(250, 490, 300, 40, paint);
                break;
            case "sculpture":
                Fill(0xf0ece6);
                canvas.DrawRect(0, 0, width, height, paint);
                Fill(0xd8d2c7);
                canvas.DrawRect(300, 420, 200, 120, paint);
                Fill(0x9c8f7d);
                canvas.DrawCircle(400, 170, 60, paint);
                canvas.DrawRoundRect(330, 220, 140, 210, 50, 50, paint);
                Fill(0xbdb5a8);
                canvas.DrawRect(300, 420, 200, 10, paint);
                break;
            case "fossil":
                Fill(0xe7dcc7);
                canvas.DrawRect(0, 0, width, height, paint);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 18;
                paint.Color = C(0x8a6d4b);

                for (var r = 40; r <= 200; r += 40)
                {
                    canvas.DrawArc(new SKRect(400 - r, 300 - r, 400 + r, 300 + r), 180, 270, false, paint);
                }

                break;
            case "beach" or "swim":
                Fill(0x9fd3e6);
                canvas.DrawRect(0, 0, width, height, paint);
                Fill(0xffe08a);
                canvas.DrawCircle(620, 130, 60, paint);
                Fill(0x2f8fb3);
                canvas.DrawRect(0, 300, width, 140, paint);
                Fill(0xf2d9a5);
                canvas.DrawOval(400, 560, 520, 160, paint);

                if (name == "beach")
                {
                    Fill(0x7a5a3a);
                    canvas.DrawRect(170, 260, 18, 230, paint);
                    Fill(0x3f8a5a);
                    canvas.DrawOval(180, 250, 110, 30, paint);
                }
                else
                {
                    Fill(0xffffff);
                    canvas.DrawOval(400, 360, 60, 14, paint);
                    Fill(0xe8b896);
                    canvas.DrawCircle(400, 342, 20, paint);
                }

                break;
            case "city" or "culture":
                Fill(name == "city" ? 0xf6c99au : 0xe7e2f3u);
                canvas.DrawRect(0, 0, width, height, paint);
                var heights = new[] { 260, 380, 220, 440, 300, 360, 240 };

                for (var i = 0; i < heights.Length; i++)
                {
                    Fill(i % 2 == 0 ? 0x4f5f7cu : 0x6a6f93u);
                    canvas.DrawRect(40 + (i * 105), 600 - heights[i], 95, heights[i], paint);
                    Fill(0xffe3b0);

                    for (var y = 620 - heights[i]; y < 560; y += 50)
                    {
                        canvas.DrawRect(60 + (i * 105), y, 18, 22, paint);
                        canvas.DrawRect(100 + (i * 105), y, 18, 22, paint);
                    }
                }

                break;
            case "forest" or "hike":
                Fill(0xdcebd5);
                canvas.DrawRect(0, 0, width, height, paint);

                for (var i = 0; i < 7; i++)
                {
                    using var tree = new SKPathBuilder();
                    var x = 60 + (i * 115);
                    tree.MoveTo(x, 520);
                    tree.LineTo(x + 50, 170 + ((i % 3) * 40));
                    tree.LineTo(x + 100, 520);
                    tree.Close();
                    using var path = tree.Detach();
                    Fill(i % 2 == 0 ? 0x2d6a3eu : 0x3f8a5au);
                    canvas.DrawPath(path, paint);
                }

                Fill(0x8a6d4b);
                canvas.DrawRect(0, 520, width, 80, paint);

                if (name == "hike")
                {
                    Fill(0xd2440f);
                    canvas.DrawRoundRect(370, 380, 60, 120, 20, 20, paint);
                    Fill(0xe8b896);
                    canvas.DrawCircle(400, 360, 24, paint);
                }

                break;
            case "food":
                Fill(0xf7ead9);
                canvas.DrawRect(0, 0, width, height, paint);
                Plate(400, 260);
                Fill(0xe9b44c);
                canvas.DrawCircle(360, 310, 50, paint);
                Fill(0x4c7a52);
                canvas.DrawCircle(450, 300, 40, paint);
                Fill(0xc2391a);
                canvas.DrawCircle(420, 350, 30, paint);
                break;
            case "team":
                Fill(0xf3efe8);
                canvas.DrawRect(0, 0, width, height, paint);
                var colours = new uint[] { 0xd2440f, 0x2f6f4f, 0x1f5f8b, 0x7c5cff, 0xb8860b };

                for (var i = 0; i < colours.Length; i++)
                {
                    var x = 140 + (i * 130);
                    var y = i % 2 == 0 ? 330 : 300;
                    Fill(colours[i]);
                    canvas.DrawRoundRect(x - 55, y, 110, 260, 50, 50, paint);
                    Fill(0xe8b896);
                    canvas.DrawCircle(x, y - 50, 48, paint);
                }

                break;
            default:
                var background = name switch { "sacher" => 0xf4e6d4u, "cheesecake" => 0xfdf3d7u, "croissant" => 0xfbecd2u, "coffee" => 0xe9dccbu, "tea" => 0xe4efe1u, "lemonade" => 0xfff6c4u, _ => 0xf7ead9u };
                Fill(background);
                canvas.DrawRect(0, 0, width, height, paint);
                var cx = width / 2f;
                var cy = (height / 2f) + 20;

                switch (name)
                {
                    case "strudel":
                        Plate(cx, cy);
                        Fill(0xd99a4e);
                        canvas.DrawRoundRect(cx - 190, cy - 70, 380, 130, 60, 60, paint);
                        Fill(0xf2d29b);

                        for (var i = 0; i < 5; i++)
                        {
                            canvas.DrawRoundRect(cx - 160 + (i * 70), cy - 60, 22, 110, 10, 10, paint);
                        }

                        break;
                    case "sacher" or "cheesecake":
                        Plate(cx, cy);
                        using (var builder = new SKPathBuilder())
                        {
                            builder.MoveTo(cx - 200, cy + 40);
                            builder.LineTo(cx + 190, cy + 40);
                            builder.LineTo(cx + 120, cy - 120);
                            builder.Close();
                            using var wedge = builder.Detach();
                            Fill(name == "sacher" ? 0x3b1f14u : 0xf6dc8au);
                            canvas.DrawPath(wedge, paint);
                        }

                        Fill(name == "sacher" ? 0xb5452au : 0x9a6a3au);
                        canvas.DrawRect(cx - 200, cy + 20, 390, 22, paint);
                        break;
                    case "croissant":
                        Plate(cx, cy);
                        Fill(0xd08a3a);

                        for (var i = -3; i <= 3; i++)
                        {
                            canvas.DrawCircle(cx + (i * 55), cy - 10 + (Math.Abs(i) * 18), 70 - (Math.Abs(i) * 10), paint);
                        }

                        break;
                    case "coffee":
                        Cup(cx, cy, 0x6b3a1f);
                        break;
                    case "tea":
                        Cup(cx, cy, 0xc9822b);
                        Fill(0x2f6f4f);
                        canvas.DrawRect(cx + 60, cy + 40, 40, 50, paint);
                        break;
                    default:
                        Fill(0xffffff);
                        canvas.DrawRoundRect(cx - 90, cy - 170, 180, 300, 20, 20, paint);
                        Fill(0xf4d03f);
                        canvas.DrawRoundRect(cx - 78, cy - 110, 156, 228, 14, 14, paint);
                        Fill(0xf9e79f);
                        canvas.DrawCircle(cx + 90, cy - 170, 60, paint);
                        break;
                }

                break;
        }
    }

    private static IReadOnlyList<SampleSurvey> Load(string prefix)
    {
        var assembly = typeof(SampleSurveys).Assembly;

        return [.. assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                var file = JsonSerializer.Deserialize<SampleFile>(stream, SurveyDefinitionJson.Options)!;

                return new SampleSurvey(name[prefix.Length..^".json".Length], file.Order, file.Names, file.Kind, file.Features, file.Look, file.Responses, file.Texts ?? new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(), file.Definition, file.Title);
            })
            .OrderBy(sample => sample.Order)];
    }

    private static Dictionary<string, ThemeTokens> LoadLooks()
    {
        const string prefix = "Askwerk.Infrastructure.Looks.";
        var assembly = typeof(SampleSurveys).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;

                return JsonSerializer.Deserialize<ThemePreset>(stream, SurveyDefinitionJson.Options)!;
            })
            .ToDictionary(preset => preset.Id, preset => preset.Tokens, StringComparer.Ordinal);
    }

    private sealed record SampleFile(
        int Order,
        TranslatedText Names,
        TranslatedText Kind,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Features,
        string Look,
        int Responses,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>? Texts,
        SurveyDefinition Definition,
        TranslatedText? Title = null);
}
