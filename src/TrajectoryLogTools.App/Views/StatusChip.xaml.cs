using System.Windows;
using System.Windows.Controls;

namespace TrajectoryLogTools.App.Views
{
    public partial class StatusChip : UserControl
    {
        public static readonly DependencyProperty KindProperty =
            DependencyProperty.Register(nameof(Kind), typeof(string), typeof(StatusChip), new PropertyMetadata("neutral"));
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusChip), new PropertyMetadata(""));

        public string Kind { get { return (string)GetValue(KindProperty); } set { SetValue(KindProperty, value); } }
        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }

        public StatusChip() { InitializeComponent(); }
    }
}
