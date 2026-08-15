using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MMONavigator.Helpers;

public static class ImageExtensions {
    public static void BurnTextToBitmap(
        this WriteableBitmap bitmap, 
        string text, 
        double mapX, 
        double mapY, 
        float fontSize, 
        string fontName, 
        float angle) 
    {
        if (bitmap == null) {
            Log.Warning("BurnTextToBitmap called on a null WriteableBitmap instance.");
            return;
        }

        if (string.IsNullOrEmpty(text)) {
            Log.Debug("BurnTextToBitmap called with empty or null text; skipping render.");
            return;
        }

        // Guard against NaN / Infinity coordinates
        if (double.IsNaN(mapX) || double.IsNaN(mapY) || double.IsInfinity(mapX) || double.IsInfinity(mapY) ||
            float.IsNaN(fontSize) || float.IsInfinity(fontSize) || float.IsNaN(angle) || float.IsInfinity(angle)) {
            Log.Warning("BurnTextToBitmap received invalid geometry/font inputs: X={X}, Y={Y}, Size={Size}, Angle={Angle}", 
                mapX, mapY, fontSize, angle);
            return;
        }

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        if (width <= 0 || height <= 0) {
            Log.Warning("BurnTextToBitmap skipped due to invalid bitmap dimensions: {Width}x{Height}", width, height);
            return;
        }

        try {
            // Must lock the WriteableBitmap BEFORE accessing BackBuffer or BackBufferStride
            bitmap.Lock();

            try {
                using (Bitmap gdiBitmap = new Bitmap(
                           width, 
                           height, 
                           bitmap.BackBufferStride, 
                           System.Drawing.Imaging.PixelFormat.Format32bppPArgb, 
                           bitmap.BackBuffer)) 
                {
                    using (Graphics g = Graphics.FromImage(gdiBitmap)) {
                        // High-quality text rendering
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        // Set up rotation matrix around insertion point
                        g.TranslateTransform((float)mapX, (float)mapY);
                        g.RotateTransform(angle);

                        // Resolve requested font or fallback safely
                        using (Font font = CreateFontSafely(fontName, fontSize))
                        using (Brush brush = new SolidBrush(Color.Black)) {
                            // Draw at (0,0) because TranslateTransform already moved the origin
                            g.DrawString(text, font, brush, 0, 0);
                        }

                        g.ResetTransform();
                    }
                }

                // Notify WPF that pixel contents have changed
                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally {
                // Ensure unlock always occurs even if GDI rendering throws
                bitmap.Unlock();
            }

            Log.Debug("Successfully burned text '{Text}' to WriteableBitmap at ({X}, {Y}).", text, mapX, mapY);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error burning text '{Text}' to WriteableBitmap.", text);
        }
    }

    private static Font CreateFontSafely(string fontName, float fontSize) {
        float safeSize = Math.Clamp(fontSize, 6f, 288f);

        if (!string.IsNullOrWhiteSpace(fontName)) {
            try {
                return new Font(fontName, safeSize, GraphicsUnit.Pixel);
            }
            catch (Exception ex) {
                Log.Warning(ex, "Failed to load font family '{FontName}'; falling back to generic SansSerif.", fontName);
            }
        }

        return new Font(FontFamily.GenericSansSerif, safeSize, GraphicsUnit.Pixel);
    }
}