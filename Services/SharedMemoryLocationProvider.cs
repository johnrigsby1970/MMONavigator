using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using MMONavigator.Helpers;
using MMONavigator.Interfaces;
using MMONavigator.Models;

namespace MMONavigator.Services;

/// <summary>
/// Native C/C++ Binary Struct Layout (1-byte packed alignment).
/// Third-party game developers write this memory block at 30-60 Hz.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MapTelemetryStruct
{
    public uint StructVersion;  // Version identifier (e.g., 1)
    public uint SequenceId;     // Incremented per frame update to check for new data
    public float X;             // World X position
    public float Y;             // World Y position
    public float Z;             // World Z altitude
    public float Heading;       // Facing direction in degrees (0-360)
    public ulong ZoneId;        // Optional Zone/Map identifier
    public ulong TimestampMs;   // Milliseconds uptime timestamp
}

public class SharedMemoryLocationProvider : ILocationProvider
{
    public event EventHandler<string>? LocationUpdated;

    private const string SharedMemoryName = "MMONavigator_Telemetry";
    private const int BufferSizeBytes = 1024;

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private DispatcherTimer? _pollTimer;
    private AppSettings? _settings;

    private uint _lastSequenceId;
    private string _lastFormattedString = string.Empty;

    public void Start(AppSettings settings, IntPtr windowHandle)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Log.Information("Starting SharedMemoryLocationProvider. Target Memory: {MemoryName}", SharedMemoryName);

        Stop();

        try
        {
            // Open or create named shared memory segment
            _mmf = MemoryMappedFile.CreateOrOpen(SharedMemoryName, BufferSizeBytes, MemoryMappedFileAccess.ReadWrite);
            _accessor = _mmf.CreateViewAccessor(0, BufferSizeBytes, MemoryMappedFileAccess.ReadWrite);

            // Configure high-frequency polling timer on UI thread (~30 Hz polling / 33ms interval)
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _pollTimer.Tick += OnPollTimerTick;
            _pollTimer.Start();

            Log.Information("SharedMemoryLocationProvider initialized successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize SharedMemoryLocationProvider.");
        }
    }

    public void Stop()
    {
        Log.Information("Stopping SharedMemoryLocationProvider.");

        if (_pollTimer != null)
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= OnPollTimerTick;
            _pollTimer = null;
        }

        if (_accessor != null)
        {
            _accessor.Dispose();
            _accessor = null;
        }

        if (_mmf != null)
        {
            _mmf.Dispose();
            _mmf = null;
        }

        _lastSequenceId = 0;
        _lastFormattedString = string.Empty;
    }

    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        if (_accessor == null || _settings?.SelectedProfile == null) return;

        try
        {
            // Check magic byte header or read initial discriminator
            // Byte 0: Telemetry Format Type (0x01 = Raw String, 0x02 = Direct Struct)
            byte formatType = _accessor.ReadByte(0);

            if (formatType == 0x01)
            {
                // Mode A: Parse null-terminated ASCII/UTF-8 string written by game
                ReadRawStringStream();
            }
            else if (formatType == 0x02)
            {
                // Mode B: Parse binary unmanaged C/C++ struct
                ReadDirectStructStream();
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error reading telemetry from shared memory buffer.");
        }
    }

    private void ReadRawStringStream()
    {
        if (_accessor == null || _settings?.SelectedProfile == null) return;

        // Byte 1: Sequence ID (1-255)
        byte sequenceId = _accessor.ReadByte(1);
        if (sequenceId == _lastSequenceId) return; // No new frame update written
        _lastSequenceId = sequenceId;

        // Read ASCII string starting at offset 2 until null-terminator
        byte[] buffer = new byte[256];
        _accessor.ReadArray(2, buffer, 0, buffer.Length);

        int nullIndex = Array.IndexOf(buffer, (byte)0);
        int length = nullIndex >= 0 ? nullIndex : buffer.Length;

        if (length == 0) return;

        string rawText = Encoding.UTF8.GetString(buffer, 0, length);

        if (Scrubber.TryParse(rawText, _settings.SelectedProfile.CoordinateOrder, out _))
        {
            string scrubbed = Scrubber.ScrubEntry(rawText) ?? string.Empty;
            if (scrubbed != _lastFormattedString)
            {
                _lastFormattedString = scrubbed;
                LocationUpdated?.Invoke(this, scrubbed);
            }
        }
    }

    private void ReadDirectStructStream()
    {
        if (_accessor == null || _settings?.SelectedProfile == null) return;

        // Read struct payload directly starting at offset 1
        _accessor.Read(1, out MapTelemetryStruct telemetry);

        if (telemetry.SequenceId == _lastSequenceId) return; // Skip if no new frame update
        _lastSequenceId = telemetry.SequenceId;

        // Format direct properties into standard string format based on SelectedProfile.CoordinateOrder
        string formattedCoordinates = FormatStructToCoordinateString(telemetry, _settings.SelectedProfile.CoordinateOrder);

        if (!string.IsNullOrEmpty(formattedCoordinates) && formattedCoordinates != _lastFormattedString)
        {
            _lastFormattedString = formattedCoordinates;
            Log.Debug("Shared Memory Telemetry parsed: {Coordinates}", formattedCoordinates);
            LocationUpdated?.Invoke(this, formattedCoordinates);
        }
    }

    private static string FormatStructToCoordinateString(MapTelemetryStruct data, string coordinateOrder)
    {
        // Formats X, Y, Z, and Heading into standard space-delimited coordinates matching expected profile order
        return coordinateOrder switch
        {
            "y x" => $"{data.Y:F1} {data.X:F1}",
            "y x z" => $"{data.Y:F1} {data.X:F1} {data.Z:F1}",
            "x y" => $"{data.X:F1} {data.Y:F1}",
            "x z y d" => $"{data.X:F1} {data.Z:F1} {data.Y:F1} {data.Heading:F1}",
            _ => $"{data.X:F1} {data.Z:F1} {data.Y:F1} {data.Heading:F1}" // Default x z y d fallback
        };
    }

    public void Dispose()
    {
        Stop();
    }
}