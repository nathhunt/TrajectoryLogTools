// Ui.cs - finding things in a window's visual tree, and waiting until the UI has settled.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrajectoryLogTools.DocShots
{
    public static class Ui
    {
        /// <summary>Lets everything queued run (bindings, layout, deferred redraws, async continuations), then a little longer for animations.</summary>
        public static async Task Settle(int ms = 400)
        {
            for (int i = 0; i < 4; i++)
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await Task.Delay(ms / 4);
            }
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }

        /// <summary>Waits (keeping the UI running) until <paramref name="done"/> is true; throws after <paramref name="timeoutS"/> seconds.</summary>
        public static async Task WaitUntil(Func<bool> done, string what, int timeoutS = 120)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(timeoutS);
            while (!done())
            {
                if (DateTime.UtcNow > until) throw new TimeoutException("Timed out waiting for " + what + ".");
                await Task.Delay(50);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            await Settle();
        }

        public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                DependencyObject d = stack.Pop();
                yield return d;
                int n = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(d) : 0;
                for (int i = n - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
        }

        /// <summary>The first element of type T (in reading order) that matches; null if none.</summary>
        public static T Find<T>(DependencyObject root, Func<T, bool> match = null) where T : DependencyObject
        {
            return Descendants(root).OfType<T>().FirstOrDefault(x => match == null || match(x));
        }

        public static List<T> FindAll<T>(DependencyObject root, Func<T, bool> match = null) where T : DependencyObject
        {
            return Descendants(root).OfType<T>().Where(x => match == null || match(x)).ToList();
        }

        /// <summary>The text an element shows: a TextBlock's text, or the text inside a button's content.</summary>
        public static string TextOf(DependencyObject d)
        {
            if (d is TextBlock t) return string.IsNullOrEmpty(t.Text) ? string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text)) : t.Text;
            if (d is ContentControl c)
            {
                if (c.Content is string s) return Displayed(s);
                return string.Join(" ", Descendants(c).OfType<TextBlock>().Select(TextOf).Where(x => !string.IsNullOrEmpty(x)));
            }
            return null;
        }

        /// <summary>A label as a content control shows it: "_" marks an access key and isn't shown; "__" shows one "_".</summary>
        static string Displayed(string s)
        {
            return s.Replace("__", "\u0001").Replace("_", "").Replace("\u0001", "_");
        }

        /// <summary>The visible button (or check box, radio button, ...) whose text is <paramref name="text"/>.</summary>
        public static FrameworkElement Button(DependencyObject root, string text)
        {
            return Find<ButtonBase>(root, b => b.IsVisible && TextOf(b) == text);
        }

        /// <summary>The visible TextBlock whose text is (or starts with) <paramref name="text"/>.</summary>
        public static TextBlock Text(DependencyObject root, string text, bool startsWith = false)
        {
            return Find<TextBlock>(root, t => t.IsVisible && TextOf(t) != null &&
                                              (startsWith ? TextOf(t).StartsWith(text, StringComparison.Ordinal) : TextOf(t) == text));
        }

        /// <summary>The nearest ancestor of type T.</summary>
        public static T Up<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = VisualTreeHelper.GetParent(d);
            return d as T;
        }

        /// <summary>Scrolls the nearest scroll viewer so <paramref name="e"/> is at its top (less <paramref name="margin"/>).</summary>
        public static async Task ScrollToTop(FrameworkElement e, double margin = 8)
        {
            var sv = Up<ScrollViewer>(VisualTreeHelper.GetParent(e));
            if (sv == null) return;
            sv.UpdateLayout();
            Point at = e.TransformToAncestor(sv).Transform(new Point(0, 0));
            sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + at.Y - margin));
            await Settle(300);
        }

        /// <summary>The expander whose header shows <paramref name="startsWith"/>.</summary>
        public static Expander Expander(DependencyObject root, string startsWith)
        {
            return Find<Expander>(root, e => e.IsVisible && ((e.Header as string)?.StartsWith(startsWith, StringComparison.Ordinal) == true ||
                                                             (e.Header is DependencyObject h && Descendants(h).OfType<TextBlock>().Any(t => (TextOf(t) ?? "").StartsWith(startsWith, StringComparison.Ordinal)))));
        }
    }
}
