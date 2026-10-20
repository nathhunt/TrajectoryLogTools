// Converters.cs - small value converters used in the XAML.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrajectoryLogTools.App.Services
{
    /// <summary>true -> false and back (two-way).</summary>
    public sealed class NotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return !(value is bool b && b); }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return !(value is bool b && b); }
    }

    /// <summary>Visible when the value is not null / not an empty string.</summary>
    public sealed class NotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool empty = value == null || (value is string s && s.Length == 0);
            return empty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
