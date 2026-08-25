using System.Runtime.InteropServices;
using System.Windows.Interop;
using MMONavigator.Helpers;
using MMONavigator.Interfaces;
using MMONavigator.Models;

namespace MMONavigator.Services;

public class ClipboardLocationProvider : ILocationProvider
{
    public event EventHandler<string>? LocationUpdated;

    private IntPtr _windowHandle;
    private AppSettings? _settings;
    private HwndSource? _hwndSource;

    public void Start(AppSettings settings, IntPtr windowHandle)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Log.Information("Starting ClipboardLocationProvider. WindowHandle: {Handle}", windowHandle);

        Stop();
        _windowHandle = windowHandle;

        if (_windowHandle == IntPtr.Zero)
        {
            Log.Warning("ClipboardLocationProvider started with an empty WindowHandle.");
            return;
        }

        try
        {
            // Register native listener
            if (NativeMethods.AddClipboardFormatListener(_windowHandle))
            {
                // Hook into the HWND message loop directly
                _hwndSource = HwndSource.FromHwnd(_windowHandle);
                _hwndSource?.AddHook(HwndHandler);
            }
            else
            {
                Log.Warning("Failed to register AddClipboardFormatListener for handle {Handle}", _windowHandle);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error starting ClipboardLocationProvider.");
        }
    }

    public void Stop()
    {
        Log.Information("Stopping ClipboardLocationProvider.");

        try
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(HwndHandler);
                _hwndSource = null;
            }

            if (_windowHandle != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error unhooking clipboard listener during ClipboardLocationProvider.Stop.");
        }
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            // Process asynchronously on the WPF STA Thread with a minor delay 
            // so external applications finish writing to the clipboard
            _ = ProcessClipboardAsync();
        }

        return IntPtr.Zero;
    }

    private async Task ProcessClipboardAsync()
    {
        // 50-75ms grace period for macro/game tools to release their clipboard lock
        await Task.Delay(75);
        HandleClipboardUpdate();
    }

    private void HandleClipboardUpdate()
    {
        if (_settings?.SelectedProfile?.WatchMode != WatchMode.Clipboard) return;

        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(HandleClipboardUpdate));
                return;
            }

            string text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrEmpty(text) || text.Length > Scrubber.MaxLength) return;

            if (Scrubber.TryParse(text, _settings.SelectedProfile.CoordinateOrder, out _))
            {
                string coordinates = Scrubber.ScrubEntry(text) ?? string.Empty;
                Log.Debug("Clipboard coordinates detected: {Coordinates}", coordinates);

                LocationUpdated?.Invoke(this, coordinates);
            }
        }
        catch (COMException ex)
        {
            Log.Debug(ex, "Clipboard access collision (COMException). Retrying on next update.");
        }
        catch (ThreadStateException ex)
        {
            Log.Warning(ex, "ThreadStateException accessing Clipboard. Ensure call originates on STA thread.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error processing Clipboard update.");
        }
    }

    public void Dispose()
    {
        Stop();
    }
}