using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using MMONavigator.Models;

namespace MMONavigator.Helpers;

public static class DrawMapHelpers {
    //Take an object that is text, a box around that text, a border around that box, all of their transparency settings,
    //with rotation and scaling and write it to an image at a location. The key being to ensure the text is centered
    //and rotated correctly based on the resolution of the image considering the text box control is not in the
    //same reference frame.
    public static void BurnTextToBitmap(WriteableBitmap bitmap, MapTextStampEventArgs args) {
        if (!ValidateInputs(bitmap, args)) return;

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        try {
            bitmap.Lock();

            try {
                using (Bitmap gdiBitmap = new Bitmap(
                           width,
                           height,
                           bitmap.BackBufferStride,
                           System.Drawing.Imaging.PixelFormat.Format32bppPArgb,
                           bitmap.BackBuffer)) {
                    using (Graphics g = Graphics.FromImage(gdiBitmap)) {
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        // 1. Move to the bounding box position
                        g.TranslateTransform((float)args.X, (float)args.Y);

                        // 2. Handle center-pivot rotation to exactly match WPF's RenderTransformOrigin="0.5,0.5"
                        if (args.RotationAngle != 0) {
                            float centerX = (float)args.Width / 2f;
                            float centerY = (float)args.Height / 2f;

                            g.TranslateTransform(centerX, centerY);
                            g.RotateTransform((float)args.RotationAngle);
                            g.TranslateTransform(-centerX, -centerY);
                        }

                        // Bounding rectangle in local space
                        RectangleF boxBounds = new RectangleF(0, 0, (float)args.Width, (float)args.Height);

                        // 3. Draw Background and Border
                        bool hasRoundedCorners = args.CornerRadius.TopLeft > 0 || args.CornerRadius.TopRight > 0 ||
                                                 args.CornerRadius.BottomRight > 0 || args.CornerRadius.BottomLeft > 0;

                        bool shouldDrawBackground = args.BackgroundOpacity > 0 &&
                                                    args.BackgroundColor != System.Windows.Media.Colors.Transparent;
                        bool shouldDrawBorder = args.BoxBorderThickness > 0 && args.BoxBorderOpacity > 0 &&
                                                args.BoxBorderColor != System.Windows.Media.Colors.Transparent;

                        if (shouldDrawBackground || shouldDrawBorder) {
                            GraphicsPath? bgPath = null;
                            GraphicsPath? borderPath = null;

                            if (hasRoundedCorners) {
                                bgPath = GetRoundedRectPath(boxBounds, args.CornerRadius);
                            }

                            try {
                                // --- A. Draw Background ---
                                if (shouldDrawBackground) {
                                    int bgAlpha = (int)(Math.Clamp(args.BackgroundOpacity, 0, 1) * 255);
                                    System.Drawing.Color gdiBgColor = System.Drawing.Color.FromArgb(
                                        bgAlpha, args.BackgroundColor.R, args.BackgroundColor.G, args.BackgroundColor.B);

                                    using (System.Drawing.Brush bgBrush = new SolidBrush(gdiBgColor)) {
                                        if (hasRoundedCorners && bgPath != null) {
                                            g.FillPath(bgBrush, bgPath);
                                        }
                                        else {
                                            g.FillRectangle(bgBrush, boxBounds);
                                        }
                                    }
                                }

                                // --- B. Draw Border ---
                                if (shouldDrawBorder) {
                                    int borderAlpha = (int)(Math.Clamp(args.BoxBorderOpacity, 0, 1) * 255);
                                    System.Drawing.Color gdiBorderColor = System.Drawing.Color.FromArgb(
                                        borderAlpha, args.BoxBorderColor.R, args.BoxBorderColor.G, args.BoxBorderColor.B);

                                    float thickness = (float)args.BoxBorderThickness;
                                    float halfThickness = thickness / 2f;

                                    RectangleF borderBounds = new RectangleF(
                                        boxBounds.X + halfThickness,
                                        boxBounds.Y + halfThickness,
                                        Math.Max(1f, boxBounds.Width - thickness),
                                        Math.Max(1f, boxBounds.Height - thickness)
                                    );

                                    using (System.Drawing.Pen borderPen = new System.Drawing.Pen(gdiBorderColor, thickness)) {
                                        // Using Center alignment prevents GDI+ from applying hidden transforms to the Graphics state
                                        borderPen.Alignment = PenAlignment.Center;

                                        if (hasRoundedCorners) {
                                            // Generate a specific inset path for the border stroke
                                            borderPath = GetRoundedRectPath(borderBounds, args.CornerRadius);
                                            g.DrawPath(borderPen, borderPath);
                                        }
                                        else {
                                            g.DrawRectangle(borderPen, borderBounds.X, borderBounds.Y, borderBounds.Width, borderBounds.Height);
                                        }
                                    }
                                }
                            }
                            finally {
                                bgPath?.Dispose();
                                borderPath?.Dispose();
                            }
                        }

                        // 4. Setup Font Style & Formatting
                        System.Drawing.FontStyle fontStyle = System.Drawing.FontStyle.Regular;
                        if (args.IsBold) fontStyle |= System.Drawing.FontStyle.Bold;
                        if (args.IsItalic) fontStyle |= System.Drawing.FontStyle.Italic;
                        if (args.IsUnderline) fontStyle |= System.Drawing.FontStyle.Underline;

                        // Calculate total inset including the border thickness if it's visible
                        float borderThickness = shouldDrawBorder ? (float)args.BoxBorderThickness : 0f;
                        float padding = (float)args.TextPadding;

                        // The text box must start further inward to clear the border
                        float textX = padding + borderThickness;
                        float textY = padding + borderThickness;
                        float textWidth = (float)(args.Width - (textX * 2));
                        float textHeight = (float)(args.Height - (textY * 2));

                        // GDI+ GenericTypographic alignment adjustment 
                        // GenericTypographic can shave off a few pixels of font descent/leading whitespace, causing an upward shift.
                        // We add a tiny offset to match WPF's looser text bounding box.
                        float gdiTypographicVerticalOffset = (float)args.FontSize * 0.05f;

                        RectangleF textLayoutBounds = new RectangleF(
                            textX,
                            textY + gdiTypographicVerticalOffset,
                            textWidth > 0 ? textWidth : 1f,
                            textHeight > 0 ? textHeight : 1f
                        );

                        // Use GenericTypographic layout formatting to match WPF rendering spacing perfectly
                        using (StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic)) {
                            // Crucial: StringFormatFlags.NoWrap or LineLimit behaves differently based on alignment.
                            // If you are using Center or Right alignment, GenericTypographic needs trailing spaces measured.
                            stringFormat.FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.NoClip;
                            stringFormat.Alignment = ConvertAlignment(args.TextAlignment);

                            // If text is vertically centered in WPF, make sure GDI+ knows it too:
                            // stringFormat.LineAlignment = StringAlignment.Center; // Uncomment if your text is vertically centered

                            int textAlpha = (int)(Math.Clamp(args.TextOpacity, 0, 1) * 255);
                            System.Drawing.Color gdiTextColor = System.Drawing.Color.FromArgb(
                                textAlpha, args.TextColor.R, args.TextColor.G, args.TextColor.B);

                            using (System.Drawing.Font font = CreateFontSafely(args.FontFamilyName, (float)args.FontSize, fontStyle))
                            using (System.Drawing.Brush textBrush = new SolidBrush(gdiTextColor)) {
                                g.DrawString(args.Text ?? string.Empty, font, textBrush, textLayoutBounds, stringFormat);
                            }
                        }

                        g.ResetTransform();
                    }
                }

                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally {
                bitmap.Unlock();
            }

            Log.Debug("BurnTextToBitmap executed successfully for text '{Text}' at ({X}, {Y}).", args.Text, args.X, args.Y);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing BurnTextToBitmap.");
        }
    }

