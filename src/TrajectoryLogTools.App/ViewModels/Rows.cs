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

        public static BeamRow From(BeamStat b, string log = "")
        {
            return new BeamRow
            {
                Log = log, Beam = b.Beam, Gantry = b.Gantry, Coll = Fmt.N(b.CollDeg, 2), ControlPoints = b.ControlPoints,
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

        public static HoldRow From(BeamHold h, string log = "")
        {
            return new HoldRow
            {
                Log = log, Type = h.Type, Beam = h.Beam, Start = Fmt.N(h.StartS, 2), Duration = Fmt.N(h.DurationS, 2),
                ControlPoint = Fmt.N(h.ControlPoint, 2), Mu = Fmt.N(h.Mu, 2), Gantry = h.Gantry, Coll = h.Coll,
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

        public static AxisRow From(AxisStat s)
        {
            return new AxisRow { Axis = s.Axis, Values = s.Values, Sampling = s.Sampling, Mean = Fmt.N(s.Mean, 4),
                                 Rms = Fmt.N(s.Rms, 4), Max = Fmt.N(s.Max, 4), P95 = Fmt.N(s.P95, 4), P99 = Fmt.N(s.P99, 4) };
        }
    }

    /// <summary>A subbeam of the log in the Eclipse MLC tab, with its template.</summary>
    public sealed class MlcSubbeamRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public int Subbeam { get; set; }
        public string Info { get; set; }
        public int ControlPoints { get; set; }

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
}
