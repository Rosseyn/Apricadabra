using System.Windows;
using System.Windows.Controls;
using Apricadabra.Trackpad.ViewModels;

namespace Apricadabra.Trackpad.Controls
{
    public partial class BindingEditor : UserControl
    {
        public BindingEditor()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => UpdateVisibility();
        }

        private void GestureType_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateDirectionOptions();
            UpdateFingerVisibility();
        }

        private void ActionType_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionFieldVisibility();
        }

        private void AxisMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Could show/hide decay rate or steps fields here
        }

        private void UpdateVisibility()
        {
            UpdateDirectionOptions();
            UpdateFingerVisibility();
            UpdateActionFieldVisibility();
        }

        private void UpdateDirectionOptions()
        {
            if (Direction == null) return;
            Direction.Items.Clear();
            var vm = DataContext as BindingRowViewModel;
            var type = vm?.EditGestureType ?? "scroll";

            switch (type)
            {
                case "scroll":
                case "swipe":
                    Direction.Items.Add("up");
                    Direction.Items.Add("down");
                    Direction.Items.Add("left");
                    Direction.Items.Add("right");
                    break;
                case "pinch":
                    Direction.Items.Add("in");
                    Direction.Items.Add("out");
                    break;
                case "rotate":
                    Direction.Items.Add("clockwise");
                    Direction.Items.Add("counterclockwise");
                    break;
                case "tap":
                    Direction.Items.Add("none");
                    break;
            }
            if (Direction.Items.Count > 0)
                Direction.SelectedIndex = 0;
        }

        private void UpdateFingerVisibility()
        {
            if (FingersLabel == null) return;
            var vm = DataContext as BindingRowViewModel;
            var type = vm?.EditGestureType ?? "scroll";
            var hidden = type == "pinch"; // pinch is always 2
            FingersLabel.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            Fingers.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
            DirectionLabel.Visibility = type == "tap" ? Visibility.Collapsed : Visibility.Visible;
            Direction.Visibility = type == "tap" ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateActionFieldVisibility()
        {
            if (AxisFields == null) return;
            var vm = DataContext as BindingRowViewModel;
            var isAxis = vm?.EditActionType == "axis";
            AxisFields.Visibility = isAxis ? Visibility.Visible : Visibility.Collapsed;
            ButtonFields.Visibility = isAxis ? Visibility.Collapsed : Visibility.Visible;
            SensitivityPanel.Visibility = isAxis ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // Walk up to find BindingsView and call save
            var bindingsView = FindParent<Views.BindingsView>(this);
            if (bindingsView?.DataContext is BindingsViewModel bvm)
                bvm.SaveEditCommand.Execute(null);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var bindingsView = FindParent<Views.BindingsView>(this);
            if (bindingsView?.DataContext is BindingsViewModel bvm)
                bvm.CancelEditCommand.Execute(null);
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typed) return typed;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
