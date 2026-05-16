using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BreezeFlow.Models;

namespace BreezeFlow.Controls
{
    public partial class StepNodeControl : UserControl
    {
        // ==================== IsSelected ====================
        // 選択時の枠線色は XAML 側の DataTrigger ({Binding IsSelected}) で制御する。
        // コードから RootBorder.BorderBrush を直接代入するとローカル値が固定され、
        // Production 昇格などの Style トリガが効かなくなるため、ここでは値を保持するだけ。

        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.Register(
                nameof(IsSelected), typeof(bool), typeof(StepNodeControl),
                new PropertyMetadata(false));

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
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
        //
        // 引数は OutputPortDragEventArgs を渡し、どのポート (BranchKey) からドラッグが始まったかを伝える。
        // 単一ポートステップでは BranchKey="" になる。

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
            // DataContext は ItemTemplate 由来の OutputPort インスタンス
            var branchKey = string.Empty;
            if (sender is FrameworkElement fe && fe.DataContext is OutputPort port)
                branchKey = port.Key;

            RaiseEvent(new OutputPortDragEventArgs(OutputPortDragStartedEvent, this, branchKey));
            e.Handled = true; // StepNode のドラッグ開始を抑制
        }

        private void OutputPort_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
            {
                b.Width = 21;
                b.Height = 21;
            }
        }

        private void OutputPort_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border b)
            {
                b.Width = 18;
                b.Height = 18;
            }
        }
    }
}
