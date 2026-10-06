using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;
internal static class SpriteAssets
{
    // Forge sprites are saved intact. Chroma-key composition happens only in memory.
    internal static BitmapSource Load(string name, bool trim = false)
    {
        using var stream = BundledAssets.Open(name);
        var input = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(input, PixelFormats.Bgra32, null, 0);
        int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
        var bytes = new byte[stride * height]; source.CopyPixels(bytes, stride, 0);
        int minX = width, minY = height, maxX = 0, maxY = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * stride + x * 4;
            double b = bytes[i], g = bytes[i + 1], r = bytes[i + 2];
            double excess = Math.Min(r, b) - g;
            if (excess > 28 && Math.Max(r,b) > 80)
            {
                double alpha = 1 - Math.Clamp((excess - 28) / 85, 0, 1);
                bytes[i + 3] = (byte)(255 * alpha);
                // Despill the magenta fringe at antialiased object edges.
                bytes[i] = (byte)Math.Min(b, g + 25); bytes[i + 2] = (byte)Math.Min(r, g + 35);
            }
            if (bytes[i + 3] > 10) { minX = Math.Min(minX,x); minY = Math.Min(minY,y); maxX = Math.Max(maxX,x); maxY = Math.Max(maxY,y); }
        }
        BitmapSource result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
        if (trim && maxX > minX && maxY > minY) result = new CroppedBitmap(result, new Int32Rect(minX,minY,maxX-minX+1,maxY-minY+1));
        result.Freeze(); return result;
    }
}