    public static void BurnCircleToBitmap(WriteableBitmap bitmap, MapTextStampEventArgs args) {
        if (!ValidateInputs(bitmap, args)) return;

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        try {
            bitmap.Lock();

            try {
                using (Bitmap gdiBitmap = new Bitmap(
                           width,
                           height,
                           bitmap.BackBufferStride,
                           System.Drawing.Imaging.PixelFormat.Format32bppPArgb,
                           bitmap.BackBuffer)) {
                    using (Graphics g = Graphics.FromImage(gdiBitmap)) {
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        // 1. Move to the bounding box position
                        g.TranslateTransform((float)args.X, (float)args.Y);

                        // 2. Handle center-pivot rotation to exactly match WPF's RenderTransformOrigin="0.5,0.5"
                        if (args.RotationAngle != 0) {
                            float centerX = (float)args.Width / 2f;
                            float centerY = (float)args.Height / 2f;

                            g.TranslateTransform(centerX, centerY);
                            g.RotateTransform((float)args.RotationAngle);
                            g.TranslateTransform(-centerX, -centerY);
                        }

                        // Bounding rectangle in local space
                        RectangleF boxBounds = new RectangleF(0, 0, (float)args.Width, (float)args.Height);

                        // Determine visibility flags
                        bool shouldDrawBackground = args.BackgroundOpacity > 0 &&
                                                    args.BackgroundColor != System.Windows.Media.Colors.Transparent;
                        bool shouldDrawBorder = args.BoxBorderThickness > 0 && args.BoxBorderOpacity > 0 &&
                                                args.BoxBorderColor != System.Windows.Media.Colors.Transparent;

                        // 3. Draw Background and Border
                        if (shouldDrawBackground) {
                            int bgAlpha = (int)(Math.Clamp(args.BackgroundOpacity, 0, 1) * 255);
                            System.Drawing.Color gdiBgColor = System.Drawing.Color.FromArgb(
                                bgAlpha, args.BackgroundColor.R, args.BackgroundColor.G, args.BackgroundColor.B);

                            using (System.Drawing.Brush bgBrush = new SolidBrush(gdiBgColor)) {
                                g.FillEllipse(bgBrush, boxBounds);
                            }
                        }

                        if (shouldDrawBorder) {
                            int borderAlpha = (int)(Math.Clamp(args.BoxBorderOpacity, 0, 1) * 255);
                            System.Drawing.Color gdiBorderColor = System.Drawing.Color.FromArgb(
                                borderAlpha, args.BoxBorderColor.R, args.BoxBorderColor.G, args.BoxBorderColor.B);

                            float thickness = (float)args.BoxBorderThickness;
                            float halfThickness = thickness / 2f;

                            RectangleF borderBounds = new RectangleF(
                                boxBounds.X + halfThickness,
                                boxBounds.Y + halfThickness,
                                Math.Max(1f, boxBounds.Width - thickness),
                                Math.Max(1f, boxBounds.Height - thickness)
                            );

                            using (System.Drawing.Pen borderPen = new System.Drawing.Pen(gdiBorderColor, thickness)) {
                                borderPen.Alignment = PenAlignment.Center;
                                g.DrawEllipse(borderPen, borderBounds);
                            }
                        }

                        // 4. Setup Font Style & Formatting
                        System.Drawing.FontStyle fontStyle = System.Drawing.FontStyle.Regular;
                        if (args.IsBold) fontStyle |= System.Drawing.FontStyle.Bold;
                        if (args.IsItalic) fontStyle |= System.Drawing.FontStyle.Italic;
                        if (args.IsUnderline) fontStyle |= System.Drawing.FontStyle.Underline;

                        // FIX: Instead of shrinking the text box dynamically (which can cause mathematical drifting), 
                        // we make the text layout bounds perfectly match the outer container bounds.
                        // The GDI+ StringAlignment engines will handle the dead-center alignment flawlessly.
                        RectangleF textLayoutBounds = new RectangleF(0, 0, (float)args.Width, (float)args.Height);

                        // Use GenericTypographic layout formatting to match WPF rendering spacing perfectly
                        using (StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic)) {
                            // Crucial for Centering: MeasureTrailingSpaces forces GDI+ to include the full width of spaces 
                            // when calculating the horizontal center point, preventing off-center drifting.
                            stringFormat.FormatFlags = StringFormatFlags.LineLimit |
                                                       StringFormatFlags.NoClip |
                                                       StringFormatFlags.MeasureTrailingSpaces;

                            // Explicitly enforce absolute center alignment both ways
                            stringFormat.Alignment = StringAlignment.Center;
                            stringFormat.LineAlignment = StringAlignment.Center;

                            int textAlpha = (int)(Math.Clamp(args.TextOpacity, 0, 1) * 255);
                            System.Drawing.Color gdiTextColor = System.Drawing.Color.FromArgb(
                                textAlpha, args.TextColor.R, args.TextColor.G, args.TextColor.B);

                            using (System.Drawing.Font font = CreateFontSafely(args.FontFamilyName, (float)args.FontSize, fontStyle))
                            using (System.Drawing.Brush textBrush = new SolidBrush(gdiTextColor)) {
                                g.DrawString(args.Text ?? string.Empty, font, textBrush, textLayoutBounds, stringFormat);
                            }
                        }

                        g.ResetTransform();
                    }
                }

                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally {
                bitmap.Unlock();
            }

            Log.Debug("BurnCircleToBitmap executed successfully at ({X}, {Y}).", args.X, args.Y);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing BurnCircleToBitmap.");
        }
    }

