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

    /// <summary>
    /// A radio button for one value of a bool: checked when the bool equals the parameter ("true" or
    /// "false"), and when checked it sets the bool to that value. Being unchecked writes nothing.
    /// A radio button's group unchecks the others in it; if that "unchecked" were written back (as a
    /// plain or inverted binding does), the two buttons of a pair can set each other back and forth
    /// without end, which overflows the stack (seen when the theme changed on the Rebuild page).
    /// </summary>
    public sealed class RadioBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b == Wanted(parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? (object)Wanted(parameter) : Binding.DoNothing;
        }

        static bool Wanted(object parameter) { return !string.Equals(parameter as string, "false", StringComparison.OrdinalIgnoreCase); }
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
