using System.ComponentModel;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MMONavigator.Base;

namespace MMONavigator.Services;

public class TimerController : ViewModelBase {
    private readonly int _initialMinutes;
    private int _secondsLeft;
    private DispatcherTimer? _timer;
    private string _displayText;
    private System.Windows.Media.Brush _background = System.Windows.Media.Brushes.CornflowerBlue;
    private System.Windows.Media.Brush _foreground = System.Windows.Media.Brushes.White;
    
    private const int WarningSecondsThreshold = 60;
    private const int UrgentSecondsThreshold = 30;
    
    public TimerController(int minutes) {
        if (minutes <= 0) {
            Log.Warning("TimerController initialized with invalid minutes ({Minutes}); defaulting to 1 minute.", minutes);
            minutes = 1;
        }

        _initialMinutes = minutes;
        _secondsLeft = minutes * 60;
        _displayText = minutes.ToString();
    }

    public string DisplayText {
        get => _displayText;
        set {
            if (_displayText != value) {
                _displayText = value;
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }

    public System.Windows.Media.Brush Background {
        get => _background;
        set {
            if (_background != value) {
                _background = value;
                OnPropertyChanged(nameof(Background));
            }
        }
    }

    public System.Windows.Media.Brush Foreground {
        get => _foreground;
        set {
            if (_foreground != value) {
                _foreground = value;
                OnPropertyChanged(nameof(Foreground));
            }
        }
    }

    public void Toggle() {
        try {
            if (_timer != null) {
                Stop();
            } else {
                Start();
            }
        }
        catch (Exception ex) {
            Log.Error(ex, "Error toggling timer state.");
        }
    }

    private void Start() {
        try {
            Log.Information("Starting timer for {Minutes} minutes.", _initialMinutes);

            _secondsLeft = _initialMinutes * 60;
            UpdateDisplay();

            if (_timer != null) {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
            }

            _timer = new DispatcherTimer {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error starting TimerController.");
        }
    }

    public void Stop() {
        try {
            if (_timer != null) {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
                _timer = null;
            }

            _secondsLeft = _initialMinutes * 60;
            DisplayText = _initialMinutes.ToString();
            Background = System.Windows.Media.Brushes.CornflowerBlue;
            Foreground = System.Windows.Media.Brushes.White;

            Log.Information("Stopped timer for {Minutes} minutes.", _initialMinutes);
        }
        catch (Exception ex) {
            Log.Error(ex, "Error stopping TimerController.");
        }
    }

    private void Timer_Tick(object? sender, EventArgs e) {
        try {
            _secondsLeft--;

            if (_secondsLeft <= 0) {
                Stop();
                PlayExpirationSound();
                return;
            }

            UpdateDisplay();
        }
        catch (Exception ex) {
            Log.Error(ex, "Error executing Timer_Tick.");
        }
    }

    private void PlayExpirationSound() {
        try {
            SystemSounds.Hand.Play();
        }
        catch (Exception ex) {
            Log.Warning(ex, "Failed to play expiration system sound.");
        }
    }

    private void UpdateDisplay() {
        try {
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
        catch (Exception ex) {
            Log.Error(ex, "Error updating timer display properties.");
        }
    }
}