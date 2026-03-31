using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SampleELT.Controls
{
    public partial class StepNodeControl : UserControl
    {
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register(
                nameof(IsSelected),
                typeof(bool),
                typeof(StepNodeControl),
                new PropertyMetadata(false, OnIsSelectedChanged));

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StepNodeControl control)
            {
                control.UpdateSelectionVisual((bool)e.NewValue);
            }
        }

        public StepNodeControl()
        {
            InitializeComponent();
        }

        private void UpdateSelectionVisual(bool isSelected)
        {
            if (RootBorder != null)
            {
                RootBorder.BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3))
                    : new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
                RootBorder.BorderThickness = isSelected
                    ? new Thickness(2)
                    : new Thickness(1.5);
            }
        }
    }
}
