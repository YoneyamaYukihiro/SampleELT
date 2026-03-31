using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SampleELT.Controls;
using SampleELT.Dialogs;
using SampleELT.Models;
using SampleELT.Steps;
using SampleELT.ViewModels;

namespace SampleELT
{
    public partial class MainWindow : Window
    {
        private MainViewModel _vm = null!;

        // Drag state for step nodes
        private bool _isDragging;
        private StepNodeViewModel? _draggingStep;
        private Point _dragStartMousePos;
        private double _dragStartNodeX;
        private double _dragStartNodeY;

        // Connection drawing state (Shift+drag)
        private bool _isConnecting;
        private StepNodeViewModel? _connectionSourceStep;
        private Point _connectionStartPoint;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            _vm.LogMessages.CollectionChanged += LogMessages_CollectionChanged;
            _vm.OpenSettingsRequested += OpenStepSettingsDialog;
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
                // Double-click: open settings
                _vm.OpenStepSettingsCommand.Execute(stepVm);
                e.Handled = true;
                return;
            }

            // Check Shift for connection mode
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                _isConnecting = true;
                _connectionSourceStep = stepVm;

                // Calculate center of source node
                var canvas = BackgroundCanvas;
                var nodeCenter = new Point(stepVm.X + 70, stepVm.Y + 35);
                _connectionStartPoint = nodeCenter;

                TempConnectionLine.X1 = nodeCenter.X;
                TempConnectionLine.Y1 = nodeCenter.Y;
                TempConnectionLine.X2 = nodeCenter.X;
                TempConnectionLine.Y2 = nodeCenter.Y;
                TempConnectionLine.Visibility = Visibility.Visible;

                fe.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Normal drag
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

