// ComparisonRules.cs - the judgements the Compare pages make from the numbers: whether two logs are
// the same plan delivered twice, and whether a difference is within the user's tolerance. Kept
// here, beside the analyses, rather than in the app, so they are tested and the same everywhere.

namespace TrajectoryLogTools.Analysis
{
    public static class ComparisonRules
    {
        /// <summary>Largest planned (expected) leaf difference, mm, below which two logs count as the same plan.</summary>
        public const double SamePlanMlcMm = 0.01;
        /// <summary>Largest planned (expected) gantry difference, degrees, below which two logs count as the same plan.</summary>
        public const double SamePlanGantryDeg = 0.05;

        /// <summary>
        /// True when two deliveries are the same plan: their planned (expected) leaf and gantry
        /// positions differ by less than <see cref="SamePlanMlcMm"/> and <see cref="SamePlanGantryDeg"/>.
        /// A value that wasn't compared (null, e.g. no gantry axis) doesn't count against it; one that
        /// is not a number (NaN) does.
        /// </summary>
        public static bool IsSamePlan(double? expectedMlcMaxMm, double? expectedGantryMaxDeg)
        {
            return (expectedMlcMaxMm == null || expectedMlcMaxMm.Value < SamePlanMlcMm) &&
                   (expectedGantryMaxDeg == null || expectedGantryMaxDeg.Value < SamePlanGantryDeg);
        }

        /// <summary>
        /// True when <paramref name="max"/> is within <paramref name="tolerance"/> (equal counts as within),
        /// false when over, null when no tolerance is set. A difference that is not a number (NaN: nothing
        /// to measure it on) is never within: it can't be shown to be.
        /// </summary>
        public static bool? WithinTolerance(double max, double? tolerance)
        {
            if (!tolerance.HasValue) return null;
            return max <= tolerance.Value;   // false for NaN
        }

        /// <summary>A tolerance as typed in Settings: a number greater than 0, or null (none, or not valid).</summary>
        public static double? ParseTolerance(string text)
        {
            double v;
            return double.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0
                ? v : (double?)null;
        }
    }
}
