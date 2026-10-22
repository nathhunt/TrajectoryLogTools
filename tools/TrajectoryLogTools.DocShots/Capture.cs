// Capture.cs - turns a window (or a part of it) into a PNG: rendered with RenderTargetBitmap at a
// fixed DPI, optionally cropped to an element, framed like a window, and marked with numbered
// callouts that the guide's steps refer to ("select (1)").

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TrajectoryLogTools.DocShots
{
    /// <summary>A numbered marker over an element.</summary>
    public sealed class Callout
    {
        public int Number;
        public Func<FrameworkElement, FrameworkElement> Target;
        /// <summary>Where on the element: "left" (centre of the left edge, the default), "right", "top", "bottom", "topleft", "inside".</summary>
        public string At = "left";
    }

    public sealed class ShotOptions
    {
        /// <summary>Only this part of the window (with Padding around it), without a frame.</summary>
        public Func<FrameworkElement, FrameworkElement> Crop;
        /// <summary>Several elements: the crop is the box around all of them.</summary>
        public Func<FrameworkElement, IEnumerable<FrameworkElement>> CropAll;
        public double Padding = 10;
        /// <summary>Blank space added below the picture (DIPs), e.g. room for callouts under a strip of buttons.</summary>
        public double SpaceBelow;
        /// <summary>Draw a window frame (title bar with the icon and title). Default: for whole windows.</summary>
        public bool? Frame;
        public List<Callout> Callouts = new List<Callout>();

        public ShotOptions Mark(int number, Func<FrameworkElement, FrameworkElement> target, string at = "left")
        {
            Callouts.Add(new Callout { Number = number, Target = target, At = at });
            return this;
        }
    }

    public static class Capture
    {
        /// <summary>Pixels per inch of the images (1.5 x the 96 of a 100 % display: crisp on high-DPI screens).</summary>
        public const double Dpi = 144;
        static double Px { get { return Dpi / 96; } }

        const double Border = 2;        // DIPs; multiples of 2/3 DIP keep the content on whole pixels at 144 DPI
        const double TitleBar = 32;
        static readonly Brush CalloutFill = Frozen(new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C)));

        /// <summary>Renders <paramref name="root"/> (a window's client area) and writes the PNG.</summary>
        public static void Shoot(Window window, FrameworkElement root, string file, ShotOptions o)
        {
            o = o ?? new ShotOptions();
            root.UpdateLayout();
            double w = Math.Round(root.ActualWidth), h = Math.Round(root.ActualHeight);
            Brush background = window.Background ?? Brushes.White;

            // 1. the whole client area
            BitmapSource full = RenderElement(root, w, h, background);

            // 2. crop
            Rect area = new Rect(0, 0, w, h);
            IEnumerable<FrameworkElement> parts = o.CropAll != null ? o.CropAll(root) : o.Crop != null ? new[] { o.Crop(root) } : null;
            if (parts != null)
            {
                Rect box = Rect.Empty;
                foreach (FrameworkElement p in parts)
                {
                    if (p == null) throw new InvalidOperationException(Path.GetFileName(file) + ": the element to crop to was not found.");
                    box.Union(BoundsIn(p, root));
                }
                box.Inflate(o.Padding, o.Padding);
                box.Intersect(new Rect(0, 0, w, h));
                if (box.IsEmpty || box.Width < 1 || box.Height < 1)
                    throw new InvalidOperationException(Path.GetFileName(file) + ": the element to crop to is outside the window (make the window taller).");
                area = Snap(box);
            }
            BitmapSource content = area == new Rect(0, 0, w, h) ? full
                : new CroppedBitmap(full, new Int32Rect((int)Math.Round(area.X * Px), (int)Math.Round(area.Y * Px),
                                                        (int)Math.Round(area.Width * Px), (int)Math.Round(area.Height * Px)));

            // 3. frame and callouts
            bool frame = o.Frame ?? parts == null;
            Point origin = frame ? new Point(Border, Border + TitleBar) : new Point(0, 0);
            double ow = area.Width + (frame ? 2 * Border : 0), oh = area.Height + (frame ? 2 * Border + TitleBar : 0) + o.SpaceBelow;
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, ow, oh));
                if (frame) DrawFrame(dc, window, ow, oh);
                dc.DrawImage(content, new Rect(origin.X, origin.Y, area.Width, area.Height));
                foreach (Callout c in o.Callouts)
                {
                    FrameworkElement target = c.Target(root);
                    if (target == null) throw new InvalidOperationException(Path.GetFileName(file) + ": callout " + c.Number + "'s element was not found.");
                    Rect b = BoundsIn(target, root);
                    if (!VisibleInScrollViewer(target))
                        throw new InvalidOperationException(Path.GetFileName(file) + ": callout " + c.Number + "'s element is scrolled out of view.");
                    if (!new Rect(area.X - 1, area.Y - 1, area.Width + 2, area.Height + 2).IntersectsWith(b))
                        throw new InvalidOperationException(Path.GetFileName(file) + ": callout " + c.Number + "'s element is outside the picture.");
                    b.Offset(origin.X - area.X, origin.Y - area.Y);
                    DrawCallout(dc, c, b, new Rect(0, 0, ow, oh));
                }
            }
            var rtb = new RenderTargetBitmap((int)Math.Round(ow * Px), (int)Math.Round(oh * Px), Dpi, Dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            Save(rtb, file);
        }

        /// <summary>The element alone, e.g. a control made just for a picture (a status chip).</summary>
        public static void ShootElement(FrameworkElement e, Brush background, string file)
        {
            e.UpdateLayout();
            BitmapSource b = RenderElement(e, Math.Ceiling(e.ActualWidth), Math.Ceiling(e.ActualHeight), background);
            Save(b, file);
        }

        static BitmapSource RenderElement(FrameworkElement e, double w, double h, Brush background)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(background, null, new Rect(0, 0, w, h));
                // A visual brush over the element's own layout box: renders it where it is, however it sits in its parent.
                var brush = new VisualBrush(e) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h),
                                                 ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h),
                                                 AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
                dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap((int)Math.Round(w * Px), (int)Math.Round(h * Px), Dpi, Dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>False when the element is scrolled out of the view of a scroll viewer it is in.</summary>
        static bool VisibleInScrollViewer(FrameworkElement e)
        {
            for (DependencyObject d = VisualTreeHelper.GetParent(e); d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (!(d is System.Windows.Controls.ScrollViewer sv)) continue;
                Rect b = e.TransformToAncestor(sv).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
                if (b.Bottom < 2 || b.Top > sv.ActualHeight - 2) return false;   // ActualHeight: a list scrolls by items, and its ViewportHeight counts items
            }
            return true;
        }

        public static Rect BoundsIn(FrameworkElement e, FrameworkElement root)
        {
            return e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        }

        /// <summary>A rectangle on whole pixels.</summary>
        static Rect Snap(Rect r)
        {
            double x = Math.Floor(r.X * Px) / Px, y = Math.Floor(r.Y * Px) / Px;
            double x2 = Math.Ceiling(r.Right * Px) / Px, y2 = Math.Ceiling(r.Bottom * Px) / Px;
            return new Rect(x, y, x2 - x, y2 - y);
        }

        /// <summary>A plain window frame: border, title bar with the app's icon and title, caption buttons.</summary>
        static void DrawFrame(DrawingContext dc, Window window, double w, double h)
        {
            dc.DrawRectangle(Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A))), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(Frozen(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3))), null, new Rect(Border, Border, w - 2 * Border, TitleBar));
            double x = Border + 12;
            ImageSource icon = window.Icon ?? Program.IconFor(window);
            if (icon != null) { dc.DrawImage(icon, new Rect(x, Border + 8, 16, 16)); x += 26; }
            var title = Text(window.Title ?? "", 12, Brushes.Black, "Segoe UI");
            dc.DrawText(title, new Point(x, Border + (TitleBar - title.Height) / 2));
            string[] glyphs = { "", "", "" };   // minimise, maximise, close (Segoe MDL2 Assets)
            for (int i = 0; i < glyphs.Length; i++)
            {
                var g = Text(glyphs[i], 10, Brushes.Black, "Segoe MDL2 Assets");
                double cx = w - Border - 46 * (glyphs.Length - i) + 23;
                dc.DrawText(g, new Point(cx - g.Width / 2, Border + (TitleBar - g.Height) / 2));
            }
        }

        static void DrawCallout(DrawingContext dc, Callout c, Rect b, Rect image)
        {
            const double r = 13;
            Point p;
            switch (c.At)
            {
                case "right": p = new Point(b.Right + r + 2, b.Top + b.Height / 2); break;
                case "top": p = new Point(b.Left + b.Width / 2, b.Top - r - 2); break;
                case "bottom": p = new Point(b.Left + b.Width / 2, b.Bottom + r + 2); break;
                case "topleft": p = new Point(b.Left + 2, b.Top + 2); break;
                case "inside": p = new Point(b.Left + r + 4, b.Top + b.Height / 2); break;
                default: p = new Point(b.Left - r - 2, b.Top + b.Height / 2); break;
            }
            // keep it inside the image
            p.X = Math.Max(image.Left + r + 1, Math.Min(image.Right - r - 1, p.X));
            p.Y = Math.Max(image.Top + r + 1, Math.Min(image.Bottom - r - 1, p.Y));
            dc.DrawEllipse(Brushes.White, null, p, r + 2, r + 2);
            dc.DrawEllipse(CalloutFill, null, p, r, r);
            var t = Text(c.Number.ToString(CultureInfo.InvariantCulture), 14, Brushes.White, "Segoe UI", FontWeights.Bold);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }

        static FormattedText Text(string s, double size, Brush brush, string font, FontWeight? weight = null)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                                     new Typeface(new FontFamily(font), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
                                     size, brush, Px);
        }

        static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

        /// <summary>Writes the PNG only when it differs, so unchanged screenshots keep their file (and git sees no change).</summary>
        static void Save(BitmapSource b, string file)
        {
            var enc = new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
            enc.Frames.Add(BitmapFrame.Create(b));
            byte[] bytes;
            using (var ms = new MemoryStream()) { enc.Save(ms); bytes = ms.ToArray(); }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(bytes)) { Program.Log("  same     " + Path.GetFileName(file)); return; }
            File.WriteAllBytes(file, bytes);
            Program.Log("  written  " + Path.GetFileName(file) + "  (" + b.PixelWidth + " x " + b.PixelHeight + ")");
        }
    }
}
