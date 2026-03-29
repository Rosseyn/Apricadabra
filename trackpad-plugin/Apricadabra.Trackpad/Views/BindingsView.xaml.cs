using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apricadabra.Trackpad.ViewModels;

namespace Apricadabra.Trackpad.Views
{
    public partial class BindingsView : UserControl
    {
        private BindingsViewModel VM => DataContext as BindingsViewModel;

        public BindingsView()
        {
            InitializeComponent();

            // Add value converters
            Resources.Add("BoolToVis", new BooleanToVisibilityConverter());
            Resources.Add("InverseBoolToVis", new InverseBoolToVisConverter());
        }

        private void Edit_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is BindingRowViewModel row)
                VM?.StartEdit(row);
        }

        private void Delete_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is BindingRowViewModel row)
                VM?.DeleteBinding(row);
        }

        private void Add_Click(object sender, MouseButtonEventArgs e) => VM?.AddCommand.Execute(null);
    }

    public class InverseBoolToVisConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, System.Type targetType, object parameter,
            System.Globalization.CultureInfo culture)
            => throw new System.NotSupportedException();
    }
}
