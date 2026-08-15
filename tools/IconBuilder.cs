using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class IconBuilder
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: IconBuilder input.png output.ico");
            return 2;
        }

        using (Image source = Image.FromFile(args[0]))
        {
            var images = new MemoryStream[Sizes.Length];
            try
            {
                for (int i = 0; i < Sizes.Length; i++) images[i] = RenderPng(source, Sizes[i]);
                WriteIcon(args[1], images);
            }
            finally
            {
                foreach (MemoryStream stream in images) if (stream != null) stream.Dispose();
            }
        }
        return 0;
    }

    private static MemoryStream RenderPng(Image source, int size)
    {
        using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            bitmap.SetResolution(96, 96);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            return stream;
        }
    }

    private static void WriteIcon(string outputPath, MemoryStream[] images)
    {
        using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Length);

            int offset = 6 + (16 * images.Length);
            for (int i = 0; i < images.Length; i++)
            {
                int size = Sizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)images[i].Length);
                writer.Write((uint)offset);
                offset += (int)images[i].Length;
            }

            foreach (MemoryStream image in images) image.CopyTo(file);
        }
    }
}
