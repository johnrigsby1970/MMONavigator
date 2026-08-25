using MMONavigator.Models;

namespace MMONavigator.Interfaces;

public interface ILocationProvider : IDisposable {
    event EventHandler<string>? LocationUpdated;
    void Start(AppSettings settings, IntPtr windowHandle);
    void Stop();
}
