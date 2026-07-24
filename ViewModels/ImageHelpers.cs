using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MMONavigator.ViewModels;

public static class ImageHelpers {
    //We need fog of war image the same size as source image
    public static WriteableBitmap CreateBlackBitmap(BitmapSource sourceImage) 
    {
        // 1. Initialize - Buffer is already all zeros (Transparent Black)
        // Switched to Pbgra32 to match your standardized application pipeline
        WriteableBitmap blackBitmap = new WriteableBitmap(
            sourceImage.PixelWidth,
            sourceImage.PixelHeight,
            sourceImage.DpiX,
            sourceImage.DpiY,
            PixelFormats.Pbgra32, 
            null);

        // 2. Clear to Opaque Black
        int stride = blackBitmap.BackBufferStride;
        int height = blackBitmap.PixelHeight;
        int bufferSize = stride * height;
        byte[] pixels = new byte[bufferSize];

        // Parallel fill remains correct: in both Bgra32 and Pbgra32, 
        // the 4th byte of every pixel group is the Alpha channel.
        Parallel.For(0, height, y =>
        {
            int rowStart = y * stride;
            for (int x = 3; x < stride; x += 4)
            {
                pixels[rowStart + x] = 255; // Set Alpha to Opaque (Fully solid)
            }
        });

        // 3. Apply the byte array to the back buffer
        blackBitmap.WritePixels(new Int32Rect(0, 0, blackBitmap.PixelWidth, blackBitmap.PixelHeight), pixels, stride, 0);

        return blackBitmap;
    }
    
    // public static WriteableBitmap CreateTransparentBitmap(BitmapSource sourceImage) 
    // {
    //     // 1. Initialize - Buffer is already all zeros (Transparent Black)
    //     WriteableBitmap blackBitmap = new WriteableBitmap(
    //         sourceImage.PixelWidth,
    //         sourceImage.PixelHeight,
    //         sourceImage.DpiX,
    //         sourceImage.DpiY,
    //         PixelFormats.Pbgra32, 
    //         null);
    //
    //     // 2. If you need Opaque Black:
    //     int stride = blackBitmap.BackBufferStride;
    //     int height = blackBitmap.PixelHeight;
    //     int bufferSize = stride * height;
    //     byte[] pixels = new byte[bufferSize];
    //
    //     // Faster than a manual for-loop: use Parallel processing for large images
    //     Parallel.For(0, height, y =>
    //     {
    //         int rowStart = y * stride;
    //         for (int x = 3; x < stride; x += 4)
    //         {
    //             pixels[rowStart + x] = 0; // Set Alpha to Opaque
    //         }
    //     });
    //
    //     // 3. Apply
    //     blackBitmap.WritePixels(new Int32Rect(0, 0, blackBitmap.PixelWidth, blackBitmap.PixelHeight), pixels, stride, 0);
    //
    //     return blackBitmap;
    // }
    
    public static WriteableBitmap CreateTransparentBitmap(BitmapSource sourceImage) 
    {
        // Instantiating with Pbgra32 naturally allocates a zeroed-out memory buffer.
        // In Pbgra32, 0,0,0,0 is perfectly valid Transparent Black.
        WriteableBitmap transparentBitmap = new WriteableBitmap(
            sourceImage.PixelWidth,
            sourceImage.PixelHeight,
            sourceImage.DpiX,
            sourceImage.DpiY,
            PixelFormats.Pbgra32, 
            null);

        // No array allocations, no Parallel loops, and no WritePixels required.
        // The framework handles the back-buffer zero-initialization natively.
        return transparentBitmap;
    }
    
    public static WriteableBitmap CreateBlackBitmapSize(int width, int height, double dpiX, double dpiY)
    {
        WriteableBitmap bitmap = new WriteableBitmap(width, height, dpiX, dpiY, PixelFormats.Bgra32, null);
        int stride = bitmap.BackBufferStride;
        byte[] pixels = new byte[stride * height];
        Parallel.For(0, height, y => {
            int rowStart = y * stride;
            for (int x = 3; x < stride; x += 4)
                pixels[rowStart + x] = 255;
        });
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return bitmap;
    }

    public static void SaveWriteableBitMap(string filename, BitmapSource image5)
    {
        if (!string.IsNullOrEmpty(filename))
        {
            try {
                var directory = Path.GetDirectoryName(filename);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                var tempFile = filename + ".tmp";
                using (FileStream stream5 = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    PngBitmapEncoder encoder5 = new PngBitmapEncoder();
                    encoder5.Frames.Add(BitmapFrame.Create(image5));
                    encoder5.Save(stream5);
                }

                if (File.Exists(filename)) {
                    File.Replace(tempFile, filename, filename + ".old");
                    try { File.Delete(filename + ".old"); } catch { /* Ignore cleanup failure */ }
                } else {
                    File.Move(tempFile, filename);
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"Error saving image to {filename}: {ex.Message}");
                // Re-throw or handle as appropriate for the application's UX
                throw;
            }
        }
    }
}