// MMONavigator 
// Copyright (C) 2026 John Rigsby
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Windows;
using MMONavigator.Helpers;
using Serilog.Events;
using MessageBox = System.Windows.MessageBox;

namespace MMONavigator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application {
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        string sentryDsn = 
#if DEBUG
            string.Empty;
#else
            "https://7e437a24c753c741be86020237e35c01@o4511910567149568.ingest.us.sentry.io/4511910578683904";
#endif
        
        // 1. Initialize Sentry FIRST so it catches any startup failures
        SentrySdk.Init(o => {
            
            // Essential for WPF / desktop applications
            o.IsGlobalModeEnabled = true;

            o.SampleRate = 1.0f; // Capture 100% of crashes
            o.TracesSampleRate = 0.0; // Disable performance tracing (focused purely on crashes)

#if DEBUG
            // Blank out the DSN during local debugging so it never sends data to sentry.io
            o.Dsn = string.Empty;
            o.Debug = true;
            o.Environment = "development";
#else
            o.Dsn = sentryDsn;
            o.Debug = false;
            o.Environment = "production";
#endif
        });

        // Initialize logging first
        LogConfig.Initialize(sentryDsn);
        
        Log.Write(LogEventLevel.Debug, "Hello Sentry");


        // 2. Wire up global exception safety nets
        SetupGlobalExceptionHandling();

        // 3. Log session startup details
        Log.Information("{AppName} session started. OS: {OSVersion}, Version: {AppVersion}",
            Constants.AppName,
            Environment.OSVersion,
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        // 4. Run directory migration
        Methods.MigrateAppDataIfNeeded();
    }

    protected override void OnExit(ExitEventArgs e) {
        Log.Information("{AppName} shutting down normally.", Constants.AppName);
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>
    /// Forcibly re-enables MainWindow at the Win32 level if a modal crash left it disabled.
    /// </summary>
    public static void ForceUnlockMainWindow() {
        try {
            if (Current.MainWindow != null) {
                var helper = new System.Windows.Interop.WindowInteropHelper(Current.MainWindow);
                if (helper.Handle != IntPtr.Zero) {
                    // 1. Re-enable Win32 mouse/keyboard input to MainWindow
                    NativeMethods.EnableWindow(helper.Handle, true);

                    // 2. Force MainWindow to the foreground
                    NativeMethods.SetForegroundWindow(helper.Handle);

                    // 3. Ensure Topmost status is reapplied if needed
                    Current.MainWindow.Topmost = true;
                }
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error in ForceUnlockMainWindow during emergency recovery.");
        }
    }

    private void SetupGlobalExceptionHandling() {
        // 1. Unhandled WPF UI Thread Exceptions (App stays alive)
        DispatcherUnhandledException += (s, e) => {
            // SPECIAL CASE: Check if the crash happened during modal/dialog teardown
            if (e.Exception is NullReferenceException && e.Exception.StackTrace?.Contains("DoDialogHide") == true) {
                Log.Error(e.Exception, "Caught modal DoDialogHide crash! Forcibly unlocking MainWindow.");
                SentrySdk.CaptureException(e.Exception);

                // Recover MainWindow input state so the app doesn't freeze in a beep loop
                ForceUnlockMainWindow();

                // Mark exception as handled silently without showing a MessageBox
                e.Handled = true;
                return;
            }

            // GENERAL CASE: Standard unhandled UI exceptions
            Log.Error(e.Exception, "Unhandled UI dispatcher exception.");

            // Explicitly push to Sentry since e.Handled = true prevents a hard crash crash-dump
            SentrySdk.CaptureException(e.Exception);

            // Do NOT call Log.CloseAndFlush() here because e.Handled = true keeps Serilog running!
            MessageBox.Show(
                $"An unexpected UI error occurred: {e.Exception?.Message ?? "Unknown error"}",
                "Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            // Keep app alive for recoverable UI errors
            e.Handled = true;
        };

        // 2. Critical AppDomain / Non-UI Thread Crashes (App WILL terminate)
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            var ex = e.ExceptionObject as Exception;
            string errorDetails = ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown exception";

            Log.Fatal(ex, "Unhandled AppDomain exception. Terminating: {IsTerminating}. Details: {Details}",
                e.IsTerminating, errorDetails);
            if (ex != null) SentrySdk.CaptureException(ex);

            // Synchronously flush Serilog because process termination is imminent
            Log.CloseAndFlush();

            if (e.IsTerminating) {
                string userMessage =
                    $"A critical error occurred and the application must close:\n\n{ex?.Message ?? "Unknown error"}";

                // Safely show dialog on UI Thread if coming from a background thread
                if (Current != null && Current.Dispatcher.CheckAccess() == false) {
                    Current.Dispatcher.Invoke(() => {
                        MessageBox.Show(userMessage, "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                else {
                    MessageBox.Show(userMessage, "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        };

        // 3. Unobserved Async Task Exceptions
        TaskScheduler.UnobservedTaskException += (s, e) => {
            Log.Error(e.Exception, "Unobserved task exception caught.");

            // Prevent background task failures from tearing down process
            e.SetObserved();
        };
    }
}