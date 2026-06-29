using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReportManagement.Helpers;

internal static class IconGenerator
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64];

    public static void Generate(string filePath, Color accent)
    {
        using var fs = new FileStream(filePath, FileMode.Create);
        using var writer = new BinaryWriter(fs);

        var entries = new List<byte[]>();
        foreach (int size in Sizes)
            entries.Add(CreatePng(size, accent));

        // ICO header
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)entries.Count);

        // Directory entries
        int offset = 6 + entries.Count * 16;
        foreach (var png in entries)
        {
            int w = Sizes[entries.IndexOf(png)];
            int h = w;
            writer.Write((byte)w);
            writer.Write((byte)h);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        // PNG data
        foreach (var png in entries)
            writer.Write(png);
    }

    private static byte[] CreatePng(int size, Color accent)
    {
        double pad = size * 0.12;
        double r = size * 0.22;
        var rect = new Rect(pad, pad, size - pad * 2, size - pad * 2);

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRoundedRectangle(new SolidColorBrush(accent), null, rect, r, r);

            var text = new FormattedText(
                "RM",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Yu Gothic UI, Meiryo UI"),
                size * 0.38,
                Brushes.White,
                96.0);

            ctx.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
