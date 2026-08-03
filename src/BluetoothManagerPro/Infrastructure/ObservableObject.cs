using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BluetoothManagerPro.Infrastructure;

/// <summary>
/// Minimal INotifyPropertyChanged base. Hand-rolled on purpose: the app ships with
/// zero NuGet dependencies so it builds offline with nothing but the .NET SDK.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns <paramref name="value"/> and raises the change notification if it differs.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
