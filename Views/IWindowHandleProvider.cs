namespace MMONavigator.Views;

public interface IWindowHandleProvider {
    /// <summary>
    /// Gets the current Win32 handle (HWND) for the window.
    /// </summary>
    public IntPtr GetWindowHandle();
}