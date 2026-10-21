// Angles.cs - showing gantry and collimator angles in IEC 61217 or the Varian Standard scale.
//
// A log stores its angles in the scale named in its header: axis scale 2 is IEC 61217, the
// machine scales (1, and 3 as Halcyon writes) and CSV exports are Varian Standard. Between the
// two, gantry and collimator are related by
//     IEC = (180 - Varian) mod 360        (and the same back: the relation is its own inverse)
// i.e. the zero is 180 degrees apart and the direction is reversed. This is the relation the MLC
// rebuild uses (MlcReconstructor.ToIec), checked there against Eclipse exports of the same beams.
// A difference (A - B, actual - expected) therefore only changes sign.
//
// Only gantry and collimator are converted. Couch rotation, pitch, roll and the couch and jaw
// positions are shown as recorded: their Varian Standard / IEC relations differ per axis and are
// not checked against data here (Halcyon has no jaws and no couch rotation).

using System;
using System.Globalization;
using System.Linq;

namespace TrajectoryLogTools.Analysis
{
    public enum AngleScale { Iec, Varian }

    public static class Angles
    {
        /// <summary>Column base names of the converted axes.</summary>
        public static readonly string[] ConvertedAxes = { "GantryRtn_deg", "CollRtn_deg" };

        public static bool IsConverted(string axis) { return ConvertedAxes.Contains(axis); }

        /// <summary>Axis scale 2 is IEC 61217; 0 (CSV), 1 and 3 are Varian Standard.</summary>
        public static AngleScale ScaleOf(int axisScale) { return axisScale == 2 ? AngleScale.Iec : AngleScale.Varian; }

        public static string Name(AngleScale s) { return s == AngleScale.Iec ? "IEC 61217" : "Varian Standard"; }

        /// <summary>An angle (gantry or collimator) from the log's scale to <paramref name="to"/>, in [0, 360).</summary>
        public static double Convert(double angle, AngleScale from, AngleScale to)
        {
            if (double.IsNaN(angle)) return angle;
            return Wrap360(from == to ? angle : 180.0 - angle);
        }

        /// <summary>+1, or -1 when the scales differ: how a difference of angles changes.</summary>
        public static double DifferenceSign(AngleScale from, AngleScale to) { return from == to ? 1.0 : -1.0; }

        /// <summary>A difference of two angles in (-180, 180].</summary>
        public static double WrapDifference(double d)
        {
            if (double.IsNaN(d)) return d;
            d = (d + 180.0) % 360.0;
            if (d < 0) d += 360.0;
            return d == 0 ? 180.0 : d - 180.0;
        }

        /// <summary>
        /// Converts the numbers in a text such as "181.0->179.0" or "90.0", keeping the separators and
        /// the number of decimals. Text without numbers is returned unchanged.
        /// </summary>
        public static string ConvertText(string text, AngleScale from, AngleScale to)
        {
            if (string.IsNullOrEmpty(text) || from == to) return text;
            return System.Text.RegularExpressions.Regex.Replace(text, @"-?\d+(\.\d+)?", m =>
            {
                double v;
                if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return m.Value;
                int dot = m.Value.IndexOf('.');
                int decimals = dot < 0 ? 0 : m.Value.Length - dot - 1;
                return Convert(v, from, to).ToString("F" + decimals, CultureInfo.InvariantCulture);
            });
        }

        static double Wrap360(double a)
        {
            a %= 360.0;
            if (a < 0) a += 360.0;
            return a >= 360.0 ? 0.0 : a;
        }
    }
}
