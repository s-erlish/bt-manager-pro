using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BluetoothManagerPro.Models;

namespace BluetoothManagerPro.Infrastructure;

/// <summary>Maps a <see cref="DeviceCategory"/> to the matching geometry in Icons.xaml.</summary>
public sealed class CategoryToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is DeviceCategory category
            ? category switch
            {
                DeviceCategory.Headphones => "Icon.Headphones",
                DeviceCategory.Speaker => "Icon.Speaker",
                DeviceCategory.Microphone => "Icon.Speaker",
                DeviceCategory.Mouse => "Icon.Mouse",
                DeviceCategory.Keyboard => "Icon.Keyboard",
                DeviceCategory.Gamepad => "Icon.Gamepad",
                DeviceCategory.Phone => "Icon.Phone",
                DeviceCategory.Computer => "Icon.Laptop",
                DeviceCategory.Wearable => "Icon.Watch",
                DeviceCategory.Printer => "Icon.Printer",
                _ => "Icon.Device",
            }
            : "Icon.Device";

        return Application.Current?.TryFindResource(key) as Geometry ?? Geometry.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Visible when the bound bool is true; collapsed otherwise. Pass "invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Green above 40 %, amber down to 20 %, red below — the usual battery ramp.</summary>
public sealed class BatteryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            int and <= 20 => "Brush.Negative",
            int and <= 40 => "Brush.Warning",
            int => "Brush.Positive",
            _ => "Brush.Text.Tertiary",
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Turns a 0..4 signal rank into the opacity of one bar, chosen by ConverterParameter.</summary>
public sealed class SignalBarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int bars || !int.TryParse(parameter as string, out int index))
        {
            return 0.25;
        }

        return index <= bars ? 1.0 : 0.25;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
