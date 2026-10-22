// LogFilter.cs - which logs of a folder to take, by when they were treated: a period (start date
// and time to end date and time, the end minute included) and / or times of day on every day.
// Shown by LogFilterPanel; used by the main app's Add from folder and by the v4.0 converter.

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Wpf
{
    public partial class LogFilter : ObservableObject
    {
        [ObservableProperty] private bool _useDateRange;
        [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime? _toDate = DateTime.Today;
        // The period's start and end times (with FromDate / ToDate): the whole of both days by default.
        [ObservableProperty] private TimeSpan _fromTime = TimeSpan.Zero;
        [ObservableProperty] private TimeSpan _toTime = new TimeSpan(23, 59, 0);
        // Time of day, on every day (within the period when that is on too).
        [ObservableProperty] private bool _useTimeOfDay;
        [ObservableProperty] private TimeSpan _dayFromTime = new TimeSpan(8, 0, 0);
        [ObservableProperty] private TimeSpan _dayToTime = new TimeSpan(17, 0, 0);

        /// <summary>Raised when any of the choices changes.</summary>
        public event EventHandler Changed;

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            var h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        public bool IsActive { get { return UseDateRange || UseTimeOfDay; } }

        /// <summary>Sets the filter of <paramref name="s"/>; returns an error message, or null.</summary>
        public string ApplyTo(FindSettings s)
        {
            DateTime from = (FromDate ?? DateTime.Today).Date + FromTime;
            DateTime to = (ToDate ?? DateTime.Today).Date + ToTime + TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1);   // to the end of that minute
            if (UseDateRange && to < from) return "The period ends before it starts.";
            s.UseDateRange = UseDateRange;
            s.From = from;
            s.To = to;
            s.UseTimeOfDay = UseTimeOfDay;
            s.TimeFrom = DayFromTime;
            s.TimeTo = DayToTime + TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1);
            return null;
        }

        /// <summary>"20/10/2026 08:00 to 21/10/2026 17:00, 08:00–12:00 each day", or "" when off.</summary>
        public string Describe()
        {
            string p = UseDateRange ? Fmt.DateTimeMinutes((FromDate ?? DateTime.Today).Date + FromTime) + " to " + Fmt.DateTimeMinutes((ToDate ?? DateTime.Today).Date + ToTime) : "";
            string t = UseTimeOfDay ? DayFromTime.ToString(@"hh\:mm") + "–" + DayToTime.ToString(@"hh\:mm") + " each day" : "";
            return p.Length > 0 && t.Length > 0 ? p + ", " + t : p + t;
        }
    }
}
