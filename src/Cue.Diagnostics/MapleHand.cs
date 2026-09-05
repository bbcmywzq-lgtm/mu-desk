using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

internal static partial class Program
{
    private static void BuildMapleHand(bool bold)
    {
        var assetRoot = Path.GetFullPath("assets/cursors/maple-link");
        var folder = bold ? Path.Combine(assetRoot, "bold-code-v2") : assetRoot;
        Directory.CreateDirectory(folder);
        var source = Decode(File.ReadAllBytes(Path.Combine(assetRoot, "generated-hand.png")));
        var sourceBounds = AlphaBounds(source);
        var sourcePixels = Pixels(source);
        if (sourcePixels.Where((value, index) => index % 4 == 3 && value == 0).Count() < source.PixelWidth * source.PixelHeight / 5)
            throw new Exception("Cursor master must have a genuinely transparent exterior, not a painted background.");
        foreach (var (xRatio, yRatio) in new[] { (.55, .65), (.55, .75), (.65, .7) })
        {
            var x = sourceBounds.X + (int)(sourceBounds.Width * xRatio);
            var y = sourceBounds.Y + (int)(sourceBounds.Height * yRatio);
            var offset = (y * source.PixelWidth + x) * 4;
            if (sourcePixels[offset + 3] < 250 || sourcePixels[offset] < 240)
                throw new Exception("Palm must remain opaque white without holes or gray shading.");
        }
        var crop = new CroppedBitmap(source, sourceBounds);
        var originalPath = bold ? Path.Combine(assetRoot, "Maple-Link-Minimal.cur") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CursorSkinManager", "skins", "curated-maple", "Link.cur");
        var original = File.ReadAllBytes(originalPath);
        var count = BitConverter.ToUInt16(original, 4);
        if (BitConverter.ToUInt16(original, 2) != 2 || count != 6) throw new Exception("Unexpected original cursor layout.");
        var frames = new List<(int Size, int X, int Y, byte[] Png)>();
        // Match the original cursor's occupied height, not the generator's arbitrary margins.
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            int size = original[entry];
            var offset = (int)BitConverter.ToUInt32(original, entry + 12);
            var length = (int)BitConverter.ToUInt32(original, entry + 8);
            var old = Decode(original.AsSpan(offset, length).ToArray());
            var oldBounds = AlphaBounds(old);
            var inkHeight = oldBounds.Height;
            var inkWidth = inkHeight * (double)crop.PixelWidth / crop.PixelHeight;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(crop, new Rect((size - inkWidth) / 2, (size - inkHeight) / 2, inkWidth, inkHeight));
            var rendered = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            BitmapSource raster = rendered;
            var bounds = AlphaBounds(raster);
            var pixels = Pixels(raster);
            // The highest opaque run is the pointing fingertip: preserve that hotspot
            // at every size rather than inheriting the old slanted finger's coordinates.
            var tipY = bounds.Y; var xs = new List<int>();
            for (var x = 0; x < size; x++) if (pixels[(tipY * size + x) * 4 + 3] >= 128) xs.Add(x);
            if (xs.Count == 0) throw new Exception("Missing fingertip alpha.");
            var tipX = (xs[0] + xs[^1]) / 2;
            if (bold)
            {
                raster = ThickenContour(old);
                tipX = BitConverter.ToUInt16(original, entry + 4);
                tipY = BitConverter.ToUInt16(original, entry + 6);
            }
            frames.Add((size, tipX, tipY, Encode(raster)));
            File.WriteAllBytes(Path.Combine(folder, $"hand-{size}.png"), Encode(raster));
        }
        var cursorPath = Path.Combine(folder, "Maple-Link-Minimal.cur");
        using (var stream = File.Create(cursorPath))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)frames.Count);
            var offset = 6 + 16 * frames.Count;
            foreach (var f in frames)
            {
                writer.Write((byte)f.Size); writer.Write((byte)f.Size); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)f.X); writer.Write((ushort)f.Y); writer.Write(f.Png.Length); writer.Write(offset);
                offset += f.Png.Length;
            }
            foreach (var f in frames) writer.Write(f.Png);
        }
        foreach (var f in frames)
        {
            var cursor = LoadCursorImage(0, cursorPath, 2, f.Size, f.Size, 0x10);
            if (cursor == 0) throw new Exception($"Windows rejected cursor size {f.Size}: {Marshal.GetLastWin32Error()}");
            try
            {
                if (!GetCursorIconInfo(cursor, out var info)) throw new Exception("Cannot verify cursor hotspot.");
                try
                {
                    if (info.HotspotX != f.X || info.HotspotY != f.Y) throw new Exception("Hotspot mismatch.");
                }
                finally { DeleteCursorBitmap(info.Color); DeleteCursorBitmap(info.Mask); }
            }
            finally { DestroyPackagedCursor(cursor); }
        }
        var preview = new DrawingVisual();
        using (var dc = preview.RenderOpen())
        {
            var ratio = Math.Min(140d / crop.PixelWidth, 140d / crop.PixelHeight);
            var w = crop.PixelWidth * ratio; var h = crop.PixelHeight * ratio;
            dc.DrawImage(crop, new Rect((221 - w) / 2, (221 - h) / 2, w, h));
        }
        var renderedPreview = new RenderTargetBitmap(221, 221, 96, 96, PixelFormats.Pbgra32);
        renderedPreview.Render(preview);
        BitmapSource previewBitmap = bold
            ? ThickenContour(Decode(File.ReadAllBytes(Path.Combine(assetRoot, "preview.png"))))
            : renderedPreview;
        File.WriteAllBytes(Path.Combine(folder, "preview.png"), Encode(previewBitmap));
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(250, 248, 245)), null, new Rect(0, 0, 620, 290));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(39, 37, 46)), null, new Rect(310, 0, 310, 290));
            for (var side = 0; side < 2; side++)
            {
                dc.DrawImage(previewBitmap, new Rect(side * 310 + 75, 10, 160, 160));
                for (var i = 0; i < 3; i++)
                {
                    var f = frames[i];
                    dc.DrawImage(Decode(f.Png), new Rect(side * 310 + 45 + i * 85, 210, f.Size, f.Size));
                }
            }
        }
        var sheetBitmap = new RenderTargetBitmap(620, 290, 96, 96, PixelFormats.Pbgra32);
        sheetBitmap.Render(sheet);
        File.WriteAllBytes(Path.Combine(folder, "size-check.png"), Encode(sheetBitmap));
        Console.WriteLine("PASS: 6 native CUR sizes (26/39/52/77/103/205), all fingertip hotspots verified. Original asset unchanged.");
        Console.WriteLine($"Generated bounds: {sourceBounds}; cursor: {cursorPath}");
    }

    // Grow the existing dark contour inward by a fractional pixel radius. Never
    // alter alpha: every silhouette edge, transparent pixel and hotspot stays exact.
    private static BitmapSource ThickenContour(BitmapSource source)
    {
        var before = Pixels(source);
        var after = (byte[])before.Clone();
        var width = source.PixelWidth; var height = source.PixelHeight;
        var radius = AlphaBounds(source).Height * .024;
        double Sample(int x, int y, int channel)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return 255;
            var offset = (y * width + x) * 4;
            return before[offset + 3] < 32 ? 255 : before[offset + channel];
        }
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var offset = (y * width + x) * 4;
            if (before[offset + 3] == 0) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                double darkest = before[offset + channel];
                for (var step = 0; step < 32; step++)
                {
                    var angle = step * Math.PI / 16;
                    var sx = x + radius * Math.Cos(angle); var sy = y + radius * Math.Sin(angle);
                    var left = (int)Math.Floor(sx); var top = (int)Math.Floor(sy);
                    var fx = sx - left; var fy = sy - top;
                    var value = Sample(left, top, channel) * (1 - fx) * (1 - fy)
                        + Sample(left + 1, top, channel) * fx * (1 - fy)
                        + Sample(left, top + 1, channel) * (1 - fx) * fy
                        + Sample(left + 1, top + 1, channel) * fx * fy;
                    darkest = Math.Min(darkest, value);
                }
                after[offset + channel] = (byte)Math.Round(darkest);
            }
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, after, width * 4);
        var roundtrip = Pixels(Decode(Encode(result)));
        var changes = 0;
        for (var i = 0; i < before.Length; i += 4)
        {
            if (before[i + 3] != roundtrip[i + 3]) throw new Exception("Contour edit changed alpha.");
            if (after[i] < before[i]) changes++;
        }
        if (changes == 0) throw new Exception("Contour edit did not increase weight.");
        Console.WriteLine($"Weight: {width}px, inward radius {radius:F3}px, {changes} darker pixels; alpha byte-identical.");
        return result;
    }

    private static BitmapSource Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
    }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        converted.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes;
    }
    private static Int32Rect AlphaBounds(BitmapSource bitmap)
    {
        var bytes = Pixels(bitmap); int left = bitmap.PixelWidth, top = bitmap.PixelHeight, right = 0, bottom = 0;
        for (var y = 0; y < bitmap.PixelHeight; y++) for (var x = 0; x < bitmap.PixelWidth; x++)
            if (bytes[(y * bitmap.PixelWidth + x) * 4 + 3] >= 128)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        if (right < left || bottom < top) throw new Exception("Empty image.");
        return new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }
    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorIconInfo
    { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadCursorImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetIconInfo")] private static extern bool GetCursorIconInfo(nint cursor, out CursorIconInfo info);
    [DllImport("user32.dll", EntryPoint = "DestroyCursor")] private static extern bool DestroyPackagedCursor(nint cursor);
    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")] private static extern bool DeleteCursorBitmap(nint bitmap);
}
