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

using System.IO;
using System.Windows;
using System.Windows.Threading;
using MMONavigator.Helpers;

namespace MMONavigator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application {
    
    protected override void OnStartup(StartupEventArgs e) {
        // Migrate data from installation folder to AppData if needed
        MigrateData();

        // Disable hardware acceleration for this process
        System.Windows.Media.RenderOptions.ProcessRenderMode = 
            System.Windows.Interop.RenderMode.SoftwareOnly;
        
        base.OnStartup(e);
        
        // 1. Catches unhandled exceptions on the main UI thread
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. Catches unhandled exceptions on background worker/Task threads
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. Catches unhandled exceptions in unobserved async Tasks
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        
        // Prevent the app from crashing!
        e.Handled = true;

        System.Windows.MessageBox.Show("An unexpected error occurred, but the app recovered. Check the crash log for details.", 
            "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved(); // Prevents process termination on older framework behavior
    }

    private void LogCrash(Exception ex)
    {
        try
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YourAppName", "crash_log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now}] {ex}\n\n");
        }
        catch { /* Fallback if logging fails */ }
    }
    
    private void MigrateData() {
        try {
            string source = NativeMethods.BaseFolder();
            string destination = NativeMethods.AppFolder();

            if (source == destination) return;

            string[] filesToMigrate = { "settings.json", "locations.json" };
            foreach (string fileName in filesToMigrate) {
                string sourceFile = Path.Combine(source, fileName);
                string destFile = Path.Combine(destination, fileName);

                if (File.Exists(sourceFile) && !File.Exists(destFile)) {
                    File.Copy(sourceFile, destFile);
                }
            }

            // Migrate folders
            string[] foldersToMigrate = { "maps", "challenges" };
            foreach (string folderName in foldersToMigrate) {
                string sourceDir = Path.Combine(source, folderName);
                string destDir = Path.Combine(destination, folderName);

                if (Directory.Exists(sourceDir)) {
                    if (!Directory.Exists(destDir)) {
                        Directory.CreateDirectory(destDir);
                    }

                    foreach (string file in Directory.GetFiles(sourceDir)) {
                        string destFile = Path.Combine(destDir, Path.GetFileName(file));
                        if (!File.Exists(destFile)) {
                            File.Copy(file, destFile);
                        }
                    }
                }
            }
        }
        catch {
            // Best effort migration
        }
    }
}