using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace T650Bridge.UI;

public static class IconGenerator
{
    public static void EnsureIcoFileExists(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) return;

            using var bmp = CreateTouchpadBitmap(32, Color.FromArgb(0, 220, 130), active: true);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            byte[] pngBytes = ms.ToArray();

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            // ICONDIR
            bw.Write((ushort)0); // Reserved
            bw.Write((ushort)1); // Resource type: 1 = Icon
            bw.Write((ushort)1); // Image count: 1

            // ICONDIRENTRY
            bw.Write((byte)32); // Width
            bw.Write((byte)32); // Height
            bw.Write((byte)0);  // Colors (0 = >= 8bpp)
            bw.Write((byte)0);  // Reserved
            bw.Write((ushort)1); // Color planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)pngBytes.Length); // Image size in bytes
            bw.Write((uint)22); // Offset to image data (6 + 16 = 22)

            // Image Data (PNG)
            bw.Write(pngBytes);
        }
        catch
        {
            // Non-critical if file cannot be written to disk
        }
    }

    public static Bitmap CreateTouchpadBitmap(int size, Color accentColor, bool active)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float scale = size / 32f;

        // Touchpad body (dark slate metallic style)
        using var bodyBrush = new SolidBrush(Color.FromArgb(245, 34, 38, 44));
        using var borderPen = new Pen(accentColor, 1.8f * scale);
        var rect = new Rectangle((int)(3 * scale), (int)(4 * scale), (int)(26 * scale), (int)(23 * scale));
        FillRoundedRectangle(g, bodyBrush, rect, (int)(4 * scale));
        DrawRoundedRectangle(g, borderPen, rect, (int)(4 * scale));

        // Mechanical click baseline at bottom
        using var dividerPen = new Pen(Color.FromArgb(160, 80, 90, 105), 1f * scale);
        g.DrawLine(dividerPen, 6 * scale, 21 * scale, 26 * scale, 21 * scale);
        g.DrawLine(dividerPen, 16 * scale, 21 * scale, 16 * scale, 25 * scale);

        // T650 Top LED indicator
        using var ledBrush = new SolidBrush(accentColor);
        g.FillEllipse(ledBrush, 14 * scale, 6 * scale, 4 * scale, 3 * scale);

        if (active)
        {
            // Multi-touch finger markers
            using var fingerBrush = new SolidBrush(Color.White);
            g.FillEllipse(fingerBrush, 8 * scale, 12 * scale, 3 * scale, 3 * scale);
            g.FillEllipse(fingerBrush, 14 * scale, 11 * scale, 3 * scale, 3 * scale);
            g.FillEllipse(fingerBrush, 20 * scale, 12 * scale, 3 * scale, 3 * scale);
        }
        else
        {
            // Pause double bars in center
            using var pauseBrush = new SolidBrush(Color.FromArgb(200, accentColor));
            g.FillRectangle(pauseBrush, 11 * scale, 11 * scale, 3 * scale, 7 * scale);
            g.FillRectangle(pauseBrush, 17 * scale, 11 * scale, 3 * scale, 7 * scale);
        }

        return bmp;
    }

    private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle bounds, int cornerRadius)
    {
        using var path = CreateRoundedRectanglePath(bounds, cornerRadius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle bounds, int cornerRadius)
    {
        using var path = CreateRoundedRectanglePath(bounds, cornerRadius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
