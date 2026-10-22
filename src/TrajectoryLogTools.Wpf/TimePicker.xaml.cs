using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TrajectoryLogTools.Wpf
{
    /// <summary>A 24-hour time of day (hours and minutes), bound through <see cref="Time"/>.</summary>
    public partial class TimePicker : UserControl
    {
        public static readonly DependencyProperty TimeProperty =
            DependencyProperty.Register(nameof(Time), typeof(TimeSpan), typeof(TimePicker),
                new FrameworkPropertyMetadata(TimeSpan.Zero, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTimeChanged, Coerce));

        public TimeSpan Time { get { return (TimeSpan)GetValue(TimeProperty); } set { SetValue(TimeProperty, value); } }

        bool _syncing;

        public TimePicker()
        {
            InitializeComponent();
            Hours.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00", CultureInfo.InvariantCulture)).ToList();
            Minutes.ItemsSource = Enumerable.Range(0, 60).Select(m => m.ToString("00", CultureInfo.InvariantCulture)).ToList();
            Show();
        }

        /// <summary>Whole minutes in [00:00, 23:59].</summary>
        static object Coerce(DependencyObject d, object value)
        {
            var t = (TimeSpan)value;
            int minutes = (int)Math.Floor(t.TotalMinutes) % (24 * 60);
            if (minutes < 0) minutes += 24 * 60;
            return TimeSpan.FromMinutes(minutes);
        }

        static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { ((TimePicker)d).Show(); }

        void Show()
        {
            Shown.Text = Time.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" + Time.Minutes.ToString("00", CultureInfo.InvariantCulture);
            _syncing = true;
            Hours.SelectedIndex = Time.Hours;
            Minutes.SelectedIndex = Time.Minutes;
            _syncing = false;
        }

        void Part_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || Hours.SelectedIndex < 0 || Minutes.SelectedIndex < 0) return;
            Time = new TimeSpan(Hours.SelectedIndex, Minutes.SelectedIndex, 0);
        }

        void Popup_Opened(object sender, EventArgs e)
        {
            // show the chosen hour and minute, near the top of their lists
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (ListBox list in new[] { Hours, Minutes })
                {
                    if (list.SelectedItem == null) continue;
                    list.ScrollIntoView(list.Items[list.Items.Count - 1]);
                    list.ScrollIntoView(list.SelectedItem);
                }
                (Hours.ItemContainerGenerator.ContainerFromIndex(Hours.SelectedIndex) as ListBoxItem)?.Focus();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        void Done_Click(object sender, RoutedEventArgs e)
        {
            Popup.IsOpen = false;
            Button.Focus();
        }

        void Step(int minutes) { Time = Time + TimeSpan.FromMinutes(minutes); }

        // The wheel over the button changes the time (and doesn't scroll the dialog).
        void Button_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 60 : 1;
            Step(e.Delta > 0 ? step : -step);
            e.Handled = true;
        }

        void Button_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 60 : 1;
            if (e.Key == Key.Up) { Step(step); e.Handled = true; }
            else if (e.Key == Key.Down) { Step(-step); e.Handled = true; }
            else if (e.Key == Key.PageUp) { Step(60); e.Handled = true; }
            else if (e.Key == Key.PageDown) { Step(-60); e.Handled = true; }
        }
    }
}
