// Shots.cs - what a scenario works with: the demo logs, opening windows off screen at a fixed size,
// and taking the picture once the window has settled (with machine-specific text replaced).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.DocShots
{
    public sealed class Shots
    {
        public DemoData Demo { get; }
        public string Output { get; }
        readonly List<Window> _open = new List<Window>();

        /// <summary>Where the two apps are shown to run from, instead of this tool's own folder.</summary>
        public const string ToolsFolder = @"C:\TrajectoryLogTools\";
        public const string ConverterFolder = @"C:\TrajectoryLogConverter\";

        public Shots(DemoData demo, string output)
        {
            Demo = demo;
            Output = output;
        }

        /// <summary>
        /// Shows the window off screen with a client area of exactly <paramref name="width"/> x
        /// <paramref name="height"/> (null: the height the window sizes itself to).
        /// </summary>
        public async Task<T> Show<T>(T w, double width, double? height = null) where T : Window
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -30000;
            w.Top = -30000;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
            bool autoHeight = height == null;
            w.SizeToContent = autoHeight ? SizeToContent.Height : SizeToContent.Manual;
            w.MinWidth = 0;
            w.MinHeight = 0;
            w.Width = width + 16;
            if (!autoHeight) w.Height = height.Value + 39;
            w.Show();
            _open.Add(w);
            await SizeClient(w, width, height);
            if (autoHeight) await SizeClient(w, width, null);   // re-wrapping at the new width changes the height
            return w;
        }

        // Windows keeps a window no larger than the screen (a CI runner's is 1024 x 768) when it is
        // sized the usual way. Sized with SWP_NOSENDCHANGING, that limit isn't applied.
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOSENDCHANGING = 0x0400, SWP_NOMOVE = 0x0002;

        /// <summary>Sizes the window so its client area is width x height (DIPs); height null = keep it.</summary>
        static async Task SizeClient(Window w, double width, double? height)
        {
            IntPtr handle = new WindowInteropHelper(w).Handle;
            for (int i = 0; i < 4; i++)
            {
                await Ui.Settle(100);
                FrameworkElement root = ClientRoot(w);
                double dw = width - root.ActualWidth, dh = height.HasValue ? height.Value - root.ActualHeight : 0;
                if (Math.Abs(dw) < 0.01 && Math.Abs(dh) < 0.01) return;
                Matrix toDevice = PresentationSource.FromVisual(w).CompositionTarget.TransformToDevice;
                double outerW = w.ActualWidth + dw, outerH = w.ActualHeight + dh;
                SetWindowPos(handle, IntPtr.Zero, 0, 0, (int)Math.Round(outerW * toDevice.M11), (int)Math.Round(outerH * toDevice.M22),
                             SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSENDCHANGING);
            }
            FrameworkElement r = ClientRoot(w);
            if (Math.Abs(width - r.ActualWidth) > 0.5 || (height.HasValue && Math.Abs(height.Value - r.ActualHeight) > 0.5))
                Program.Log("  note: client area is " + r.ActualWidth + " x " + r.ActualHeight + ", not " + width + " x " + height);
        }

        /// <summary>Changes the client height of a shown window (e.g. taller, to show a whole page).</summary>
        public async Task Resize(Window w, double width, double height)
        {
            await SizeClient(w, width, height);
        }

        public static FrameworkElement ClientRoot(Window w)
        {
            return (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
        }

        public void Close(Window w)
        {
            _open.Remove(w);
            try { w.Close(); } catch (Exception) { }
        }

        public void CloseAll()
        {
            foreach (Window w in _open.ToList()) Close(w);
        }

        /// <summary>Waits for the window to settle, replaces machine-specific text, and writes docs/user/images/&lt;name&gt;.png.</summary>
        public async Task Shoot(Window w, string name, ShotOptions o = null)
        {
            await Ui.Settle();
            Sanitise(w);
            ClientRoot(w).UpdateLayout();
            Capture.Shoot(w, ClientRoot(w), Path.Combine(Output, name + ".png"), o);
        }

        public void ShootElement(FrameworkElement e, string name)
        {
            Capture.ShootElement(e, (Brush)Application.Current.FindResource("ApplicationBackgroundBrush"), Path.Combine(Output, name + ".png"));
        }

        /// <summary>
        /// The line at the bottom of each window (version, Windows, .NET, the app's folder) and the
        /// converter's settings path would show this PC; they show a fixed environment instead.
        /// </summary>
        void Sanitise(Window w)
        {
            bool converter = w.GetType().Namespace.StartsWith("TrajectoryLogTools.Converter", StringComparison.Ordinal);
            string folder = converter ? ConverterFolder : ToolsFolder;
            string env = "Version " + AppVersion.Version + "  ·  Microsoft Windows 10.0.19045  ·  .NET 10.0.0  ·  " + folder;
            string here = AppContext.BaseDirectory;
            foreach (DependencyObject d in Ui.Descendants(w).ToList())
            {
                if (d is TextBlock t)
                {
                    if (t.Text == AppVersion.EnvironmentText) t.Text = env;
                    else if (t.Inlines.Count > 1)
                        foreach (Run r in t.Inlines.OfType<Run>().Where(r => r.Text != null && r.Text.Contains(here)).ToList())
                            r.Text = r.Text.Replace(here, folder);
                    else if (t.Text != null && t.Text.Contains(here)) t.Text = t.Text.Replace(here, folder);
                }
                else if (d is TextBox b && b.Text != null && b.Text.Contains(here)) b.Text = b.Text.Replace(here, folder);
            }
        }
    }

    public sealed class Scenario
    {
        public string Name;
        public string Description;
        public Func<Shots, Task> Run;
    }
}
