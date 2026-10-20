using System.Windows;
using System.Windows.Controls;

namespace TrajectoryLogTools.App.Views
{
    public partial class PatientTag : UserControl
    {
        public static readonly DependencyProperty IdProperty =
            DependencyProperty.Register(nameof(Id), typeof(string), typeof(PatientTag), new PropertyMetadata(null, OnIdChanged));

        public string Id { get { return (string)GetValue(IdProperty); } set { SetValue(IdProperty, value); } }

        public PatientTag()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed;
        }

        static void OnIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PatientTag)d).Visibility = string.IsNullOrEmpty(e.NewValue as string) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
