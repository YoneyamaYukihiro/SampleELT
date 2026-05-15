using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using BreezeFlow.Controls;
using BreezeFlow.Dialogs;
using BreezeFlow.Models;
using BreezeFlow.Services;
using BreezeFlow.Steps;
using BreezeFlow.Tools;
using BreezeFlow.ViewModels;

namespace BreezeFlow
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm = null!;
        private readonly StepDialogService _dialogService = new();

        // Drag state for step nodes
        private bool _isDragging;
        private StepNodeViewModel? _draggingStep;
        private Point _dragStartMousePos;
        private double _dragStartNodeX;
        private double _dragStartNodeY;

        // Connection drawing state (port drag)
        private bool _isConnecting;
        private StepNodeViewModel? _connectionSourceStep;
        private StepNodeViewModel? _connectionTargetStep;

        // Resize state
        private bool _isResizing;
        private StepNodeViewModel? _resizingStep;
        private Point _resizeStartMousePos;
        private double _resizeStartWidth;
        private double _resizeStartHeight;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            _vm.LogMessages.CollectionChanged += LogMessages_CollectionChanged;
            _vm.OpenSettingsRequested += OpenStepSettingsDialog;
            _vm.OpenScheduleManagerRequested += OpenScheduleManagerDialog;
            _vm.OpenJobManagerRequested += OpenJobManagerDialog;
            _vm.ScheduleStatusChanged += () => Dispatcher.InvokeAsync(RefreshSchedulePanel);

            RefreshSchedulePanel();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_vm.TryConfirmClose())
                e.Cancel = true;
            base.OnClosing(e);
        }

        private void LogMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Auto-scroll log to bottom
            Dispatcher.InvokeAsync(() =>
            {
                LogScrollViewer.ScrollToEnd();
            });
        }

        // ==================== STEP NODE MOUSE EVENTS ====================

        private void StepNode_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            // Select this step
            if (_vm.SelectedStep != null && _vm.SelectedStep != stepVm)
                _vm.SelectedStep.IsSelected = false;

            _vm.SelectedStep = stepVm;
            stepVm.IsSelected = true;

            if (e.ClickCount == 2)
            {
                // ダブルクリック: 設定を開く
                _vm.OpenStepSettingsCommand.Execute(stepVm);
                e.Handled = true;
                return;
            }

            // 通常ドラッグ
            _isDragging = true;
            _draggingStep = stepVm;
            _dragStartMousePos = e.GetPosition(BackgroundCanvas);
            _dragStartNodeX = stepVm.X;
            _dragStartNodeY = stepVm.Y;

            fe.CaptureMouse();
            e.Handled = true;
        }

        private void StepNode_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            if (_isResizing && _resizingStep == stepVm && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(BackgroundCanvas);
                var deltaX = currentPos.X - _resizeStartMousePos.X;
                var deltaY = currentPos.Y - _resizeStartMousePos.Y;

                stepVm.NodeWidth = Math.Max(120, _resizeStartWidth + deltaX);
                stepVm.NodeHeight = Math.Max(50, _resizeStartHeight + deltaY);
                e.Handled = true;
            }
            else if (_isDragging && _draggingStep == stepVm && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(BackgroundCanvas);
                var deltaX = currentPos.X - _dragStartMousePos.X;
                var deltaY = currentPos.Y - _dragStartMousePos.Y;

                var newX = Math.Max(0, _dragStartNodeX + deltaX);
                var newY = Math.Max(0, _dragStartNodeY + deltaY);

                stepVm.X = newX;
                stepVm.Y = newY;
                e.Handled = true;
            }
            else if (_isConnecting && _connectionSourceStep != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(BackgroundCanvas);
                TempConnectionLine.X2 = currentPos.X;
                TempConnectionLine.Y2 = currentPos.Y;
                UpdateConnectionTargetHighlight(currentPos);
                e.Handled = true;
            }
        }

        private void StepNode_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            if (_isResizing && _resizingStep == stepVm)
            {
                _isResizing = false;
                _resizingStep = null;
                fe.ReleaseMouseCapture();
                if (stepVm.NodeWidth != _resizeStartWidth || stepVm.NodeHeight != _resizeStartHeight)
                    _vm.MarkModified();
                e.Handled = true;
                return;
            }

            if (_isConnecting && _connectionSourceStep != null)
            {
                var mousePos = e.GetPosition(BackgroundCanvas);
                var targetStep = FindStepAtPosition(mousePos, _connectionSourceStep);

                if (targetStep != null)
                {
                    _vm.ConnectStepsCommand.Execute(
                        new Tuple<Guid, Guid>(_connectionSourceStep.Step.Id, targetStep.Step.Id));
                }

                ClearConnectionMode();
                fe.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;
                _draggingStep = null;
                fe.ReleaseMouseCapture();
                if (stepVm.X != _dragStartNodeX || stepVm.Y != _dragStartNodeY)
                    _vm.MarkModified();
                e.Handled = true;
            }
        }

        private void StepNode_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            // Select the step
            if (_vm.SelectedStep != null && _vm.SelectedStep != stepVm)
                _vm.SelectedStep.IsSelected = false;

            _vm.SelectedStep = stepVm;
            stepVm.IsSelected = true;
        }

        private void StepNode_ResizeDragStarted(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            _isResizing = true;
            _isDragging = false;
            _resizingStep = stepVm;
            _resizeStartMousePos = Mouse.GetPosition(BackgroundCanvas);
            _resizeStartWidth = stepVm.NodeWidth;
            _resizeStartHeight = stepVm.NodeHeight;

            fe.CaptureMouse();
            e.Handled = true;
        }

        // ==================== CANVAS MOUSE EVENTS ====================

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Click on empty canvas = deselect
            if (_vm.SelectedStep != null)
            {
                _vm.SelectedStep.IsSelected = false;
                _vm.SelectedStep = null;
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isConnecting) ClearConnectionMode();
            if (_isDragging) { _isDragging = false; _draggingStep = null; }
            if (_isResizing) { _isResizing = false; _resizingStep = null; }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isConnecting && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(BackgroundCanvas);
                TempConnectionLine.X2 = pos.X;
                TempConnectionLine.Y2 = pos.Y;
            }
        }

        // ==================== CONTEXT MENU HANDLERS ====================

        private void StepSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is StepNodeViewModel stepVm)
            {
                OpenStepSettingsDialog(stepVm);
            }
        }

        private void StepDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is StepNodeViewModel stepVm)
            {
                _vm.DeleteStep(stepVm);
            }
        }

        // ==================== SETTINGS DIALOG ====================

        private void OpenStepSettingsDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;

            // ダイアログ前の状態をスナップショット（キャンセル時に dirty にしないため）
            var snapshotName = step.Name;
            var snapshotSettings = step.Settings.ToDictionary(
                kv => kv.Key,
                kv => kv.Value?.ToString());

            _dialogService.ShowSettingsDialog(this, stepVm, _vm.CurrentPipeline);

            // 実際に値が変わった時だけ dirty にする
            bool changed = step.Name != snapshotName
                || step.Settings.Count != snapshotSettings.Count
                || step.Settings.Any(kv =>
                       !snapshotSettings.TryGetValue(kv.Key, out var prev)
                       || prev != kv.Value?.ToString());
            if (changed)
                _vm.MarkModified();
        }

        // ==================== HELPER METHODS ====================

        private StepNodeViewModel? FindStepAtPosition(Point canvasPos, StepNodeViewModel? excludeStep)
        {
            foreach (var stepVm in _vm.Steps)
            {
                if (stepVm == excludeStep) continue;
                if (canvasPos.X >= stepVm.X && canvasPos.X <= stepVm.X + stepVm.NodeWidth &&
                    canvasPos.Y >= stepVm.Y && canvasPos.Y <= stepVm.Y + stepVm.NodeHeight)
                    return stepVm;
            }
            return null;
        }

        // ==================== 接続モード ヘルパー ====================

        private void StepNode_OutputPortDragStarted(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            _isConnecting = true;
            _connectionSourceStep = stepVm;

            // 接続線の始点 = ステップ右端の中央
            var startX = stepVm.X + stepVm.NodeWidth;
            var startY = stepVm.Y + stepVm.NodeHeight / 2;

            TempConnectionLine.X1 = startX;
            TempConnectionLine.Y1 = startY;
            TempConnectionLine.X2 = startX;
            TempConnectionLine.Y2 = startY;
            TempConnectionLine.Visibility = Visibility.Visible;

            // 全ステップに「接続モード」を通知 → 入力ポート（左の丸）を表示
            foreach (var s in _vm.Steps)
                s.IsConnectingMode = true;

            fe.CaptureMouse();
            e.Handled = true;
        }

        private void UpdateConnectionTargetHighlight(Point canvasPos)
        {
            var newTarget = FindStepAtPosition(canvasPos, _connectionSourceStep);
            if (newTarget == _connectionTargetStep) return;

            if (_connectionTargetStep != null)
                _connectionTargetStep.IsConnectionTarget = false;

            _connectionTargetStep = newTarget;

            if (_connectionTargetStep != null)
                _connectionTargetStep.IsConnectionTarget = true;
        }

        private void ClearConnectionMode()
        {
            _isConnecting = false;
            _connectionSourceStep = null;
            TempConnectionLine.Visibility = Visibility.Collapsed;

            if (_connectionTargetStep != null)
            {
                _connectionTargetStep.IsConnectionTarget = false;
                _connectionTargetStep = null;
            }

            foreach (var s in _vm.Steps)
            {
                s.IsConnectingMode = false;
                s.IsConnectionTarget = false;
            }
        }

        // ==================== SCHEDULE STATUS PANEL ====================

        private void RefreshSchedulePanel()
        {
            // XAML の IsChecked="True" 初期化で Checked イベントが
            // InitializeComponent 途中に発火する場合、ScheduleStatusPanel がまだ未構築
            if (ScheduleStatusPanel == null) return;

            ScheduleStatusPanel.Children.Clear();
            var showEnabledOnly = ShowEnabledOnlyCheckBox?.IsChecked == true;
            var schedules = ScheduleRegistry.Instance.Schedules
                .Where(s => !showEnabledOnly || s.IsEnabled)
                .ToList();

            if (schedules.Count == 0)
            {
                ScheduleStatusPanel.Children.Add(new TextBlock
                {
                    Text = showEnabledOnly
                        ? "有効なスケジュールがありません\n「有効のみ」を外すと全件表示します"
                        : "スケジュールがありません\n「スケジュール管理」から追加してください",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 8, 4, 0)
                });
                return;
            }

            foreach (var entry in schedules)
                ScheduleStatusPanel.Children.Add(BuildScheduleCard(entry));
        }

        private UIElement BuildScheduleCard(ScheduleEntry entry)
        {
            // スケジュール種別の説明
            var typeDesc = entry.Type switch
            {
                ScheduleType.Daily    => $"毎日 {entry.TimeHour:D2}:{entry.TimeMinute:D2}",
                ScheduleType.Weekly   => $"毎週{GetWeekDayName(entry.WeekDay)} {entry.TimeHour:D2}:{entry.TimeMinute:D2}",
                ScheduleType.Hourly   => $"毎時 {entry.HourlyMinute:D2}分",
                ScheduleType.Interval => $"{entry.IntervalMinutes}分ごと",
                _                     => ""
            };

            // 次回実行時刻（有効時のみ）
            string nextStr = "";
            if (entry.IsEnabled)
            {
                var next = ScheduleRegistry.Instance.CalcNextRunTime(entry);
                if (next.HasValue)
                    nextStr = $"次回: {next.Value:MM/dd HH:mm}";
            }

            // 前回実行情報
            string lastRunStr;
            Color lastRunColor;
            if (entry.LastRunTime.HasValue)
            {
                var icon = entry.LastRunSuccess == true ? "✓" : "✗";
                lastRunStr  = $"{icon} {entry.LastRunTime.Value:MM/dd HH:mm}";
                lastRunColor = entry.LastRunSuccess == true
                    ? Color.FromRgb(0x4C, 0xAF, 0x50)
                    : Color.FromRgb(0xF4, 0x43, 0x36);
            }
            else
            {
                lastRunStr  = "未実行";
                lastRunColor = Color.FromRgb(0x9E, 0x9E, 0x9E);
            }

            var fg = new SolidColorBrush(
                entry.IsEnabled ? Color.FromRgb(0x21, 0x21, 0x21) : Color.FromRgb(0x75, 0x75, 0x75));

            var content = new StackPanel();

            // 名前行（ドット + 名前 | トグルボタン）
            var nameRow = new DockPanel { LastChildFill = true };

            // トグルボタン（右端）
            var toggleBtn = CreateToggleButton(entry.IsEnabled, (_, _) =>
            {
                entry.IsEnabled = !entry.IsEnabled;
                ScheduleRegistry.Instance.Save();
                RefreshSchedulePanel();
            });
            DockPanel.SetDock(toggleBtn, Dock.Right);
            nameRow.Children.Add(toggleBtn);

            // 左側：ドット + 名前 [+ (無効) ラベル]
            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            leftStack.Children.Add(new TextBlock
            {
                Text = entry.Target == ScheduleTarget.Job ? "📦" : "📄",
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = entry.Target == ScheduleTarget.Job ? "ジョブ" : "パイプライン"
            });
            leftStack.Children.Add(new TextBlock
            {
                Text = entry.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = fg,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            nameRow.Children.Add(leftStack);
            content.Children.Add(nameRow);

            // 種別
            content.Children.Add(new TextBlock
            {
                Text = typeDesc,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)),
                Margin = new Thickness(14, 2, 0, 0)
            });

            // 次回
            if (!string.IsNullOrEmpty(nextStr))
                content.Children.Add(new TextBlock
                {
                    Text = nextStr,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)),
                    Margin = new Thickness(14, 1, 0, 0)
                });

            // 前回
            content.Children.Add(new TextBlock
            {
                Text = lastRunStr,
                FontSize = 10,
                Foreground = new SolidColorBrush(lastRunColor),
                Margin = new Thickness(14, 1, 0, 0)
            });

            // エラーメッセージ（失敗時）
            if (entry.LastRunSuccess == false && !string.IsNullOrEmpty(entry.LastRunMessage))
                content.Children.Add(new TextBlock
                {
                    Text = entry.LastRunMessage,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(14, 1, 0, 0)
                });

            return new Border
            {
                Background = new SolidColorBrush(
                    entry.IsEnabled ? Colors.White : Color.FromRgb(0xF5, 0xF5, 0xF5)),
                BorderBrush = new SolidColorBrush(
                    entry.IsEnabled ? Color.FromRgb(0xDD, 0xDD, 0xDD) : Color.FromRgb(0xE8, 0xE8, 0xE8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 6, 8, 6),
                Child = content
            };
        }

        private static Button CreateToggleButton(bool isEnabled, RoutedEventHandler onClick)
        {
            var bg = new SolidColorBrush(
                isEnabled ? Color.FromRgb(0x43, 0xA0, 0x47)   // green
                          : Color.FromRgb(0x9E, 0x9E, 0x9E)); // gray

            // CornerRadius を持つ pill 型テンプレート
            var borderFef = new FrameworkElementFactory(typeof(Border));
            borderFef.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            borderFef.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            borderFef.SetValue(Border.PaddingProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            var cpFef = new FrameworkElementFactory(typeof(ContentPresenter));
            cpFef.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpFef.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFef.AppendChild(cpFef);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = borderFef };

            var btn = new Button
            {
                Content         = isEnabled ? "有効" : "無効",
                Background      = bg,
                Foreground      = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding         = new Thickness(8, 2, 8, 2),
                FontSize        = 10,
                FontWeight      = FontWeights.SemiBold,
                Cursor          = Cursors.Hand,
                Template        = template,
                VerticalAlignment = VerticalAlignment.Center,
                Margin          = new Thickness(6, 0, 0, 0)
            };
            btn.Click += onClick;
            return btn;
        }

        private static string GetWeekDayName(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday    => "月曜",
            DayOfWeek.Tuesday   => "火曜",
            DayOfWeek.Wednesday => "水曜",
            DayOfWeek.Thursday  => "木曜",
            DayOfWeek.Friday    => "金曜",
            DayOfWeek.Saturday  => "土曜",
            DayOfWeek.Sunday    => "日曜",
            _                   => ""
        };

        // ==================== PALETTE DOUBLE CLICK ====================

        private void PaletteButton_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is StepType stepType)
            {
                _vm.AddStepCommand.Execute(stepType);
                e.Handled = true;
            }
        }

        // ==================== MENU / TOOLBAR HANDLERS ====================

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && _isConnecting)
            {
                ClearConnectionMode();
                // マウスキャプチャを解放
                Mouse.Captured?.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void ConnectionManager_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ConnectionManagerDialog(initialSelectionId: null, referencePipeline: _vm.CurrentPipeline)
            {
                Owner = this
            };
            dialog.ShowDialog();
            // 接続名の変更を全ノードの「接続設定名」ラベルへ即時反映
            _vm.RefreshAllConnectionLabels();
        }

        private void ScheduleManager_Click(object sender, RoutedEventArgs e)
        {
            OpenScheduleManagerDialog();
        }

        private void OpenScheduleManagerDialog()
        {
            var dialog = new ScheduleManagerDialog { Owner = this };
            dialog.ShowDialog();
            RefreshSchedulePanel(); // ダイアログで追加・変更された内容を反映
        }

        private void OpenJobManagerDialog()
        {
            var logger = new Progress<string>(msg => _vm.AddLog(msg));
            var dialog = new JobManagerDialog(logger) { Owner = this };
            dialog.ShowDialog();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ImportKtr_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.ConfirmDiscardChanges()) return;

            var openDlg = new OpenFileDialog
            {
                Title = "KTR ファイルを取り込み",
                Filter = "Kettle Transformation (*.ktr)|*.ktr|XML files (*.xml)|*.xml|All files (*.*)|*.*"
            };
            if (openDlg.ShowDialog(this) != true) return;

            KtrConvertResult result;
            try
            {
                result = KtrToJsonConverter.Convert(openDlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"KTR の解析に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 新規接続を Connection Manager に登録（パスワードは空）
            if (result.NewConnections.Count > 0)
            {
                foreach (var c in result.NewConnections)
                    ConnectionRegistry.Instance.Connections.Add(c);
                ConnectionRegistry.Instance.Save();
            }

            // 出力先を選択
            var saveDlg = new SaveFileDialog
            {
                Title = "変換結果の保存先",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = System.IO.Path.GetFileNameWithoutExtension(openDlg.FileName) + ".json",
                InitialDirectory = System.IO.Path.GetDirectoryName(openDlg.FileName) ?? ""
            };
            if (saveDlg.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(saveDlg.FileName, result.PipelineJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"JSON の保存に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // サマリ表示
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"パイプライン名: {result.PipelineName}");
            summary.AppendLine($"出力先: {saveDlg.FileName}");
            summary.AppendLine();
            if (result.MatchedConnections.Count > 0)
            {
                summary.AppendLine("● 既存の接続を流用:");
                foreach (var c in result.MatchedConnections)
                    summary.AppendLine($"  - {c.Name} ({c.DbType})");
                summary.AppendLine();
            }
            if (result.NewConnections.Count > 0)
            {
                summary.AppendLine("● 新規接続を Connection Manager に登録 (パスワード未設定):");
                foreach (var c in result.NewConnections)
                    summary.AppendLine($"  - {c.Name} ({c.DbType})");
                summary.AppendLine();
            }
            if (result.Warnings.Count > 0)
            {
                summary.AppendLine("● 警告:");
                foreach (var w in result.Warnings)
                    summary.AppendLine($"  - {w}");
                summary.AppendLine();
            }
            summary.AppendLine("変換した JSON を今すぐ開きますか？");

            var ans = MessageBox.Show(summary.ToString(), "KTR 取り込み完了",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (ans == MessageBoxResult.Yes)
            {
                _vm.LoadPipelineFromFile(saveDlg.FileName);
            }
        }

        private void RefreshSchedule_Click(object sender, RoutedEventArgs e)
        {
            RefreshSchedulePanel();
        }

        private void DeleteConnection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is ConnectionViewModel connVm)
            {
                _vm.DeleteConnection(connVm);
            }
        }

        private void ShowEnabledOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            RefreshSchedulePanel();
        }

        private void HelpUsage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HelpDialog(0) { Owner = this };
            dialog.Show();
        }

        private void HelpSpec_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HelpDialog(1) { Owner = this };
            dialog.Show();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                $"BreezeFlow\nVersion {App.AppVersion}",
                "バージョン情報",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _vm.LogMessages.Clear();
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var text = string.Join(Environment.NewLine, _vm.LogMessages);
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }
    }
}
