using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SampleELT.Controls
{
    public partial class StepNodeControl : UserControl
    {
        // ==================== IsSelected ====================

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register(
                nameof(IsSelected), typeof(bool), typeof(StepNodeControl),
                new PropertyMetadata(false, OnIsSelectedChanged));

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StepNodeControl ctrl)
                ctrl.UpdateSelectionVisual((bool)e.NewValue);
        }

        private void UpdateSelectionVisual(bool isSelected)
        {
            if (RootBorder == null) return;
            RootBorder.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3))
                : new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
            RootBorder.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1.5);
        }

        // ==================== IsConnectionTarget ====================

        public static readonly DependencyProperty IsConnectionTargetProperty =
            DependencyProperty.Register(
                nameof(IsConnectionTarget), typeof(bool), typeof(StepNodeControl),
                new PropertyMetadata(false, OnIsConnectionTargetChanged));

        public bool IsConnectionTarget
        {
            get => (bool)GetValue(IsConnectionTargetProperty);
            set => SetValue(IsConnectionTargetProperty, value);
        }

        private static void OnIsConnectionTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StepNodeControl ctrl)
            {
                var show = (bool)e.NewValue;
                ctrl.ConnectionTargetOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                ctrl.InputPort.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ==================== IsConnecting (global mode hint) ====================

        public static readonly DependencyProperty IsConnectingModeProperty =
            DependencyProperty.Register(
                nameof(IsConnectingMode), typeof(bool), typeof(StepNodeControl),
                new PropertyMetadata(false, OnIsConnectingModeChanged));

        public bool IsConnectingMode
        {
            get => (bool)GetValue(IsConnectingModeProperty);
            set => SetValue(IsConnectingModeProperty, value);
        }

        private static void OnIsConnectingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is StepNodeControl ctrl)
            {
                // 接続モード中は全ステップの入力ポートを薄く表示
                if ((bool)e.NewValue && ctrl.InputPort.Visibility == Visibility.Collapsed)
                    ctrl.InputPort.Visibility = Visibility.Visible;
                else if (!(bool)e.NewValue && !ctrl.IsConnectionTarget)
                    ctrl.InputPort.Visibility = Visibility.Collapsed;
            }
        }

        // ==================== ResizeDragStarted (カスタムイベント) ====================

        public static readonly RoutedEvent ResizeDragStartedEvent =
            EventManager.RegisterRoutedEvent(
                "ResizeDragStarted", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(StepNodeControl));

        public event RoutedEventHandler ResizeDragStarted
        {
            add => AddHandler(ResizeDragStartedEvent, value);
            remove => RemoveHandler(ResizeDragStartedEvent, value);
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(ResizeDragStartedEvent));
            e.Handled = true;
        }

        // ==================== OutputPortDragStarted (カスタムイベント) ====================

        public static readonly RoutedEvent OutputPortDragStartedEvent =
            EventManager.RegisterRoutedEvent(
                "OutputPortDragStarted", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(StepNodeControl));

        public event RoutedEventHandler OutputPortDragStarted
        {
            add => AddHandler(OutputPortDragStartedEvent, value);
            remove => RemoveHandler(OutputPortDragStartedEvent, value);
        }

        // ==================== コンストラクタ ====================

        public StepNodeControl()
        {
            InitializeComponent();
        }

        // ==================== 出力ポートのマウスイベント ====================

        private void OutputPort_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // ドラッグ開始イベントを親へ通知 (ルーティング: Bubble)
            RaiseEvent(new RoutedEventArgs(OutputPortDragStartedEvent));
            e.Handled = true; // StepNodeのドラッグ開始を抑制
        }

        private void OutputPort_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            OutputPort.Width = 21;
            OutputPort.Height = 21;
            OutputPort.Background = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        }

        private void OutputPort_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            OutputPort.Width = 18;
            OutputPort.Height = 18;
            OutputPort.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
        }
    }
}
