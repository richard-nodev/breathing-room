using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Renders the cat into a multi-size .ico so the exe, tray and windows all use the same face.
static class MakeIcon
{
    [STAThread]
    static int Main(string[] args)
    {
        string outPath = args.Length > 0 ? args[0] : "icon.ico";
        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var images = new List<byte[]>();
        const double box = 118;

        foreach (int size in sizes)
        {
            var host = new Grid { Width = box, Height = box };
            host.Children.Add(new BreathingRoom.CatView { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            host.Measure(new Size(box, box));
            host.Arrange(new Rect(0, 0, box, box));
            host.UpdateLayout();

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / box, size / box));
                dc.DrawRectangle(new VisualBrush(host), null, new Rect(0, 0, box, box));
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var ms = new MemoryStream())
            {
                encoder.Save(ms);
                images.Add(ms.ToArray());
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        using (var w = new BinaryWriter(File.Create(outPath)))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
        Console.WriteLine("Wrote " + outPath);
        return 0;
    }
}