    public static void BurnCircleMarkerToBitmap(WriteableBitmap bitmap, MapTextStampEventArgs args) {
        if (!ValidateInputs(bitmap, args)) return;

        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;

        try {
            bitmap.Lock();

            try {
                using (Bitmap gdiBitmap = new Bitmap(
                           width,
                           height,
                           bitmap.BackBufferStride,
                           System.Drawing.Imaging.PixelFormat.Format32bppPArgb,
                           bitmap.BackBuffer)) {
                    using (Graphics g = Graphics.FromImage(gdiBitmap)) {
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.SmoothingMode = SmoothingMode.AntiAlias;

                        g.TranslateTransform((float)args.X, (float)args.Y);

                        if (args.RotationAngle != 0) {
                            float centerX = (float)args.Width / 2f;
                            float centerY = (float)args.Height / 2f;

                            g.TranslateTransform(centerX, centerY);
                            g.RotateTransform((float)args.RotationAngle);
                            g.TranslateTransform(-centerX, -centerY);
                        }

                        RectangleF boxBounds = new RectangleF(0, 0, (float)args.Width, (float)args.Height);

                        bool shouldDrawBackground = args.BackgroundOpacity > 0 &&
                                                    args.BackgroundColor != System.Windows.Media.Colors.Transparent;
                        bool shouldDrawBorder = args.BoxBorderThickness > 0 && args.BoxBorderOpacity > 0 &&
                                                args.BoxBorderColor != System.Windows.Media.Colors.Transparent;

                        if (shouldDrawBackground) {
                            int bgAlpha = (int)(Math.Clamp(args.BackgroundOpacity, 0, 1) * 255);
                            System.Drawing.Color gdiBgColor = System.Drawing.Color.FromArgb(
                                bgAlpha, args.BackgroundColor.R, args.BackgroundColor.G, args.BackgroundColor.B);

                            using (System.Drawing.Brush bgBrush = new SolidBrush(gdiBgColor)) {
                                g.FillEllipse(bgBrush, boxBounds);
                            }
                        }

                        if (shouldDrawBorder) {
                            int borderAlpha = (int)(Math.Clamp(args.BoxBorderOpacity, 0, 1) * 255);
                            System.Drawing.Color gdiBorderColor = System.Drawing.Color.FromArgb(
                                borderAlpha, args.BoxBorderColor.R, args.BoxBorderColor.G, args.BoxBorderColor.B);

                            float thickness = (float)args.BoxBorderThickness;
                            float halfThickness = thickness / 2f;

                            RectangleF borderBounds = new RectangleF(
                                boxBounds.X + halfThickness,
                                boxBounds.Y + halfThickness,
                                Math.Max(1f, boxBounds.Width - thickness),
                                Math.Max(1f, boxBounds.Height - thickness)
                            );

                            using (System.Drawing.Pen borderPen = new System.Drawing.Pen(gdiBorderColor, thickness)) {
                                borderPen.Alignment = PenAlignment.Center;
                                g.DrawEllipse(borderPen, borderBounds);
                            }
                        }

                        g.ResetTransform();
                    }
                }

                bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally {
                bitmap.Unlock();
            }

            Log.Debug("BurnCircleMarkerToBitmap executed successfully at ({X}, {Y}).", args.X, args.Y);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing BurnCircleMarkerToBitmap.");
        }
    }

    private static bool ValidateInputs(WriteableBitmap bitmap, MapTextStampEventArgs args) {
        if (bitmap == null) {
            Log.Warning("Burn operation skipped: WriteableBitmap is null.");
            return false;
        }

        if (args == null) {
            Log.Warning("Burn operation skipped: MapTextStampEventArgs is null.");
            return false;
        }

        if (double.IsNaN(args.X) || double.IsNaN(args.Y) || double.IsNaN(args.Width) || double.IsNaN(args.Height) ||
            double.IsInfinity(args.X) || double.IsInfinity(args.Y) || double.IsInfinity(args.Width) || double.IsInfinity(args.Height)) {
            Log.Warning("Burn operation skipped due to invalid geometry arguments: X={X}, Y={Y}, W={W}, H={H}", args.X, args.Y, args.Width, args.Height);
            return false;
        }

        return true;
    }

    private static System.Drawing.Font CreateFontSafely(string fontName, float fontSize, System.Drawing.FontStyle style) {
        float safeSize = Math.Clamp(fontSize, 6f, 288f);

        if (!string.IsNullOrWhiteSpace(fontName)) {
            try {
                return new System.Drawing.Font(fontName, safeSize, style, System.Drawing.GraphicsUnit.Pixel);
            }
            catch (Exception ex) {
                Log.Warning(ex, "Failed to load requested font family '{FontName}'. Falling back to GenericSansSerif.", fontName);
            }
        }

        return new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, safeSize, style, System.Drawing.GraphicsUnit.Pixel);
    }

