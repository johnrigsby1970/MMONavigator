using System.Threading.Tasks;
using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit;

public class ProfileThreadSafetyTests
{
    [WpfFact]
    public async Task ProfilePropertyChanged_FiredFromBackgroundThread_DoesNotThrow()
    {
        // 1. Arrange
        var settingsService = new SettingsService();
        var factory = new LocationProviderFactory(new ILocationProvider[] { new LogFileLocationProvider() });
        var mainViewModel = new MainViewModel(settingsService, factory);

        Exception? caughtException = null;

        // 2. Act: Mutate profile settings on a background ThreadPool thread
        await Task.Run(() =>
        {
            try
            {
                // Changing WatchMode triggers Profile_PropertyChanged off-thread
                mainViewModel.Settings.SelectedProfile.WatchMode = WatchMode.Clipboard;
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        });

        // Yield to let the Dispatcher process queued property callbacks
        await System.Windows.Threading.Dispatcher.Yield();

        // 3. Assert
        Assert.Null(caughtException);
        Assert.Equal(WatchMode.Clipboard, mainViewModel.Settings.SelectedProfile.WatchMode);
    }
}