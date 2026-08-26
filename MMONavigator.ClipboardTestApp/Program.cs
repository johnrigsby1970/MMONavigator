using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MMONavigatorClipboardTestApp;

internal class Program
{
    // Win32 Clipboard Imports for Native Interop
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [STAThread]
    static void Main(string[] args)
    {
        Console.Title = "MMO Navigator Clipboard Telemetry Simulator";
        Console.WriteLine("================================================");
        Console.WriteLine(" MMO Navigator Clipboard Macro Simulation Test  ");
        Console.WriteLine("================================================");
        Console.WriteLine("[1] Simulating '/loc' macro execution.");
        Console.WriteLine("[2] Press 'SPACE' to trigger a location update.");
        Console.WriteLine("[3] Press 'A' to toggle auto-walk telemetry loop.");
        Console.WriteLine("[4] Press 'ESC' to exit.\n");

        float x = 100.0f;
        float y = 100.0f;
        float z = 15.0f;
        float heading = 90.0f; // East
        bool autoWalk = false;
        int lockGraceDelayMs = 75; // Grace period for game/macro clipboard releases

        while (true)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.Escape) break;

                if (key == ConsoleKey.Spacebar)
                {
                    // Single step / manual /loc trigger
                    heading = (heading + 5.0f) % 360.0f;
                    x += (float)Math.Cos(heading * Math.PI / 180.0) * 2.0f;
                    y += (float)Math.Sin(heading * Math.PI / 180.0) * 2.0f;

                    SendClipboardLocation(x, z, y, heading, lockGraceDelayMs);
                }
                else if (key == ConsoleKey.A)
                {
                    autoWalk = !autoWalk;
                    Console.WriteLine($"\n[>] Auto-walk loop: {(autoWalk ? "ON" : "OFF")}");
                }
            }

            if (autoWalk)
            {
                heading = (heading + 2.0f) % 360.0f;
                x += (float)Math.Cos(heading * Math.PI / 180.0) * 0.5f;
                y += (float)Math.Sin(heading * Math.PI / 180.0) * 0.5f;

                SendClipboardLocation(x, z, y, heading, lockGraceDelayMs);
                Thread.Sleep(200); // 5 Hz update rate to simulate realistic clipboard macro usage
            }
            else
            {
                Thread.Sleep(33);
            }
        }

        Console.WriteLine("\nTest App stopped.");
    }

    private static void SendClipboardLocation(float x, float z, float y, float heading, int graceDelayMs)
    {
        // Format string matching Pantheon style: "X Z Y Heading"
        string locationText = $"{x:F1} {z:F1} {y:F1} {heading:F1}";

        if (SetTextToClipboard(locationText))
        {
            Console.Write($"\r[Clipboard] Wrote: \"{locationText}\" (Grace Delay: {graceDelayMs}ms)    ");
        }
        else
        {
            Console.Write($"\r[Clipboard] Access collision/busy. Retrying next tick...              ");
        }

        // Healthy delay allowing OS and background listeners to process clipboard updates cleanly
        Thread.Sleep(graceDelayMs);
    }

    /// <summary>
    /// Safely writes text to the Win32 clipboard using native P/Invoke with error handling.
    /// </summary>
    private static bool SetTextToClipboard(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;

        try
        {
            EmptyClipboard();

            int bytesCount = (text.Length + 1) * sizeof(char);
            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytesCount);
            if (hMem == IntPtr.Zero) return false;

            IntPtr pMem = GlobalLock(hMem);
            if (pMem == IntPtr.Zero) return false;

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pMem, text.Length);
                Marshal.WriteInt16(pMem, text.Length * sizeof(char), 0); // Null terminator
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            return SetClipboardData(CF_UNICODETEXT, hMem) != IntPtr.Zero;
        }
        finally
        {
            CloseClipboard();
        }
    }
}