// WheelScrolling.cs - the mouse wheel over a table, list or text box inside a scrolling page.
//
// WPF gives the wheel to the innermost scroll viewer under the mouse, and a scroll viewer keeps
// the wheel even when it has nothing to scroll (a table sized to its rows, or one already at its
// end), so the page stops scrolling while the mouse is over it. Here, a scroll viewer that can't
// scroll further that way passes the wheel on to the next one out, so the page carries on.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TrajectoryLogTools.App.Services
{
    public static class WheelScrolling
    {
        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
        }

        static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            if (e.Handled || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
            // Only the scroll viewer nearest the mouse decides (the event tunnels through the outer ones first).
            if (Nearest<ScrollViewer>(e.OriginalSource as DependencyObject) != sv) return;
            bool up = e.Delta > 0;
            bool canScroll = sv.ScrollableHeight > 0 && (up ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight);
            if (canScroll) return;
            DependencyObject parent = Parent(sv);
            if (Nearest<ScrollViewer>(parent) == null) return;
            e.Handled = true;
            var pass = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sv };
            (parent as UIElement ?? (parent as FrameworkContentElement)?.Parent as UIElement)?.RaiseEvent(pass);
        }

        static T Nearest<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = Parent(d);
            return d as T;
        }

        static DependencyObject Parent(DependencyObject d)
        {
            if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            if (d is TextElement t) return t.Parent;
            return LogicalTreeHelper.GetParent(d);
        }
    }
}
