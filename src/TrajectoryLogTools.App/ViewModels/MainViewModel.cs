// MainViewModel.cs - the four tabs of the main window.

using CommunityToolkit.Mvvm.ComponentModel;

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ConvertViewModel Convert { get; }
        public ViewerViewModel Viewer { get; }
        public CompareViewModel Compare { get; }
        public MlcViewModel Mlc { get; }

        /// <summary>0 = Convert &amp; export, 1 = Log viewer, 2 = Compare, 3 = Eclipse MLC.</summary>
        [ObservableProperty] private int _selectedTab;

        public string Title { get; } = "Trajectory Log Tools " + typeof(MainViewModel).Assembly.GetName().Version.ToString(3);

        /// <summary>Where and on what the app is running (handy when checking a new workstation).</summary>
        public string EnvironmentText { get; } =
            System.Runtime.InteropServices.RuntimeInformation.OSDescription + "  ·  " +
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " (" +
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture + ")  ·  " + System.AppContext.BaseDirectory;

        public MainViewModel(StartupArgs args)
        {
            Convert = new ConvertViewModel(args);
            Viewer = new ViewerViewModel();
            Compare = new CompareViewModel();
            Mlc = new MlcViewModel();
            Viewer.OpenInMlc = path => { Mlc.LoadLog(path); SelectedTab = 3; };
            Viewer.OpenInCompare = path => { Compare.PathA = path; SelectedTab = 2; };

            switch (args.Tab)
            {
                case "viewer": SelectedTab = 1; break;
                case "compare": SelectedTab = 2; break;
                case "mlc": SelectedTab = 3; break;
                default: SelectedTab = 0; break;
            }
            if (args.OpenLog != null)
            {
                Viewer.Open(args.OpenLog);
                if (args.Tab == null && args.Input == null) SelectedTab = 1;
            }
        }
    }
}
