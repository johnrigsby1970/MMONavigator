using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Reflection;

namespace MMONavigator.Helpers;

public static class Methods {
    public static string GetDisplayName(Enum enumValue) {
        return enumValue.GetType()
            .GetMember(enumValue.ToString())
            .First()
            .GetCustomAttribute<DisplayAttribute>()?
            .GetName() ?? enumValue.ToString();
    }

    public static string GetAppDataFolder() {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Constants.AppName
        );

        if (!Directory.Exists(folder)) {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    public static void MigrateAppDataIfNeeded() {
        try {
            string newFolder = GetAppDataFolder();
            string markerPath = Path.Combine(newFolder, ".migrated");

            // 1. Instant exit if we already migrated previously
            if (File.Exists(markerPath))
                return;

            string oldFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Constants.AppName
            );

            // Ensure target directory exists
            Directory.CreateDirectory(newFolder);

            // 2. Copy files from Roaming to Local
            if (Directory.Exists(oldFolder)) {
                foreach (string filePath in Directory.GetFiles(oldFolder, "*.*", SearchOption.AllDirectories)) {
                    string relativePath = Path.GetRelativePath(oldFolder, filePath);
                    string destinationPath = Path.Combine(newFolder, relativePath);

                    string? destinationDir = Path.GetDirectoryName(destinationPath);
                    if (destinationDir != null && !Directory.Exists(destinationDir)) {
                        Directory.CreateDirectory(destinationDir);
                    }

                    if (!File.Exists(destinationPath)) {
                        File.Copy(filePath, destinationPath, overwrite: false);
                    }
                }

                // Cleanup old directory (best-effort)
                try {
                    Directory.Delete(oldFolder, recursive: true);
                }
                catch (Exception deleteEx) {
                    Log.Warning(deleteEx, "Failed to delete old AppData folder during migration cleanup");
                }
            }

            // 3. Always drop marker after copy attempt finishes so we don't loop migration
            File.WriteAllText(markerPath, $"Migrated on {DateTime.UtcNow:O}");
            Log.Information("AppData migration to LocalAppData completed successfully.");
        }
        catch (Exception ex) {
            Log.Error(ex, "AppData migration failed.");
            // Do NOT re-throw here — allow app to proceed with startup
        }
    }
}