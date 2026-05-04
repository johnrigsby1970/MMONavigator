using System;
using System.Diagnostics;

namespace MMONavigator.Helpers;

public static class Logger
{
    private const string SourceName = "MMONavigator";
    private const string LogName = "Application";

    public static void LogError(string message, Exception? ex = null)
    {
        try
        {
            string logMessage = message;
            if (ex != null)
            {
                logMessage += $"\n\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
            }

            // Using EventLog.WriteEntry directly. 
            // Note: On some systems, creating the source requires administrative privileges.
            // If the source doesn't exist, this might fail.
            // However, typically for desktop apps, we might use a fallback or expect the installer to create the source.
            // As per instructions, we must write to eventviewer.
            
            EventLog.WriteEntry(SourceName, logMessage, EventLogEntryType.Error);
        }
        catch
        {
            // Fallback to Trace if EventLog fails
            Trace.WriteLine($"Failed to write to EventLog: {message}");
            if (ex != null) Trace.WriteLine(ex);
        }
    }
}
