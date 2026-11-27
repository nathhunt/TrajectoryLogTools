// SystemSettingsLock.cs - the safeguard on settings that affect everyone using a copy of an app
// (they are saved beside the app): they start locked (greyed out) and are edited only after
// Unlock is pressed. Unlocking is logged, and says straight away if this user can't save them
// (the app's folder isn't writable for them), rather than at Save.

using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Wpf
{
    public partial class SystemSettingsLock : ObservableObject
    {
        readonly string _what;

        /// <summary>The settings for everyone can be edited.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Locked), nameof(ButtonText), nameof(Icon), nameof(Text))]
        private bool _unlocked;

        /// <summary>Said after unlocking when this user can't save the settings ("" otherwise).</summary>
        [ObservableProperty] private string _warning = "";

        public bool Locked { get { return !Unlocked; } }
        public string ButtonText { get { return Unlocked ? "Lock" : "Unlock to edit"; } }
        /// <summary>Segoe Fluent Icons: Lock / Unlock.</summary>
        public string Icon { get { return Unlocked ? "" : ""; } }
        public string Text
        {
            get
            {
                return Unlocked
                    ? "Unlocked: changes to these settings apply to everyone using this copy of the app."
                    : "These settings apply to everyone using this copy of the app. They are locked so they aren't changed by accident.";
            }
        }

        /// <param name="what">What the settings are, for the log, e.g. "the converter's settings".</param>
        public SystemSettingsLock(string what) { _what = what; }

        [RelayCommand]
        void Toggle()
        {
            Unlocked = !Unlocked;
            Warning = "";
            if (!Unlocked) return;
            AppLog.Info("Unlocked " + _what + " for editing (settings for everyone, saved beside the app in " + AppContext.BaseDirectory + ")");
            string test = Path.Combine(AppContext.BaseDirectory, ".tlt-settings-check-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (new FileStream(test, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            }
            catch (Exception ex)
            {
                Warning = "You can't save these here: the app's folder (" + AppContext.BaseDirectory + ") isn't writable for you (" + ex.Message +
                          "). Ask whoever looks after the app's folder.";
                AppLog.Warn("The settings for everyone can't be saved by this user: the app's folder isn't writable", ex, "App folder", AppContext.BaseDirectory);
            }
        }
    }
}
