using System.IO;

namespace MMONavigator;

public static class LogConfig
{
    public static void Initialize()
    {
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Helpers.Constants.AppName, "Logs");
        try
        {
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            var logFilePath = Path.Combine(logDirectory, "log-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true, buffered: false)
                .WriteTo.Sentry(o =>
                {
                    // Tell Serilog NOT to re-initialize the SDK (we already initialized it in OnStartup)
                    o.InitializeSdk = false;
                    o.Dsn = "https://7e437a24c753c741be86020237e35c01@o4511910567149568.ingest.us.sentry.io/4511910578683904";
                    // Log messages at Warning/Error become Sentry breadcrumbs or events
                    o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
                    o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
                })
                .CreateLogger();

            Log.Information("Application starting up...");
        }
        catch (Exception ex)
        {
            // Fallback if logging fails to initialize
            System.Diagnostics.Debug.WriteLine($"Failed to initialize logging: {ex}");
        }
    }

    public static void Shutdown()
    {
        Log.Information("Application shutting down...");
        Log.CloseAndFlush();
    }
}
