// LogItem.cs - one log in the Open logs list. The list shows what the header says (plan, fields,
// date, version, folder) straight away; the whole file is read the first time the log is shown,
// compared, rebuilt or exported.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class LogItem : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public string Path { get; }
        public LogHeader Header { get; }
        public bool IsCsv { get; }

        public string DisplayName { get; }
        /// <summary>Patient ID from the header, or null (CSV exports, logs without one).</summary>
        public string PatientId { get; }
        public string FieldsText { get; }
        public DateTime Date { get; }
        /// <summary>"20/10/2026 14:23" (New Zealand format).</summary>
        public string DateText { get { return Core.Fmt.DateTimeMinutes(Date); } }
        public string Version { get; }
        public string SubtitleText { get { return string.Join(" · ", new[] { FieldsText, DateText, Version }.Where(s => !string.IsNullOrEmpty(s))); } }
        public string Folder { get { return System.IO.Path.GetDirectoryName(Path); } }
        public string FolderShort { get { return ShortenFolder(Folder); } }

        [ObservableProperty] private bool _isChecked;
        [ObservableProperty] private bool _isLoading;
        /// <summary>Short badge after loading: "Interrupted", "CRC failed", "Can't read"; "" for none.</summary>
        [ObservableProperty] private string _badge = "";
        /// <summary>"caution" or "critical".</summary>
        [ObservableProperty] private string _badgeKind = "caution";

        public DeliveryRecord Record { get; private set; }
        public LogSummary Summary { get; private set; }
        public bool CrcFailed { get; private set; }
        public string LoadError { get; private set; }

        /// <summary>The log page, kept so its tab and view survive switching logs.</summary>
        public LogPageViewModel Page { get; set; }

        Task _load;

        LogItem(string path, LogHeader header)
        {
            Path = System.IO.Path.GetFullPath(path);
            Header = header;
            IsCsv = header == null;
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (header != null)
            {
                DisplayName = header.PlanName ?? StripStamp(file);
                PatientId = header.PatientId;
                FieldsText = header.FieldsText;
                Date = header.Date;
                Version = "v" + header.Version;
            }
            else
            {
                DisplayName = StripStamp(file);
                FieldsText = "";
                LogFile f = LogFinder.Describe(path, null);
                Date = f.Date;
                Version = "CSV export";
            }
        }

        /// <summary>A .bin log (header read now) or a CSV export. Throws if the file isn't a log.</summary>
        public static LogItem Create(string path)
        {
            bool csv = string.Equals(System.IO.Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
            return new LogItem(path, csv ? null : LogHeader.Read(path));
        }

        /// <summary>Reads the whole log once. A log whose CRC doesn't verify is opened anyway and marked.</summary>
        public Task LoadAsync()
        {
            if (_load == null) _load = Load();
            return _load;
        }

        async Task Load()
        {
            IsLoading = true;
            try
            {
                string path = Path;
                var loaded = await Task.Run(() =>
                {
                    DeliveryRecord r;
                    bool crcFailed = false;
                    try { r = DeliveryRecord.Load(path); }
                    catch (InvalidDataException ex) when (ex.Message.Contains("CRC"))
                    {
                        r = DeliveryRecord.Load(path, ignoreCrc: true);
                        crcFailed = true;
                    }
                    LogSummary s = r.Has("MU_Actual") && r.Has("ControlPoint") ? DeliveryComparison.SummariseLog(r) : null;
                    return Tuple.Create(r, s, crcFailed);
                });
                Record = loaded.Item1;
                Summary = loaded.Item2;
                CrcFailed = loaded.Item3;
                if (CrcFailed) { Badge = "CRC failed"; BadgeKind = "critical"; }
                else if (Summary != null && Summary.Holds.Any(h => h.Type == "interruption")) { Badge = "Interrupted"; BadgeKind = "caution"; }
            }
            catch (Exception ex)
            {
                LoadError = ex.Message;
                Badge = "Can't read";
                BadgeKind = "critical";
            }
            finally { IsLoading = false; }
        }

        static string StripStamp(string name)
        {
            string s = Regex.Replace(name, @"_?\d{14}$", "");
            return s.Length > 0 ? s : name;
        }

        /// <summary>"L:\Community_Health\...\v5_binary_logs" -> "L:\…\Halcyon Log converter\v5_binary_logs".</summary>
        public static string ShortenFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder.Length <= 42) return folder;
            string[] parts = folder.Split('\\');
            if (parts.Length <= 3) return folder;
            string head = parts[0].Length == 0 && parts.Length > 3 ? "\\\\" + parts[2] : parts[0];
            string tail = parts[parts.Length - 1];
            string tail2 = parts[parts.Length - 2] + "\\" + tail;
            return head + "\\…\\" + (tail2.Length <= 34 ? tail2 : tail);
        }
    }
}
