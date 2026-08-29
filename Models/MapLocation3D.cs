using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MMONavigator.Base;

namespace MMONavigator.Models;

public class MapLocation3D : ViewModelBase
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private double _x;
    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    private double _y;
    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    private double _z;
    public double Z
    {
        get => _z;
        set { _z = value; OnPropertyChanged(); }
    }

    private Visibility _visibility = Visibility.Collapsed;
    public Visibility Visibility
    {
        get => _visibility;
        set { _visibility = value; OnPropertyChanged(); }
    }
}