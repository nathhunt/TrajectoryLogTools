// Display.cs - how angles are shown: IEC 61217 (the default) or Varian Standard. Gantry and
// collimator in the charts, tables and cards follow it; the files written (exports, Eclipse MLC
// files, saved reports' tables) are not affected. See TrajectoryLogTools.Analysis.Angles.

using System;
using System.Windows;
using System.Windows.Threading;
using TrajectoryLogTools.Analysis;

namespace TrajectoryLogTools.App.Services
{
    public static class Display
    {
        /// <summary>Raised (after the current input is handled) when the angle scale changes, so views redraw.</summary>
        public static event EventHandler AnglesChanged;

        public static AngleScale Angles
        {
            get { return AppSettings.Current.Angles == "Varian" ? AngleScale.Varian : AngleScale.Iec; }
        }

        /// <summary>"IEC 61217" or "Varian Standard".</summary>
        public static string AnglesName { get { return Analysis.Angles.Name(Angles); } }

        public static void SetAngles(AngleScale scale)
        {
            if (scale == Angles) return;
            AppSettings.Current.Angles = scale == AngleScale.Varian ? "Varian" : "IEC";
            AppSettings.Current.Save();
            // Deferred: views redraw once the click or selection that changed it has finished.
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var h = AnglesChanged;
                if (h != null) h(null, EventArgs.Empty);
            }));
        }

        /// <summary>A gantry or collimator angle of a log (axis scale <paramref name="axisScale"/>) in the chosen scale.</summary>
        public static double Angle(double value, int axisScale) { return Analysis.Angles.Convert(value, Analysis.Angles.ScaleOf(axisScale), Angles); }

        /// <summary>Angle text of a log ("181.0->179.0") in the chosen scale.</summary>
        public static string AngleText(string text, int axisScale) { return Analysis.Angles.ConvertText(text, Analysis.Angles.ScaleOf(axisScale), Angles); }

        /// <summary>+1 or -1: how an angle difference of a log changes in the chosen scale.</summary>
        public static double DifferenceSign(int axisScale) { return Analysis.Angles.DifferenceSign(Analysis.Angles.ScaleOf(axisScale), Angles); }
    }
}
