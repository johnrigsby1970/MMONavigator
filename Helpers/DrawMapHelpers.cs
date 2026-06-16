using System.Windows;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Media.Imaging;
using MMONavigator.Models;

namespace MMONavigator.Helpers;

public static class DrawMapHelpers {
    //Take an object that is text, a box around that text, a border around that box, all of their transparency settings,
    //with rotation and scaling and write it to an image at a location. The key being to ensure the text is centered
    //and rotated correctly based on the resolution of the image considering the text box control is not in the
    //same reference frame.
    public static void BurnTextToBitmap(WriteableBitmap bitmap, MapTextStampEventArgs args) {
    int width = bitmap.PixelWidth;
    int height = bitmap.PixelHeight;

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
                if (args.BackgroundOpacity > 0 && args.BackgroundColor != System.Windows.Media.Colors.Transparent) {
                    int bgAlpha = (int)(args.BackgroundOpacity * 255);
                    System.Drawing.Color gdiBgColor = System.Drawing.Color.FromArgb(
                        bgAlpha, args.BackgroundColor.R, args.BackgroundColor.G, args.BackgroundColor.B);

                    int borderAlpha = (int)(args.BackgroundOpacity * 255);
                    System.Drawing.Color gdiBorderColor = System.Drawing.Color.FromArgb(
                        borderAlpha, args.BoxBorderColor.R, args.BoxBorderColor.G, args.BoxBorderColor.B);

                    if (args.CornerRadius.TopLeft > 0 || args.CornerRadius.TopRight > 0 ||
                        args.CornerRadius.BottomRight > 0 || args.CornerRadius.BottomLeft > 0) {
                        using (GraphicsPath path = GetRoundedRectPath(boxBounds, args.CornerRadius)) {
                            using (System.Drawing.Brush bgBrush = new SolidBrush(gdiBgColor)) {
                                g.FillPath(bgBrush, path);
                            }

                            if (args.BoxBorderThickness > 0 && args.BoxBorderColor != System.Windows.Media.Colors.Transparent) {
                                using (System.Drawing.Pen borderPen = new System.Drawing.Pen(gdiBorderColor, (float)args.BoxBorderThickness)) {
                                    borderPen.Alignment = PenAlignment.Inset;
                                    g.DrawPath(borderPen, path);
                                }
                            }
                        }
                    }
                    else {
                        using (System.Drawing.Brush bgBrush = new SolidBrush(gdiBgColor)) {
                            g.FillRectangle(bgBrush, boxBounds);
                        }

                        if (args.BoxBorderThickness > 0 && args.BoxBorderColor != System.Windows.Media.Colors.Transparent) {
                            using (System.Drawing.Pen borderPen = new System.Drawing.Pen(gdiBorderColor, (float)args.BoxBorderThickness)) {
                                borderPen.Alignment = PenAlignment.Inset;
                                g.DrawRectangle(borderPen, 0, 0, boxBounds.Width, boxBounds.Height);
                            }
                        }
                    }
                }

                // 4. Setup Font Style & Formatting
                System.Drawing.FontStyle fontStyle = System.Drawing.FontStyle.Regular;
                if (args.IsBold) fontStyle |= System.Drawing.FontStyle.Bold;
                if (args.IsItalic) fontStyle |= System.Drawing.FontStyle.Italic;
                if (args.IsUnderline) fontStyle |= System.Drawing.FontStyle.Underline;

                float padding = (float)args.TextPadding;
                float textWidth = (float)(args.Width - (padding * 2));
                float textHeight = (float)(args.Height - (padding * 2));

                RectangleF textLayoutBounds = new RectangleF(
                    padding,
                    padding,
                    textWidth > 0 ? textWidth : 1f,
                    textHeight > 0 ? textHeight : 1f
                );

                // Use GenericTypographic layout formatting to match WPF rendering spacing perfectly
                using (StringFormat stringFormat = new StringFormat(StringFormat.GenericTypographic)) {
                    stringFormat.FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.NoClip;
                    stringFormat.Alignment = ConvertAlignment(args.TextAlignment);

                    int textAlpha = (int)(args.TextOpacity * 255);
                    System.Drawing.Color gdiTextColor = System.Drawing.Color.FromArgb(
                        textAlpha, args.TextColor.R, args.TextColor.G, args.TextColor.B);

                    using (System.Drawing.Font font = new System.Drawing.Font(args.FontFamilyName,
                               (float)args.FontSize, fontStyle, System.Drawing.GraphicsUnit.Pixel))
                    using (System.Drawing.Brush textBrush = new SolidBrush(gdiTextColor)) {
                        g.DrawString(args.Text, font, textBrush, textLayoutBounds, stringFormat);
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
}
    
// Map WPF Alignment Options directly to GDI+ Layout Engine
    private static StringAlignment ConvertAlignment(System.Windows.TextAlignment wpfAlign) {
        return wpfAlign switch {
            System.Windows.TextAlignment.Center => StringAlignment.Center,
            System.Windows.TextAlignment.Right => StringAlignment.Far,
            _ => StringAlignment
                .Near // Default handles Left and Justified (GDI+ doesn't cleanly auto-justify via StringFormat)
        };
    }

    private static GraphicsPath GetRoundedRectPath(RectangleF bounds, float radius) {
        GraphicsPath path = new GraphicsPath();

        // The diameter of the corner arc determines the rounding curvature
        float diameter = radius * 2;

        // Sanity check: Ensure the radius isn't larger than the box itself
        if (diameter > bounds.Width) diameter = bounds.Width;
        if (diameter > bounds.Height) diameter = bounds.Height;

        SizeF size = new SizeF(diameter, diameter);
        RectangleF arc = new RectangleF(bounds.Location, size);

        // Top-Left Corner
        path.AddArc(arc, 180, 90);

        // Top-Right Corner
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        // Bottom-Right Corner
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        // Bottom-Left Corner
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }

    private static GraphicsPath GetRoundedRectPath(RectangleF bounds, System.Windows.CornerRadius radius) {
        GraphicsPath path = new GraphicsPath();

        // Top-Left Corner
        float tlDiameter = (float)radius.TopLeft * 2;
        if (tlDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Left, bounds.Top, tlDiameter, tlDiameter);
            path.AddArc(arc, 180, 90);
        }
        else {
            path.AddLine(bounds.Left, bounds.Top, bounds.Left, bounds.Top);
        }

        // Top-Right Corner
        float trDiameter = (float)radius.TopRight * 2;
        if (trDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Right - trDiameter, bounds.Top, trDiameter, trDiameter);
            path.AddArc(arc, 270, 90);
        }
        else {
            path.AddLine(bounds.Right, bounds.Top, bounds.Right, bounds.Top);
        }

        // Bottom-Right Corner
        float brDiameter = (float)radius.BottomRight * 2;
        if (brDiameter > 0) {
            RectangleF arc = new RectangleF(bounds.Right - brDiameter, bounds.Bottom - brDiameter, brDiameter,
                brDiameter);
            path.AddArc(arc, 0, 90);
        }
        else {
            path.AddLine(bounds.Right, bounds.Bottom, bounds.Right, bounds.Bottom);
        }

        // Bottom-Left Corner
        float blDiameter = (float)radius.BottomLeft * 2;
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