using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using System.Media;

namespace MMONavigator;

public class TimerController : INotifyPropertyChanged {
    private readonly int _initialMinutes;
    private int _secondsLeft;
    private DispatcherTimer? _timer;
    private string _displayText;
    private Brush _background = Brushes.CornflowerBlue;
    private Brush _foreground = Brushes.White;
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

    public Brush Background {
        get => _background;
        set {
            _background = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
        }
    }

    public Brush Foreground {
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
        Background = Brushes.CornflowerBlue;
        Foreground = Brushes.White;
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
            Background = Brushes.Red;
            Foreground = Brushes.White;
        } else if (_secondsLeft <= WarningSecondsThreshold) {
            Background = Brushes.Orange;
            Foreground = Brushes.Black;
        } else {
            Background = Brushes.CornflowerBlue;
            Foreground = Brushes.White;
        }
    }
}
