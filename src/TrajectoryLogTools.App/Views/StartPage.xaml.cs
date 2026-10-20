using System;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App.Views
{
    public partial class StartPage : UserControl
    {
        public StartPage() { InitializeComponent(); }
    }

    /// <summary>File name / shortened folder of a path, for the recent list.</summary>
    public sealed class PathParts : IValueConverter
    {
        public static readonly PathParts FileName = new PathParts { _folder = false };
        public static readonly PathParts Folder = new PathParts { _folder = true };
        bool _folder;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string p = value as string ?? "";
            return _folder ? LogItem.ShortenFolder(Path.GetDirectoryName(p)) : Path.GetFileName(p);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
