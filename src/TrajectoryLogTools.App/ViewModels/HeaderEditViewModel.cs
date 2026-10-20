// HeaderEditViewModel.cs - Header details of one log for its v4.0 export: patient ID, plan name,
// plan UID and the field (subbeam) names, each kept as in the log or changed. Only what is changed
// is passed to the converter, so everything else in the header is copied exactly.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    /// <summary>One value of the header: as in the log, and as it will be written.</summary>
    public partial class HeaderValueRow : ObservableObject
    {
        public string Label { get; set; }
        /// <summary>Metadata key the value is stored under (the log's own spelling when present); 0 for a field name.</summary>
        public string Key { get; set; }
        public int Subbeam { get; set; }
        public string Original { get; set; }
        public string OriginalText { get { return Original == null ? "(not in the log)" : Original.Length == 0 ? "(empty)" : Original; } }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsChanged))]
        private string _value;

        public bool IsChanged { get { return (Value ?? "") != (Original ?? ""); } }

        [RelayCommand]
        void Reset() { Value = Original ?? ""; }
    }

    public partial class HeaderEditViewModel : ObservableObject
    {
        readonly LogHeader _header;

        public ObservableCollection<HeaderValueRow> Details { get; } = new ObservableCollection<HeaderValueRow>();
        public ObservableCollection<HeaderValueRow> Fields { get; } = new ObservableCollection<HeaderValueRow>();
        public string OtherMetadata { get; }
        public string FileName { get { return System.IO.Path.GetFileName(_header.Path); } }

        public HeaderEditViewModel(LogHeader header)
        {
            _header = header;
            string meta = header.MetaDataText ?? "";
            Details.Add(Detail("Patient ID", MetaData.PatientIdKeys, meta));
            Details.Add(Detail("Plan name", MetaData.PlanNameKeys, meta));
            Details.Add(Detail("Plan UID", MetaData.PlanUidKeys, meta));
            for (int i = 0; i < header.Subbeams.Count; i++)
            {
                string name = header.Subbeams[i].Name ?? "";
                Fields.Add(new HeaderValueRow { Label = "Field " + (i + 1), Subbeam = i + 1, Original = name, Value = name });
            }
            var shown = new HashSet<string>(Details.Select(d => d.Key), StringComparer.OrdinalIgnoreCase);
            OtherMetadata = string.Join(Environment.NewLine, MetaData.Entries(meta).Where(e => !shown.Contains(e.Key)).Select(e => e.Key + ": " + e.Value));
        }

        static HeaderValueRow Detail(string label, string[] keys, string meta)
        {
            string key = MetaData.FindKey(meta, keys);
            string value = key == null ? null : MetaData.Get(meta, key) ?? "";
            return new HeaderValueRow { Label = label, Key = key ?? keys[0], Original = value, Value = value ?? "" };
        }

        /// <summary>A copy to edit, so Cancel leaves these values alone.</summary>
        public HeaderEditViewModel Copy()
        {
            var c = new HeaderEditViewModel(_header);
            for (int i = 0; i < Details.Count; i++) c.Details[i].Value = Details[i].Value;
            for (int i = 0; i < Fields.Count; i++) c.Fields[i].Value = Fields[i].Value;
            return c;
        }

        /// <summary>The changed metadata values, for Options.MetaEdits.</summary>
        public List<KeyValuePair<string, string>> MetaEdits()
        {
            return Details.Where(d => d.IsChanged).Select(d => new KeyValuePair<string, string>(d.Key, (d.Value ?? "").Trim())).ToList();
        }

        /// <summary>The changed field names, for Options.SubbeamNames.</summary>
        public Dictionary<int, string> SubbeamNames()
        {
            return Fields.Where(f => f.IsChanged).ToDictionary(f => f.Subbeam, f => (f.Value ?? "").Trim());
        }

        /// <summary>"Patient ID → QA001", one per change.</summary>
        public List<string> Changes()
        {
            return Details.Concat(Fields).Where(r => r.IsChanged)
                          .Select(r => r.Label + " → " + ((r.Value ?? "").Trim().Length == 0 ? "(empty)" : r.Value.Trim())).ToList();
        }

        /// <summary>Checks the values fit the header; returns an error message, or null.</summary>
        public string Validate()
        {
            foreach (HeaderValueRow r in Details.Concat(Fields))
                if ((r.Value ?? "").IndexOfAny(new[] { '\r', '\n' }) >= 0) return r.Label + " must be one line.";
            string meta = _header.MetaDataText ?? "";
            foreach (var e in MetaEdits()) meta = MetaData.Set(meta, e.Key, e.Value);
            int bytes = Encoding.UTF8.GetByteCount(meta);
            if (bytes > MetaData.MaxBytes)
                return "The patient and plan details would take " + bytes + " bytes; the header holds " + MetaData.MaxBytes + ". Shorten a value.";
            foreach (HeaderValueRow f in Fields)
                if (Encoding.UTF8.GetByteCount(f.Value ?? "") > 500) return f.Label + "'s name is too long.";
            return null;
        }

        [RelayCommand]
        void ResetAll()
        {
            foreach (HeaderValueRow r in Details.Concat(Fields)) r.ResetCommand.Execute(null);
        }
    }
}
