using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MMONavigator.Helpers;

public static class ImageExtensions {
    public static void BurnTextToBitmap(this WriteableBitmap bitmap, string text, double mapX, double mapY, float fontSize, string fontName, float angle)
    {
        // 1. Open a stream or pointer to your WriteableBitmap's pixel buffer
        // For safety and ease, you can copy the WriteableBitmap to a standard GDI+ Bitmap, or use its BackBuffer
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
    
        using (Bitmap gdiBitmap = new Bitmap(width, height, bitmap.BackBufferStride, 
                   System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bitmap.BackBuffer))
        {
            bitmap.Lock();

            using (Graphics g = Graphics.FromImage(gdiBitmap))
            {
                // Enable high-quality text rendering
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 2. Set up the rotation matrix around the insertion point
                g.TranslateTransform((float)mapX, (float)mapY);
                g.RotateTransform(angle);

                // 3. Draw the text using the user's selected font
                using (Font font = new Font(fontName, fontSize, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(Color.Black)) // Or user-selected color
                {
                    // Draw at (0,0) because TranslateTransform already moved the origin
                    g.DrawString(text, font, brush, 0, 0);
                }

                g.ResetTransform();
            }

            // 4. Tell WPF the pixels have changed so it updates the screen
            bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            bitmap.Unlock();
        }
    }
}