using System;
using MMONavigator.Models;

namespace MMONavigator.Interfaces;

public interface IWatcherService : IDisposable {
    event EventHandler<string>? LocationUpdated;
    void Start(AppSettings settings, IntPtr windowHandle);
    void Stop();
}
