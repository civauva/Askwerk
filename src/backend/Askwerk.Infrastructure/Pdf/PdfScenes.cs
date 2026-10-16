// SPDX-License-Identifier: AGPL-3.0-only
using SkiaSharp;

namespace Askwerk.Infrastructure.Pdf;

/// <summary>
/// The look's drawn backgrounds as pictures the PDF can place (W28): the web draws them from SVG, the PDF prints the
/// same scene as a band at the top of page 1.
/// </summary>
public static class PdfScenes
{
    /// <summary>The mountain landscape at dawn, as a PNG band.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <returns>The PNG bytes.</returns>
    public static byte[] Mountains(int width = 1600, int height = 380)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        // The scene is drawn on a 1600 × 1000 grid and cut around the peaks.
        // Covers the picture whatever its shape: wide bands keep the full width, squarer pictures are cut at the sides.
        var scale = Math.Max(width / 1600f, height / 760f);
        canvas.Scale(scale);
        canvas.Translate(-((1600 * scale) - width) / 2 / scale, -240);
        using var paint = new SKPaint { IsAntialias = true };
        SKColor C(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 1000), [C(0x2d4a6b), C(0xd98b73), C(0xf6c99a)], [0f, 0.45f, 0.72f], SKShaderTileMode.Clamp))
        {
            paint.Shader = sky;
            canvas.DrawRect(0, 0, 1600, 1000, paint);
            paint.Shader = null;
        }

        paint.Color = C(0xffe3b0);
        canvas.DrawCircle(1180, 430, 70, paint);

        void Shape(uint colour, params float[] points)
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(points[0], points[1]);

            for (var i = 2; i < points.Length; i += 2)
            {
                builder.LineTo(points[i], points[i + 1]);
            }

            builder.Close();
            using var path = builder.Detach();
            paint.Color = C(colour);
            canvas.DrawPath(path, paint);
        }

        Shape(0x7a6f93, 0, 560, 180, 380, 300, 470, 470, 290, 640, 470, 760, 360, 900, 500, 1080, 330, 1260, 500, 1420, 380, 1600, 520, 1600, 1000, 0, 1000);
        Shape(0xf4eef6, 470, 290, 520, 340, 495, 345, 470, 320, 445, 350, 420, 340);
        Shape(0xf4eef6, 1080, 330, 1130, 380, 1100, 382, 1080, 362, 1055, 388, 1030, 380);
        Shape(0x4f5f7c, 0, 640, 220, 500, 380, 600, 560, 470, 760, 620, 960, 500, 1160, 640, 1360, 520, 1600, 640, 1600, 1000, 0, 1000);
        Shape(0x2f4a4c, 0, 720, 560, 690, 1200, 680, 1600, 700, 1600, 1000, 0, 1000);
        Shape(0x5d8597, 0, 770, 1600, 770, 1600, 1000, 0, 1000);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }
}
