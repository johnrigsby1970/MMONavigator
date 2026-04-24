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
        WriteableBitmap blackBitmap = new WriteableBitmap(
            sourceImage.PixelWidth,
            sourceImage.PixelHeight,
            sourceImage.DpiX,
            sourceImage.DpiY,
            PixelFormats.Bgra32, 
            null);

        // 2. If you need Opaque Black:
        int stride = blackBitmap.BackBufferStride;
        int height = blackBitmap.PixelHeight;
        int bufferSize = stride * height;
        byte[] pixels = new byte[bufferSize];

        // Faster than a manual for-loop: use Parallel processing for large images
        Parallel.For(0, height, y =>
        {
            int rowStart = y * stride;
            for (int x = 3; x < stride; x += 4)
            {
                pixels[rowStart + x] = 255; // Set Alpha to Opaque
            }
        });

        // 3. Apply
        blackBitmap.WritePixels(new Int32Rect(0, 0, blackBitmap.PixelWidth, blackBitmap.PixelHeight), pixels, stride, 0);

        return blackBitmap;
    }
    
    public static WriteableBitmap CreateTransparentBitmap(BitmapSource sourceImage) 
    {
        // 1. Initialize - Buffer is already all zeros (Transparent Black)
        WriteableBitmap blackBitmap = new WriteableBitmap(
            sourceImage.PixelWidth,
            sourceImage.PixelHeight,
            sourceImage.DpiX,
            sourceImage.DpiY,
            PixelFormats.Bgra32, 
            null);

        // 2. If you need Opaque Black:
        int stride = blackBitmap.BackBufferStride;
        int height = blackBitmap.PixelHeight;
        int bufferSize = stride * height;
        byte[] pixels = new byte[bufferSize];

        // Faster than a manual for-loop: use Parallel processing for large images
        Parallel.For(0, height, y =>
        {
            int rowStart = y * stride;
            for (int x = 3; x < stride; x += 4)
            {
                pixels[rowStart + x] = 0; // Set Alpha to Opaque
            }
        });

        // 3. Apply
        blackBitmap.WritePixels(new Int32Rect(0, 0, blackBitmap.PixelWidth, blackBitmap.PixelHeight), pixels, stride, 0);

        return blackBitmap;
    }
    
    public static void SaveWriteableBitMap(string filename, BitmapSource image5)
    {
        if (filename != string.Empty)
        {
            using (FileStream stream5 = new FileStream(filename, FileMode.Create))
            {
                PngBitmapEncoder encoder5 = new PngBitmapEncoder();
                encoder5.Frames.Add(BitmapFrame.Create(image5));
                encoder5.Save(stream5);
            }
        }
    }
}