            if (_isDragging && _draggingStep == stepVm && e.LeftButton == MouseButtonState.Pressed)
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
                e.Handled = true;
            }
        }

        private void StepNode_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not StepNodeViewModel stepVm) return;

            if (_isConnecting && _connectionSourceStep != null)
            {
                // Find target step at mouse position
                var mousePos = e.GetPosition(BackgroundCanvas);
                var targetStep = FindStepAtPosition(mousePos, _connectionSourceStep);

                if (targetStep != null)
                {
                    _vm.ConnectStepsCommand.Execute(
                        new Tuple<Guid, Guid>(_connectionSourceStep.Step.Id, targetStep.Step.Id));
                }

                // Reset connection drawing
                _isConnecting = false;
                _connectionSourceStep = null;
                TempConnectionLine.Visibility = Visibility.Collapsed;
                fe.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;
                _draggingStep = null;
                fe.ReleaseMouseCapture();
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
            if (_isConnecting)
            {
                _isConnecting = false;
                _connectionSourceStep = null;
                TempConnectionLine.Visibility = Visibility.Collapsed;
            }

            if (_isDragging)
            {
                _isDragging = false;
                _draggingStep = null;
            }
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

            switch (step.StepType)
            {
                case StepType.OracleInput:
                    OpenOracleInputDialog(stepVm);
                    break;
                case StepType.MySQLInput:
                    OpenMySQLInputDialog(stepVm);
                    break;
                case StepType.ExcelInput:
                    OpenExcelInputDialog(stepVm);
                    break;
                case StepType.OracleOutput:
                    OpenDBOutputDialog(stepVm, "Oracle");
                    break;
                case StepType.MySQLOutput:
                    OpenDBOutputDialog(stepVm, "MySQL");
                    break;
                case StepType.ExcelOutput:
                    OpenExcelOutputDialog(stepVm);
                    break;
                case StepType.Filter:
                    OpenFilterDialog(stepVm);
                    break;
                case StepType.Calculation:
                    OpenCalculationDialog(stepVm);
                    break;
            }
        }

        private void OpenOracleInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new OracleInputDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("ConnectionString", out var cs) ? cs?.ToString() ?? "" : "",
                step.Settings.TryGetValue("SQL", out var sql) ? sql?.ToString() ?? "" : "");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionString"] = dialog.ConnectionString;
                step.Settings["SQL"] = dialog.SQL;
                stepVm.NotifyNameChanged();
            }
        }

        private void OpenMySQLInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new MySQLInputDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("ConnectionString", out var cs) ? cs?.ToString() ?? "" : "",
                step.Settings.TryGetValue("SQL", out var sql) ? sql?.ToString() ?? "" : "");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionString"] = dialog.ConnectionString;
                step.Settings["SQL"] = dialog.SQL;
                stepVm.NotifyNameChanged();
            }
        }

        private void OpenExcelInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExcelInputDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                step.Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("HasHeader", out var hh) && hh is bool b ? b : true);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FilePath"] = dialog.FilePath;
                step.Settings["SheetName"] = dialog.SheetName;
                step.Settings["HasHeader"] = dialog.HasHeader;
                stepVm.NotifyNameChanged();
            }
        }

        private void OpenDBOutputDialog(StepNodeViewModel stepVm, string defaultDbType)
        {
            var step = stepVm.Step;
            var dialog = new DBOutputDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("DBType", out var dt) ? dt?.ToString() ?? defaultDbType : defaultDbType,
                step.Settings.TryGetValue("ConnectionString", out var cs) ? cs?.ToString() ?? "" : "",
                step.Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["DBType"] = dialog.DBType;
                step.Settings["ConnectionString"] = dialog.ConnectionString;
                step.Settings["TableName"] = dialog.TableName;
                step.Settings["Mode"] = dialog.Mode;
                stepVm.NotifyNameChanged();
            }
        }

        private void OpenExcelOutputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExcelOutputDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                step.Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "Sheet1" : "Sheet1",
                step.Settings.TryGetValue("IncludeHeader", out var ih) && ih is bool b ? b : true);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FilePath"] = dialog.FilePath;
                step.Settings["SheetName"] = dialog.SheetName;
                step.Settings["IncludeHeader"] = dialog.IncludeHeader;
                stepVm.NotifyNameChanged();
            }
        }

        private void OpenFilterDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new FilterDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FieldName", out var fn) ? fn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("Operator", out var op) ? op?.ToString() ?? "equals" : "equals",
                step.Settings.TryGetValue("Value", out var v) ? v?.ToString() ?? "" : "");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FieldName"] = dialog.FieldName;
                step.Settings["Operator"] = dialog.Operator;
                step.Settings["Value"] = dialog.Value;
                stepVm.NotifyNameChanged();
            }
        }

        private void OpenCalculationDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new CalculationDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("OutputFieldName", out var ofn) ? ofn?.ToString() ?? "Result" : "Result",
                step.Settings.TryGetValue("ExpressionType", out var et) ? et?.ToString() ?? "add" : "add",
                step.Settings.TryGetValue("Field1", out var f1) ? f1?.ToString() ?? "" : "",
                step.Settings.TryGetValue("Field2", out var f2) ? f2?.ToString() ?? "" : "",
                step.Settings.TryGetValue("Constant", out var c) ? c?.ToString() ?? "0" : "0");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["OutputFieldName"] = dialog.OutputFieldName;
                step.Settings["ExpressionType"] = dialog.ExpressionType;
                step.Settings["Field1"] = dialog.Field1;
                step.Settings["Field2"] = dialog.Field2;
                step.Settings["Constant"] = dialog.Constant;
                stepVm.NotifyNameChanged();
            }
        }

        // ==================== HELPER METHODS ====================

        private StepNodeViewModel? FindStepAtPosition(Point canvasPos, StepNodeViewModel? excludeStep)
        {
            const double nodeW = 140.0;
            const double nodeH = 70.0;

            foreach (var stepVm in _vm.Steps)
            {
                if (stepVm == excludeStep) continue;

                if (canvasPos.X >= stepVm.X && canvasPos.X <= stepVm.X + nodeW &&
                    canvasPos.Y >= stepVm.Y && canvasPos.Y <= stepVm.Y + nodeH)
                {
                    return stepVm;
                }
            }

            return null;
        }

        // ==================== MENU / TOOLBAR HANDLERS ====================

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "SampleELT - ETL Designer\nVersion 1.0\n\n" +
                "Pentaho Spoon スタイルの ETL ツール\n" +
                ".NET 8 / WPF",
                "バージョン情報",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _vm.LogMessages.Clear();
        }
    }
}
