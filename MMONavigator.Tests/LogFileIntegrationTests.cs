using System.IO;
using System.Threading.Tasks;
using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit;

public class LogFileIntegrationTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _logFilePath;

    public LogFileIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDirectory);
        _logFilePath = Path.Combine(_tempDirectory, "game.log");
        File.WriteAllText(_logFilePath, string.Empty);
    }

    [WpfFact]
    public async Task LogFileWatcher_AppendsCoordinates_UpdatesMainViewModelOnUIThread()
    {
        // 1. Arrange
        var settingsService = new SettingsService();
        var logProvider = new LogFileLocationProvider();
        var factory = new LocationProviderFactory(new ILocationProvider[] { logProvider });

        var mainViewModel = new MainViewModel(settingsService, factory);
        mainViewModel.Settings.SelectedProfile.WatchMode = WatchMode.File;
        mainViewModel.Settings.SelectedProfile.LogFilePath = _logFilePath;

        // Initialize mock window handle and start watcher
        mainViewModel.StartWatcher(new IntPtr(12345));

        // 2. Act: Append a log line from a background thread
        await Task.Run(async () =>
        {
            await Task.Delay(100); // Give FileSystemWatcher time to attach
            using var writer = File.AppendText(_logFilePath);
            await writer.WriteLineAsync("[12:00:00] Your Location is 500.0, 250.0, 10.0");
        });

        // 3. Assert: Wait briefly and yield to WPF Dispatcher loop
        await Task.Delay(300);
        await System.Windows.Threading.Dispatcher.Yield();

        Assert.Equal("500.0 250.0 10.0", mainViewModel.CurrentCoordinates);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}