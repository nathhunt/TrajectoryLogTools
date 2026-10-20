// FileCompareViewModel.cs - the File comparison window: two .bin logs compared value by value
// (header, subbeams, every expected and actual value of every axis), summarised answer first: the
// verdict, cards, what differs in the header and subbeams, a table of the axes, and the values
// that differ on the axis picked. The text the command line prints is one tab away.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    public sealed class AxisCompareRow
    {
        public AxisDifference Source { get; set; }
        public string Axis { get; set; }
        public string State { get; set; }
        /// <summary>"ok", "critical" or "caution" for the status chip.</summary>
        public string StateKind { get; set; }
        public string Samples { get; set; }
        public string ValuesDiffer { get; set; }
        /// <summary>"354 / 3,746": how many of the values that differ are expected / actual.</summary>
        public string ExpectedActual { get; set; }
        /// <summary>Values that are "no value" in one log only.</summary>
        public string NoValue { get; set; }
        public string Snapshots { get; set; }
        public string MaxDifference { get; set; }
        public string First { get; set; }
    }

    public partial class FileCompareViewModel : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public LogItem A { get; }
        public LogItem B { get; }

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _ignoreVersion;
        [ObservableProperty] private bool _onlyDiffering = true;
        [ObservableProperty] private string _verdict = "";
        /// <summary>"ok" or "caution".</summary>
        [ObservableProperty] private string _verdictKind = "ok";
        [ObservableProperty] private string _rawText = "";
        [ObservableProperty] private string _error = "";
        [ObservableProperty] private AxisCompareRow _selectedAxis;

        public ObservableCollection<Fact> Cards { get; } = new ObservableCollection<Fact>();
        public ObservableCollection<FieldDifference> Fields { get; } = new ObservableCollection<FieldDifference>();
        public ObservableCollection<AxisCompareRow> Axes { get; } = new ObservableCollection<AxisCompareRow>();
        public ObservableCollection<ValueDifference> Examples { get; } = new ObservableCollection<ValueDifference>();
        public ObservableCollection<string> Notes { get; } = new ObservableCollection<string>();

        public string NameA { get { return Describe(A); } }
        public string NameB { get { return Describe(B); } }
        public bool HasFields { get { return Fields.Count > 0; } }
        public bool HasNotes { get { return Notes.Count > 0; } }
        public string NotesText { get { return string.Join("  ·  ", Notes); } }
        public string FieldsTab { get { return "Header and subbeams" + (Fields.Count > 0 ? " (" + Fields.Count + " differ)" : " (same)"); } }
        public string AxesTab
        {
            get
            {
                int n = _allAxes.Count(x => x.Source.State != "same");
                return "Axes" + (_allAxes.Count == 0 ? "" : n == 0 ? " (same)" : " (" + n + " of " + _allAxes.Count + " differ)");
            }
        }
        public string ExamplesTitle
        {
            get
            {
                AxisDifference d = SelectedAxis?.Source;
                if (d == null) return "Pick an axis above to see its values that differ.";
                if (d.ValuesDiffer == 0) return d.Axis + ": no values differ.";
                return d.Axis + ": " + (d.ValuesDiffer > d.Examples.Count ? "the first " + d.Examples.Count + " of " + d.ValuesDiffer.ToString("N0", Inv) : "all " + d.ValuesDiffer.ToString("N0", Inv)) +
                       " values that differ";
            }
        }

        List<AxisCompareRow> _allAxes = new List<AxisCompareRow>();

        public FileCompareViewModel(LogItem a, LogItem b, bool ignoreVersion)
        {
            A = a;
            B = b;
            _ignoreVersion = ignoreVersion;
            Run();
        }

        static string Describe(LogItem l)
        {
            return (l.PatientId != null ? l.PatientId + " · " : "") + l.DisplayName + " · " + l.DateText + " · " + l.Version + " · " + System.IO.Path.GetFileName(l.Path);
        }

        partial void OnIgnoreVersionChanged(bool value) { Run(); }
        partial void OnOnlyDifferingChanged(bool value) { FillAxes(); }

        partial void OnSelectedAxisChanged(AxisCompareRow value)
        {
            Examples.Clear();
            if (value != null) foreach (ValueDifference v in value.Source.Examples) Examples.Add(v);
            OnPropertyChanged(nameof(ExamplesTitle));
        }

        async void Run()
        {
            IsBusy = true;
            Error = "";
            try
            {
                var opt = new Options { Command = Command.Compare, IgnoreVersion = IgnoreVersion, IgnoreCrc = true, MaxDiffs = 50 };
                string pa = A.Path, pb = B.Path;
                Commands.FileComparison c = await Task.Run(() => Commands.CompareFiles(pa, pb, opt));
                Show(c);
            }
            catch (Exception ex) { Error = "Could not compare the files: " + ex.Message; }
            finally { IsBusy = false; }
        }

        void Show(Commands.FileComparison c)
        {
            CompareResult r = c.Result;
            RawText = string.Join(Environment.NewLine, c.Lines);
            Cards.Clear(); Fields.Clear(); Notes.Clear();

            if (r.ByteIdentical) { Verdict = "The files are identical, byte for byte."; VerdictKind = "ok"; }
            else if (r.Same) { Verdict = "Same content: every header field, subbeam and stored value is equal" + (IgnoreVersion ? ", apart from what differs between v4.0 and v5.1 by design." : "."); VerdictKind = "ok"; }
            else
            {
                Verdict = r.DifferenceCount.ToString("N0", Inv) + (r.DifferenceCount == 1 ? " difference" : " differences") + " found.";
                bool otherPatient = A.PatientId != null && B.PatientId != null && !string.Equals(A.PatientId, B.PatientId, StringComparison.OrdinalIgnoreCase);
                if (otherPatient) Verdict += " A and B are different patients (" + A.PatientId + ", " + B.PatientId + ").";
                else if (!string.Equals(A.DisplayName, B.DisplayName, StringComparison.OrdinalIgnoreCase)) Verdict += " A and B are different plans (" + A.DisplayName + ", " + B.DisplayName + ").";
                VerdictKind = "caution";
            }

            int header = r.Fields.Count(f => f.Section == "Header"), meta = r.Fields.Count(f => f.Section == "Metadata");
            int subbeam = r.Fields.Count - header - meta;
            var differing = r.Axes.Where(x => x.State != "same").ToList();
            long values = r.Axes.Sum(x => x.ValuesDiffer), noValue = r.Axes.Sum(x => x.NoValueDiffer);
            Func<int, string, string> count = (n, what) => n == 0 ? "same" : n + " " + what + (n == 1 ? "" : "s") + " differ";
            Cards.Add(new Fact { Label = "Header", Value = count(header, "field"), Kind = header == 0 ? "ok" : "caution",
                                 Detail = c.A.Version == c.B.Version ? "v" + c.A.Version + " both" : "v" + c.A.Version + " vs v" + c.B.Version });
            Cards.Add(new Fact { Label = "Metadata", Value = count(meta, "line"), Kind = meta == 0 ? "ok" : "caution", Detail = "patient, plan, MU…" });
            Cards.Add(new Fact { Label = "Subbeams", Value = count(subbeam, "field"), Kind = subbeam == 0 ? "ok" : "caution",
                                 Detail = c.A.Subbeams.Count + " in A, " + c.B.Subbeams.Count + " in B" });
            Cards.Add(new Fact { Label = "Axes", Value = r.ByteIdentical || differing.Count == 0 ? "same" : differing.Count + " of " + r.Axes.Count + " differ",
                                 Kind = differing.Count == 0 ? "ok" : "caution", Detail = r.SnapshotsCompared.ToString("N0", Inv) + " snapshots compared" });
            double max = r.Axes.Count == 0 ? 0 : r.Axes.Max(x => x.MaxDifference);
            Cards.Add(new Fact { Label = "Stored values", Value = values == 0 ? "all equal" : values.ToString("N0", Inv) + " differ", Kind = values == 0 ? "ok" : "caution",
                                 Detail = values == 0 ? "expected and actual, every sample"
                                          : (values > noValue ? "largest |A − B| " + max.ToString("G6", Inv) : "") +
                                            (noValue > 0 ? (values > noValue ? " · " : "") + noValue.ToString("N0", Inv) + " 'no value' in one log" : "") });

            foreach (FieldDifference f in r.Fields) Fields.Add(f);
            foreach (string i in r.Info.Where(x => !x.StartsWith("value differences listed", StringComparison.Ordinal)))   // that one is about the text
                Notes.Add(char.ToUpper(i[0], Inv) + i.Substring(1));
            if (c.A.CrcWarning != null) Notes.Add("A: " + c.A.CrcWarning);
            if (c.B.CrcWarning != null) Notes.Add("B: " + c.B.CrcWarning);
            _allAxes = r.Axes.Select(Row).ToList();
            foreach (string n in new[] { nameof(HasFields), nameof(HasNotes), nameof(NotesText), nameof(FieldsTab), nameof(AxesTab) }) OnPropertyChanged(n);
            FillAxes();
            SelectedAxis = Axes.FirstOrDefault(x => x.Source.ValuesDiffer > 0);
        }

        void FillAxes()
        {
            Axes.Clear();
            foreach (AxisCompareRow x in _allAxes.Where(x => !OnlyDiffering || x.Source.State != "same")) Axes.Add(x);
        }

        static AxisCompareRow Row(AxisDifference d)
        {
            bool diff = d.ValuesDiffer > 0;
            return new AxisCompareRow
            {
                Source = d,
                Axis = d.Axis,
                State = d.State == "same" ? "Same" : d.State == "differs" ? "Differs" : char.ToUpper(d.State[0], Inv) + d.State.Substring(1),
                StateKind = d.State == "same" ? "ok" : d.State == "differs" ? "caution" : "critical",
                Samples = d.Samples.ToString(Inv),
                ValuesDiffer = diff ? d.ValuesDiffer.ToString("N0", Inv) : "",
                ExpectedActual = diff ? d.ExpectedDiffer.ToString("N0", Inv) + " / " + d.ActualDiffer.ToString("N0", Inv) : "",
                NoValue = d.NoValueDiffer > 0 ? d.NoValueDiffer.ToString("N0", Inv) : "",
                Snapshots = diff ? d.SnapshotsDiffer.ToString("N0", Inv) : "",
                MaxDifference = diff && d.NoValueDiffer < d.ValuesDiffer ? d.MaxDifference.ToString("G6", Inv) : "",
                First = diff ? d.FirstSnapshot.ToString(Inv) : "",
            };
        }

        [RelayCommand]
        void CopyText()
        {
            try { System.Windows.Clipboard.SetText(RawText); }
            catch (Exception ex) { Dialogs.Warn("Could not copy: " + ex.Message); }
        }
    }
}
