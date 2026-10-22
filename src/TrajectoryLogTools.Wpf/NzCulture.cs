// NzCulture.cs - dates in the New Zealand format (dd/MM/yyyy, 24-hour) whatever the PC's regional
// settings: the date pickers and any date WPF formats itself follow the culture. Text the apps
// write uses Core.Fmt, which is fixed to the same format.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace TrajectoryLogTools.Wpf
{
    public static class NzCulture
    {
        /// <summary>Call once at start-up, before any window is created.</summary>
        public static void Apply()
        {
            try
            {
                var nz = (CultureInfo)CultureInfo.GetCultureInfo("en-NZ").Clone();
                nz.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
                nz.DateTimeFormat.ShortTimePattern = "HH:mm";
                nz.DateTimeFormat.LongTimePattern = "HH:mm:ss";
                CultureInfo.DefaultThreadCurrentCulture = nz;
                CultureInfo.CurrentCulture = nz;
                FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-NZ")));
            }
            catch (Exception ex) { ErrorLog.Write("Date format", ex); }
        }
    }
}
