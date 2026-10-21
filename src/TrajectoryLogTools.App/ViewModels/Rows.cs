// Rows.cs - table rows for the DataGrids (formatted text, as properties for data binding).

using System;
using System.Globalization;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string N(double v, int decimals) { return double.IsNaN(v) ? "" : v.ToString("F" + decimals, Inv); }
    }

    public sealed class SubbeamRow
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public int ControlPoint { get; set; }
        public string Mu { get; set; }
        public string RadiationTime { get; set; }
        public int Sequence { get; set; }

        public static SubbeamRow From(int i, Subbeam s)
        {
            return new SubbeamRow { Number = i + 1, Name = s.Name, ControlPoint = s.ControlPoint, Mu = Fmt.N(s.MU, 2),
                                    RadiationTime = Fmt.N(s.RadTime, 2), Sequence = s.Seq };
        }
    }

    public sealed class BeamRow
    {
        public string Log { get; set; }
        public int Beam { get; set; }
        public string Gantry { get; set; }
        public string Coll { get; set; }
        public string ControlPoints { get; set; }
        public string MuPlanned { get; set; }
        public string MuDelivered { get; set; }
        public string BeamOnS { get; set; }
        public string HoldS { get; set; }
        public string MlcRms { get; set; }
        public string MlcMax { get; set; }

        /// <param name="axisScale">Axis scale of the log; gantry and collimator are shown in the chosen scale (Display).</param>
        /// <param name="record">The log, for the field's own control points (FieldSpan); null = as the comparison counted them.</param>
        public static BeamRow From(BeamStat b, string log, int axisScale, TrajectoryLogTools.Analysis.DeliveryRecord record = null)
        {
            int first, last;
            string cps = record != null && TrajectoryLogTools.Analysis.FieldSpan.Of(record, b.Beam, out first, out last) ? first + "-" + last : b.ControlPoints;
            return new BeamRow
            {
                Log = log, Beam = b.Beam, Gantry = Services.Display.AngleText(b.Gantry, axisScale),
                Coll = Fmt.N(Services.Display.Angle(b.CollDeg, axisScale), 2), ControlPoints = cps,
                MuPlanned = Fmt.N(b.MuPlanned, 3), MuDelivered = Fmt.N(b.MuDelivered, 3), BeamOnS = Fmt.N(b.BeamOnS, 2),
                HoldS = Fmt.N(b.HoldS, 2), MlcRms = Fmt.N(b.MlcRmsErrMm, 3), MlcMax = Fmt.N(b.MlcMaxErrMm, 3)
            };
        }
    }

    public sealed class HoldRow
    {
        public string Log { get; set; }
        public string Type { get; set; }
        public int Beam { get; set; }
        public string Start { get; set; }
        public string Duration { get; set; }
        public string ControlPoint { get; set; }
        public string Mu { get; set; }
        public string Gantry { get; set; }
        public string Coll { get; set; }
        public string LeafErr { get; set; }
        public string LeafErrAfter { get; set; }

        /// <param name="axisScale">Axis scale of the log; gantry and collimator are shown in the chosen scale (Display).</param>
        public static HoldRow From(BeamHold h, string log, int axisScale)
        {
            return new HoldRow
            {
                Log = log, Type = h.Type, Beam = h.Beam, Start = Fmt.N(h.StartS, 2), Duration = Fmt.N(h.DurationS, 2),
                ControlPoint = Fmt.N(h.ControlPoint, 2), Mu = Fmt.N(h.Mu, 2),
                Gantry = Services.Display.AngleText(h.Gantry, axisScale), Coll = Services.Display.AngleText(h.Coll, axisScale),
                LeafErr = Fmt.N(h.LeafErrDuringMm, 3), LeafErrAfter = Fmt.N(h.LeafErr1sAfterMm, 3)
            };
        }
    }

    public sealed class AxisRow
    {
        public string Axis { get; set; }
        public string Values { get; set; }
        public string Sampling { get; set; }
        public string Mean { get; set; }
        public string Rms { get; set; }
        public string Max { get; set; }
        public string P95 { get; set; }
        public string P99 { get; set; }

        /// <summary>Gantry and collimator rows in the chosen scale: only the mean's sign can change (the rest are sizes).</summary>
        public static AxisRow From(AxisStat s, int axisScale)
        {
            bool angle = s.Axis.StartsWith("Gantry", System.StringComparison.Ordinal) || s.Axis.StartsWith("Collimator", System.StringComparison.Ordinal);
            double sign = angle ? Services.Display.DifferenceSign(axisScale) : 1.0;
            return new AxisRow { Axis = angle ? s.Axis.Replace("(deg)", "(deg, " + Services.Display.AnglesName + ")") : s.Axis,
                                 Values = s.Values, Sampling = s.Sampling, Mean = Fmt.N(sign * s.Mean, 4),
                                 Rms = Fmt.N(s.Rms, 4), Max = Fmt.N(s.Max, 4), P95 = Fmt.N(s.P95, 4), P99 = Fmt.N(s.P99, 4) };
        }
    }

    /// <summary>A field on the Rebuild page: one subbeam of one log, with its Eclipse export.</summary>
    public sealed class MlcSubbeamRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public LogItem Log { get; set; }
        public int Subbeam { get; set; }
        public string Name { get; set; }
        string _info = "";
        /// <summary>"41 control points · gantry 130.0° · 150.0 MU", gantry in the chosen scale (see Describe).</summary>
        public string Info { get { return _info; } set { SetProperty(ref _info, value); } }
        public double Mu { get; set; }

        /// <summary>Sets Info in the chosen angle scale (the field's gantry is kept in IEC).</summary>
        public void Describe()
        {
            Info = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} control points · gantry {1:F1}° · {2:F1} MU", ControlPoints,
                                 TrajectoryLogTools.Analysis.Angles.Convert(GantryIec, TrajectoryLogTools.Analysis.AngleScale.Iec, Services.Display.Angles), Mu);
        }
        /// <summary>Where the field comes from: "Own log · 10:42 · a.bin" or "Field 2 of 3 in a.bin · 10:42".</summary>
        public string Source { get; set; }
        /// <summary>A caution shown under the field ("" for none), e.g. the same field is in another log too.</summary>
        public string Note { get; set; } = "";
        public int ControlPoints { get; set; }
        /// <summary>Planned gantry at the first beam-on snapshot (IEC), for matching Eclipse exports.</summary>
        public double GantryIec { get; set; } = double.NaN;

        bool _include = true;
        public bool Include { get { return _include; } set { SetProperty(ref _include, value); } }

        string _template;
        public string Template
        {
            get { return _template; }
            set { if (SetProperty(ref _template, value)) { OnPropertyChanged(nameof(TemplateName)); } }
        }

        string _templateInfo = "";
        public string TemplateInfo { get { return _templateInfo; } set { SetProperty(ref _templateInfo, value); } }

        public string TemplateName { get { return string.IsNullOrEmpty(Template) ? "(none)" : System.IO.Path.GetFileName(Template); } }
    }

    /// <summary>
    /// The fields of one delivery on the Rebuild page: the logs of one plan on one day, whether the
    /// machine wrote one log per field or one log with several fields. Its tick ticks them all.
    /// </summary>
    public sealed class MlcFieldGroup : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string PatientId { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public System.Collections.Generic.List<MlcSubbeamRow> Fields { get; } = new System.Collections.Generic.List<MlcSubbeamRow>();

        bool _updating;

        /// <summary>True when every field is ticked, false when none, null when some.</summary>
        public bool? Include
        {
            get
            {
                int n = System.Linq.Enumerable.Count(Fields, f => f.Include);
                return n == Fields.Count ? true : n == 0 ? (bool?)false : null;
            }
            set
            {
                bool on = value != false;
                _updating = true;
                foreach (MlcSubbeamRow f in Fields) f.Include = on;
                _updating = false;
                OnPropertyChanged();
            }
        }

        public void Add(MlcSubbeamRow row)
        {
            Fields.Add(row);
            row.PropertyChanged += (s, e) => { if (!_updating && e.PropertyName == nameof(MlcSubbeamRow.Include)) OnPropertyChanged(nameof(Include)); };
        }
    }

    /// <summary>A log the Rebuild page can't use, with the reason.</summary>
    public sealed class SkippedLogRow
    {
        public string Name { get; set; }
        public string Reason { get; set; }
    }
}