    private static StringAlignment ConvertAlignment(System.Windows.TextAlignment wpfAlign) {
        return wpfAlign switch {
            System.Windows.TextAlignment.Center => StringAlignment.Center,
            System.Windows.TextAlignment.Right => StringAlignment.Far,
            _ => StringAlignment.Near
        };
    }

    private static GraphicsPath GetRoundedRectPath(RectangleF bounds, System.Windows.CornerRadius radius) {
        GraphicsPath path = new GraphicsPath();

        float maxDiameter = Math.Min(bounds.Width, bounds.Height);

        // The diameter of the corner arc determines the rounding curvature
        // Top-Left Corner
        float tlDiameter = Math.Min(maxDiameter, (float)radius.TopLeft * 2);
        if (tlDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Left, bounds.Top, tlDiameter, tlDiameter);
            path.AddArc(arc, 180, 90);
        }
        else {
            path.AddLine(bounds.Left, bounds.Top, bounds.Left, bounds.Top);
        }

        // Top-Right Corner
        float trDiameter = Math.Min(maxDiameter, (float)radius.TopRight * 2);
        if (trDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Right - trDiameter, bounds.Top, trDiameter, trDiameter);
            path.AddArc(arc, 270, 90);
        }
        else {
            path.AddLine(bounds.Right, bounds.Top, bounds.Right, bounds.Top);
        }

        // Bottom-Right Corner
        float brDiameter = Math.Min(maxDiameter, (float)radius.BottomRight * 2);
        if (brDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Right - brDiameter, bounds.Bottom - brDiameter, brDiameter, brDiameter);
            path.AddArc(arc, 0, 90);
        }
        else {
            path.AddLine(bounds.Right, bounds.Bottom, bounds.Right, bounds.Bottom);
        }

        // Bottom-Left Corner
        float blDiameter = Math.Min(maxDiameter, (float)radius.BottomLeft * 2);
        if (blDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Left, bounds.Bottom - blDiameter, blDiameter, blDiameter);
            path.AddArc(arc, 90, 90);
        }
        else {
            path.AddLine(bounds.Left, bounds.Bottom, bounds.Left, bounds.Bottom);
        }

        path.CloseFigure();
        return path;
    }
}