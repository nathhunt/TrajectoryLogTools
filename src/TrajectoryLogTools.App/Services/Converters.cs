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

    /// <summary>Visible when the value is not null / not an empty string / not zero.</summary>
    public sealed class NotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool empty = value == null || (value is string s && s.Length == 0) || (value is int i && i == 0) ||
                         (value is System.Collections.ICollection c && c.Count == 0);
            if (parameter as string == "invert") empty = !empty;
            return empty ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    /// <summary>" · " after a value that is there, "" otherwise (for joining Runs).</summary>
    public sealed class NotEmptyToSeparatorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? "" : " · ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    /// <summary>Collapsed when true (the inverse of BooleanToVisibilityConverter).</summary>
    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }

    /// <summary>fraction (0..1) x available width -> width, for the delivery timeline.</summary>
    public sealed class FractionToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || !(values[0] is double f) || !(values[1] is double w) || double.IsNaN(w)) return 0.0;
            return Math.Max(0, Math.Floor(f * w));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
