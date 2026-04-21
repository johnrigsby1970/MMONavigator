using System.ComponentModel;
using System.Windows.Threading;
using System.Media;

namespace MMONavigator.Services;

public class TimerController : INotifyPropertyChanged {
    private readonly int _initialMinutes;
    private int _secondsLeft;
    private DispatcherTimer? _timer;
    private string _displayText;
    private System.Windows.Media.Brush _background = System.Windows.Media.Brushes.CornflowerBlue;
    private System.Windows.Media.Brush _foreground = System.Windows.Media.Brushes.White;
    private const int WarningSecondsThreshold = 60;
    private const int UrgentSecondsThreshold = 30;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public TimerController(int minutes) {
        _initialMinutes = minutes;
        _secondsLeft = minutes * 60;
        _displayText = minutes.ToString();
    }

    public string DisplayText {
        get => _displayText;
        set {
            _displayText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText)));
        }
    }

    public System.Windows.Media.Brush Background {
        get => _background;
        set {
            _background = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
        }
    }

    public System.Windows.Media.Brush Foreground {
        get => _foreground;
        set {
            _foreground = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
        }
    }

    public void Toggle() {
        if (_timer != null) {
            Stop();
        } else {
            Start();
        }
    }

    private void Start() {
        _secondsLeft = _initialMinutes * 60;
        UpdateDisplay();
        _timer = new DispatcherTimer {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    public void Stop() {
        if (_timer != null) {
            _timer.Stop();
            _timer = null;
        }
        _secondsLeft = _initialMinutes * 60;
        DisplayText = _initialMinutes.ToString();
        Background = System.Windows.Media.Brushes.CornflowerBlue;
        Foreground = System.Windows.Media.Brushes.White;
    }

    private void Timer_Tick(object? sender, EventArgs e) {
        _secondsLeft--;
        if (_secondsLeft <= 0) {
            Stop();
            SystemSounds.Hand.Play();
            return;
        }
        UpdateDisplay();
    }

    private void UpdateDisplay() {
        int minutes = _secondsLeft / 60;
        int seconds = _secondsLeft % 60;
        DisplayText = $"{minutes}:{seconds:D2}";
        if (_secondsLeft <= UrgentSecondsThreshold) {
            Background = System.Windows.Media.Brushes.Red;
            Foreground = System.Windows.Media.Brushes.White;
        } else if (_secondsLeft <= WarningSecondsThreshold) {
            Background = System.Windows.Media.Brushes.Orange;
            Foreground = System.Windows.Media.Brushes.Black;
        } else {
            Background = System.Windows.Media.Brushes.CornflowerBlue;
            Foreground = System.Windows.Media.Brushes.White;
        }
    }
}
