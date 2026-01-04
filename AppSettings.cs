using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MMONavigator;

public class AppSettings : INotifyPropertyChanged {
    private string _coordinateOrder = "x z y d";
    public string CoordinateOrder {
        get => _coordinateOrder;
        set {
            if (_coordinateOrder != value) {
                _coordinateOrder = value;
                OnPropertyChanged();
            }
        }
    }

    public List<string> AvailableCoordinateOrders { get; set; } = new List<string> { "x z y d", "y x" };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
