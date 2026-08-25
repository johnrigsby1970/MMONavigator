using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MMONavigator.TelemetryTestApp;

// Match the exact struct layout expected by SharedMemoryLocationProvider
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapTelemetryStruct
{
    public uint StructVersion;  // Version identifier (e.g., 1)
    public uint SequenceId;     // Incremented per update tick
    public float X;             // World X position
    public float Y;             // World Y position
    public float Z;             // World Z altitude
    public float Heading;       // Facing direction in degrees (0-360)
    public ulong ZoneId;        // Optional Zone/Map identifier
    public ulong TimestampMs;   // Milliseconds uptime timestamp
}

class Program
{
    private const string SharedMemoryName = "MMONavigator_Telemetry";
    private const int BufferSizeBytes = 1024;

    static void Main(string[] args)
    {
        Console.Title = "MMO Navigator Telemetry Simulator";
        Console.WriteLine("============================================");
        Console.WriteLine(" MMO Navigator Shared Memory Test Controller");
        Console.WriteLine("============================================");

        try
        {
            // 1. Create or attach to the shared memory block
            using var mmf = MemoryMappedFile.CreateOrOpen(SharedMemoryName, BufferSizeBytes, MemoryMappedFileAccess.ReadWrite);
            using var accessor = mmf.CreateViewAccessor(0, BufferSizeBytes, MemoryMappedFileAccess.ReadWrite);

            Console.WriteLine($"\n[+] Created shared memory buffer: '{SharedMemoryName}'");
            Console.WriteLine("[1] Simulating movement with Direct Binary Struct (Mode 0x02)");
            Console.WriteLine("[2] Press 'S' to toggle to Raw String Mode (Mode 0x01)");
            Console.WriteLine("[3] Press 'ESC' to exit\n");

            uint sequenceId = 0;
            float currentX = 100.0f;
            float currentY = 100.0f;
            float currentZ = 15.0f;
            float heading = 90.0f; // Facing East
            bool useDirectStructMode = true;

            // Simulation loop (pings updates at roughly 30 FPS / ~33ms delay)
            while (true)
            {
                // Key bindings check
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true).Key;
                    if (key == ConsoleKey.Escape) break;
                    if (key == ConsoleKey.S)
                    {
                        useDirectStructMode = !useDirectStructMode;
                        Console.WriteLine($"\n[>] Switched Mode -> {(useDirectStructMode ? "Direct Struct (0x02)" : "Raw String (0x01)")}");
                    }
                }

                // Simulate character walking in a small circle/path
                sequenceId++;
                heading = (heading + 2.0f) % 360.0f;
                currentX += (float)Math.Cos(heading * Math.PI / 180.0) * 0.5f;
                currentY += (float)Math.Sin(heading * Math.PI / 180.0) * 0.5f;

                if (useDirectStructMode)
                {
                    // MODE B: Direct Binary Struct Write (Byte 0 = 0x02)
                    accessor.Write(0, (byte)0x02);

                    var telemetry = new MapTelemetryStruct
                    {
                        StructVersion = 1,
                        SequenceId = sequenceId,
                        X = currentX,
                        Y = currentY,
                        Z = currentZ,
                        Heading = heading,
                        ZoneId = 101,
                        TimestampMs = (ulong)Environment.TickCount64
                    };

                    // Write struct starting at offset 1
                    accessor.Write(1, ref telemetry);

                    Console.Write($"\r[Struct Mode] Seq: {sequenceId} | Pos: X={currentX:F1}, Y={currentY:F1}, Z={currentZ:F1} | Facing: {heading:F1}°   ");
                }
                else
                {
                    // MODE A: Raw String Stream Write (Byte 0 = 0x01)
                    accessor.Write(0, (byte)0x01);
                    accessor.Write(1, (byte)(sequenceId % 255)); // Sequence byte at offset 1

                    // Format string matching game log output format (e.g., "X Z Y Heading")
                    string rawCoordinateText = $"{currentX:F1} {currentZ:F1} {currentY:F1} {heading:F1}";
                    byte[] textBytes = Encoding.UTF8.GetBytes(rawCoordinateText + "\0"); // Null-terminated string

                    // Write string starting at offset 2
                    accessor.WriteArray(2, textBytes, 0, textBytes.Length);

                    Console.Write($"\r[String Mode] Seq: {sequenceId} | Raw String: \"{rawCoordinateText}\"   ");
                }

                Thread.Sleep(33); // Sleep ~33 ms (30 Hz update rate)
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n\n[!] Error: {ex.Message}");
        }

        Console.WriteLine("\n\nTest Controller stopped. Memory released.");
    }
}