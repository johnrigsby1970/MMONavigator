using System;
using System.Threading.Tasks;
using MMONavigator.Interfaces;
using MMONavigator.Models;
using MMONavigator.Services;
using MMONavigator.ViewModels;
using Xunit; // or NUnit.Framework

public class LocationThreadSafetyTests
{
    [WpfFact] // Executes the test on a WPF STA UI Thread with an active Dispatcher
    public async Task LocationUpdated_RaisedFromBackgroundThread_DoesNotThrowThreadException()
    {
        // 1. Arrange: Instantiate dependencies and ViewModel on UI Thread
        var settingsService = new SettingsService();
        var mockFactory = new LocationProviderFactory(Array.Empty<ILocationProvider>());
        
        var mainViewModel = new MainViewModel(settingsService, mockFactory);
        
        string testCoordinates = "100.0, 200.0";
        Exception? caughtException = null;

        // 2. Act: Fire location updates from a background worker thread
        await Task.Run(() =>
        {
            try
            {
                // Accessing OnLocationUpdated directly simulates a background FileSystemWatcher 
                // or Shared Memory provider pushing data from off-thread
                var method = typeof(MainViewModel).GetMethod(
                    "OnLocationUpdated", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                method?.Invoke(mainViewModel, new object[] { this, testCoordinates });
            }
            catch (Exception ex)
            {
                // Capture the inner InvalidOperationException if thread marshaling fails
                caughtException = ex.InnerException ?? ex;
            }
        });

        // Force the WPF Dispatcher to drain its queue and process the marshaled action
        await System.Windows.Threading.Dispatcher.Yield();

        // 3. Assert
        Assert.Null(caughtException);
        Assert.Equal(testCoordinates, mainViewModel.CurrentCoordinates);
    }
}