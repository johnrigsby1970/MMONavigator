using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MMONavigator;

//https://stackoverflow.com/questions/21461017/wpf-window-with-transparent-background-containing-opaque-controls
//https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality
//https://corey255a1.wixsite.com/wundervision/single-post/simple-wpf-compass-control

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged {
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private readonly IntPtr _windowHandle;
    private readonly Regex _rgx = new Regex("[^0-9- .]");

    public event EventHandler? ClipboardUpdate;

    public MainWindow() {
        InitializeComponent();
        myGrid.DataContext = this;
        Topmost = true;
        _windowHandle = new WindowInteropHelper(this).EnsureHandle();
        HwndSource.FromHwnd(_windowHandle)?.AddHook(HwndHandler);
        Start();
    }

    private string? _currentCoordinates = "";

    public string? CurrentCoordinates {
        get => _currentCoordinates;
        set {
            _currentCoordinates = value;
            ShowDirection();
            OnPropertyChanged();
        }
    }

    private string? _targetCoordinates = "";

    public string? TargetCoordinates {
        get => _targetCoordinates;
        set {
            _targetCoordinates = value;
            ShowDirection();
            OnPropertyChanged();
        }
    }

    private string? _tX;

    public string? Tx {
        get => _tX;
        set {
            _tX = value;
            OnPropertyChanged();
        }
    }

    private string? _tY;

    public string? Ty {
        get => _tY;
        set {
            _tY = value;
            OnPropertyChanged();
        }
    }

    private string? _cX;

    public string? Cx {
        get => _cX;
        set {
            _cX = value;
            OnPropertyChanged();
        }
    }

    private string? _cY;

    public string? Cy {
        get => _cY;
        set {
            _cY = value;
            OnPropertyChanged();
        }
    }

    private string? _goDirection;

    public string? GoDirection {
        get => _goDirection;
        set {
            _goDirection = value;
            OnPropertyChanged();
        }
    }

    public static readonly DependencyProperty ClipboardUpdateCommandProperty =
        DependencyProperty.Register("ClipboardUpdateCommand", typeof(ICommand), typeof(MainWindow), new FrameworkPropertyMetadata(null));

    public ICommand ClipboardUpdateCommand {
        get { return (ICommand)GetValue(ClipboardUpdateCommandProperty); }
        set { SetValue(ClipboardUpdateCommandProperty, value); }
    }

    protected virtual void OnClipboardUpdate() {
        try {
            // var s = Clipboard.GetText();
            // s = s.Replace("/jumploc ", "");
            //
            // s = _rgx.Replace(s, "");
            // s = s.Trim();
            CurrentCoordinates = ScrubEntry(Clipboard.GetText());
        }
        catch {
            GoDirection = string.Empty;
        }
    }

    private string? ScrubEntry(string? value) {
        try {
            var s = value;
            s = s.Replace("/jumploc ", "");

            s = _rgx.Replace(s, "");
            s = s.Trim();
            return s;
        }
        catch {
            return value;
        }
    }

    protected virtual void ShowDirection() {
        try {
            GoDirection = string.Empty;
            var s = ScrubEntry(CurrentCoordinates);//may have been user entered

            if (!string.IsNullOrWhiteSpace(TargetCoordinates)) {
                var target = TargetCoordinates.Split(' ');
                var targetValid = true;
                foreach (var item in target) {
                    if (!decimal.TryParse(item, out decimal _)) {
                        targetValid = false;
                        //return;
                    }
                }

                if (!targetValid) return;
                if (target.Length <= 1) return;

                 if (!string.IsNullOrWhiteSpace(s)) {
                    var coordinates = s.Split(' ');
                    var currentValid = true;
                    foreach (var item in coordinates) {
                        if (!decimal.TryParse(item, out decimal _)) {
                            currentValid = false;
                            //continue;
                        }
                    }

                    if (!currentValid) return;
                    if (coordinates.Length < 3) return;

                    var x1 = double.Parse(coordinates[0]);
                    var x2 = double.Parse(target[0]);
                    var y1 = double.Parse(coordinates[2]);
                    var y2 = (target.Length > 2) ? double.Parse(target[2]) : double.Parse(target[1]);

                    Tx = $"x:{x2}";
                    Ty = $"y:{y2}";

                    Cx = $"x:{x1}";
                    Cy = $"y:{y1}";

                    //double direction = GetDirection(double.Parse(target[0]), double.Parse(target[2]), double.Parse(coordinates[0]), double.Parse(coordinates[2]));
                    double direction = GetDirection(x1, y1, x2, y2);

                    try {
                        //labelDirection.Foreground = Brushes.White;
                        labelDirection.Fill = Brushes.White;
                        if (coordinates.Length > 3) {
                            //4th number is facing in compass degrees
                            if (double.Parse(coordinates[3]) >= direction - 2 && double.Parse(coordinates[3]) <= direction + 2) {
                                labelDirection.Fill = Brushes.Green;
                            }
                            else if (double.Parse(coordinates[3]) >= direction - 4 && double.Parse(coordinates[3]) <= direction + 4) {
                                labelDirection.Fill = Brushes.YellowGreen;
                            }
                            else if (double.Parse(coordinates[3]) >= direction - 6 && double.Parse(coordinates[3]) <= direction + 6) {
                                labelDirection.Fill = Brushes.Yellow;
                            }
                        }
                    }
                    catch {
                        //ignore
                    }

                    double distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
                    if (distance <= 10) {
                        labelDirection.Fill = Brushes.Green;
                    }

                    GoDirection = GetCompassDirection(direction) + $" {Convert.ToInt32(direction)}\u00b0 {Convert.ToInt32(distance)}";
                }
            }
        }
        catch {
            GoDirection = string.Empty;
        }
    }

    private static string GetCompassDirection(double angle) {
        // Normalize angle to the range [0, 360)
        angle = (angle % 360 + 360) % 360;

        if (angle >= 0 && angle < 22.5) {
            return "N"; // North
        }
        else if (angle >= 22.5 && angle < 67.5) {
            return "NE"; // Northeast
        }
        else if (angle >= 67.5 && angle < 112.5) {
            return "E"; // East
        }
        else if (angle >= 112.5 && angle < 157.5) {
            return "SE"; // Southeast
        }
        else if (angle >= 157.5 && angle < 202.5) {
            return "S"; // South
        }
        else if (angle >= 202.5 && angle < 247.5) {
            return "SW"; // Southwest
        }
        else if (angle >= 247.5 && angle < 292.5) {
            return "W"; // West
        }
        else if (angle >= 292.5 && angle < 337.5) {
            return "NW"; // Northwest
        }
        else {
            return "N"; // North
        }
    }

    private static double GetDirection(double x1, double y1, double x2, double y2) {
        // Calculate the difference in x and y coordinates
        double dx = x2 - x1;
        double dy = y2 - y1;

        // Use Atan2 to calculate the angle in radians
        double angleRad = Math.Atan2(dy, dx);

        // Convert the angle from radians to degrees
        double angleDeg = angleRad * 180 / Math.PI;

        // Normalize the angle to be between 0 and 360 degrees
        while (angleDeg < 0) {
            angleDeg += 360;
        }

        while (angleDeg >= 360) {
            angleDeg -= 360;
        }

        return ReverseAngle(angleDeg);
    }

    private static double ReverseAngle(double angleInDegrees) {
        // Ensure the angle is within the 0-360 degree range
        double normalizedAngle = angleInDegrees % 360;

        // If the normalized angle is negative, add 360 to make it positive
        if (normalizedAngle < 0) {
            normalizedAngle += 360;
        }

        // Calculate the reversed angle (180 degrees difference)
        var reversed = (180 - normalizedAngle) % 360;
        reversed = reversed - 90;
        if (reversed < 0) {
            reversed += 360;
        }

        return reversed;
    }

    private void Start() {
        NativeMethods.AddClipboardFormatListener(_windowHandle);
    }

    private void Stop() {
        NativeMethods.RemoveClipboardFormatListener(_windowHandle);
    }

    private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (msg == WM_CLIPBOARDUPDATE) {
            // fire event
            ClipboardUpdate?.Invoke(this, new EventArgs());
            // execute command
            if (ClipboardUpdateCommand?.CanExecute(null) ?? false) {
                ClipboardUpdateCommand?.Execute(null);
            }

            // call virtual method
            OnClipboardUpdate();
        }

        handled = false;
        return IntPtr.Zero;
    }


    private static class NativeMethods {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }

    #region Title Bar

    //https://stackoverflow.com/questions/55447212/how-do-i-make-a-transparent-wpf-window-with-the-default-title-bar-functionality

    /// <summary>
    /// Min
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Minimized) {
            WindowState = WindowState.Minimized;
        }
    }

    /// <summary>
    /// Max
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) {
        if (WindowState != WindowState.Maximized) {
            WindowState = WindowState.Normal;
        }
    }

    /// <summary>
    /// Close
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Stop();
        Close();
    }

    /// <summary>
    /// DragMove
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        this.DragMove();
    }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}