using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
            _vm.ScheduleStatusChanged += () => Dispatcher.InvokeAsync(RefreshSchedulePanel);

            RefreshSchedulePanel();
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

            switch (step.StepType)
            {
                case StepType.DBInput:
                    OpenDBInputDialog(stepVm);
                    break;
                case StepType.OracleInput:
                    OpenOracleInputDialog(stepVm);
                    break;
                case StepType.MySQLInput:
                    OpenMySQLInputDialog(stepVm);
                    break;
                case StepType.ExcelInput:
                    OpenExcelInputDialog(stepVm);
                    break;
                case StepType.DBOutput:
                    OpenDBOutputDialog(stepVm, "");
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
                case StepType.SelectValues:
                    OpenSelectValuesDialog(stepVm);
                    break;
                case StepType.DBDelete:
                    OpenDBDeleteDialog(stepVm);
                    break;
                case StepType.InsertUpdate:
                    OpenInsertUpdateDialog(stepVm);
                    break;
                case StepType.ExecSQL:
                    OpenExecSQLDialog(stepVm);
                    break;
                case StepType.Dummy:
                    OpenDummyStepDialog(stepVm);
                    break;
                case StepType.GenerateRows:
                    OpenGenerateRowsDialog(stepVm);
                    break;
                case StepType.MergeJoin:
                    OpenMergeJoinDialog(stepVm);
                    break;
                case StepType.DBUpdate:
                    OpenDBUpdateDialog(stepVm);
                    break;
                case StepType.SetVariable:
                    OpenSetVariableDialog(stepVm);
                    break;
            }
        }

        private void OpenDBInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new DBInputDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            bool executeEachRow = step.Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("SQL", out var sql) ? sql?.ToString() ?? "" : "",
                executeEachRow);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["SQL"] = dialog.SQL;
                step.Settings["ExecuteEachRow"] = dialog.ExecuteEachRow ? "true" : "false";
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenOracleInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new OracleInputDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            bool executeEachRow = step.Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("SQL", out var sql) ? sql?.ToString() ?? "" : "",
                executeEachRow);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["SQL"] = dialog.SQL;
                step.Settings["ExecuteEachRow"] = dialog.ExecuteEachRow ? "true" : "false";
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenMySQLInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new MySQLInputDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            bool executeEachRow = step.Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("SQL", out var sql) ? sql?.ToString() ?? "" : "",
                executeEachRow);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["SQL"] = dialog.SQL;
                step.Settings["ExecuteEachRow"] = dialog.ExecuteEachRow ? "true" : "false";
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenExcelInputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExcelInputDialog { Owner = this };

            var hhRaw = step.Settings.TryGetValue("HasHeader", out var hh) ? hh?.ToString() : null;
            bool hasHeader = hhRaw == null || !bool.TryParse(hhRaw, out var hhBool) ? true : hhBool;

            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FilePath",  out var fp)  ? fp?.ToString()  ?? "" : "",
                step.Settings.TryGetValue("SheetName", out var sn)  ? sn?.ToString()  ?? "" : "",
                hasHeader,
                step.Settings.TryGetValue("Format",    out var fmt) ? fmt?.ToString() ?? "Excel" : "Excel",
                step.Settings.TryGetValue("Delimiter", out var dl)  ? dl?.ToString()  ?? "," : ",",
                step.Settings.TryGetValue("Encoding",  out var enc) ? enc?.ToString() ?? "UTF-8" : "UTF-8");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FilePath"]  = dialog.FilePath;
                step.Settings["Format"]    = dialog.Format;
                step.Settings["SheetName"] = dialog.SheetName;
                step.Settings["HasHeader"] = dialog.HasHeader.ToString();
                step.Settings["Delimiter"] = dialog.Delimiter;
                step.Settings["Encoding"]  = dialog.Encoding;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenDBOutputDialog(StepNodeViewModel stepVm, string defaultDbType)
        {
            var step = stepVm.Step;
            var dialog = new DBOutputDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            int commitSize = step.Settings.TryGetValue("CommitSize", out var cs)
                && int.TryParse(cs?.ToString(), out var csVal) ? csVal : 100;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("Mode", out var m) ? m?.ToString() ?? "INSERT" : "INSERT",
                commitSize);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"] = dialog.TableName;
                step.Settings["Mode"] = dialog.Mode;
                step.Settings["CommitSize"] = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenExcelOutputDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExcelOutputDialog { Owner = this };

            var ihRaw = step.Settings.TryGetValue("IncludeHeader", out var ih) ? ih?.ToString() : null;
            bool includeHeader = ihRaw == null || !bool.TryParse(ihRaw, out var ihBool) ? true : ihBool;

            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                step.Settings.TryGetValue("Format", out var fmt) ? fmt?.ToString() ?? "Excel" : "Excel",
                step.Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "Sheet1" : "Sheet1",
                step.Settings.TryGetValue("Delimiter", out var dl) ? dl?.ToString() ?? "," : ",",
                step.Settings.TryGetValue("Encoding", out var enc) ? enc?.ToString() ?? "UTF-8" : "UTF-8",
                includeHeader);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FilePath"] = dialog.FilePath;
                step.Settings["Format"] = dialog.Format;
                step.Settings["SheetName"] = dialog.SheetName;
                step.Settings["Delimiter"] = dialog.Delimiter;
                step.Settings["Encoding"] = dialog.Encoding;
                step.Settings["IncludeHeader"] = dialog.IncludeHeader.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenFilterDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new FilterDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FieldName",  out var fn) ? fn?.ToString()   ?? "" : "",
                step.Settings.TryGetValue("Operator",   out var op) ? op?.ToString()   ?? "equals" : "equals",
                step.Settings.TryGetValue("Value",      out var v)  ? v?.ToString()    ?? "" : "",
                step.Settings.TryGetValue("RightField", out var rf) ? rf?.ToString()   ?? "" : "");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FieldName"]  = dialog.FieldName;
                step.Settings["Operator"]   = dialog.Operator;
                step.Settings["Value"]      = dialog.Value;
                step.Settings["RightField"] = dialog.RightField;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
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
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenSelectValuesDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new SelectValuesDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("FieldMappings", out var fm) ? fm?.ToString() ?? "" : "");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FieldMappings"] = dialog.FieldMappings;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenDBDeleteDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new DBDeleteDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            int commitSizeDel = step.Settings.TryGetValue("CommitSize", out var csDel)
                && int.TryParse(csDel?.ToString(), out var csDelVal) ? csDelVal : 100;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "",
                commitSizeDel);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"] = dialog.TableName;
                step.Settings["KeyFields"] = dialog.KeyFields;
                step.Settings["CommitSize"] = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenInsertUpdateDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new InsertUpdateDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            int commitSizeIU = step.Settings.TryGetValue("CommitSize", out var csIU)
                && int.TryParse(csIU?.ToString(), out var csIUVal) ? csIUVal : 100;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "",
                step.Settings.TryGetValue("UpdateFields", out var uf) ? uf?.ToString() ?? "" : "",
                commitSizeIU);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"] = dialog.TableName;
                step.Settings["KeyFields"] = dialog.KeyFields;
                step.Settings["UpdateFields"] = dialog.UpdateFields;
                step.Settings["CommitSize"] = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenExecSQLDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExecSQLDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            bool executeEachRow = step.Settings.TryGetValue("ExecuteEachRow", out var eer)
                && eer?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("SQL", out var sql) ? sql?.ToString() ?? "" : "",
                executeEachRow);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["SQL"] = dialog.SQL;
                step.Settings["ExecuteEachRow"] = dialog.ExecuteEachRow ? "true" : "false";
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenDummyStepDialog(StepNodeViewModel stepVm)
        {
            var dialog = new DummyStepDialog { Owner = this };
            dialog.Initialize(stepVm.Step.Name);
            if (dialog.ShowDialog() == true)
            {
                stepVm.Step.Name = dialog.StepName;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenGenerateRowsDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new GenerateRowsDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("Fields", out var f) ? f?.ToString() ?? "" : "",
                step.Settings.TryGetValue("RowCount", out var rc) ? rc?.ToString() ?? "1" : "1");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["Fields"] = dialog.Fields;
                step.Settings["RowCount"] = dialog.RowCount;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenMergeJoinDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new MergeJoinDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("JoinType", out var jt) ? jt?.ToString() ?? "INNER" : "INNER",
                step.Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["JoinType"] = dialog.JoinType;
                step.Settings["KeyFields"] = dialog.KeyFields;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenDBUpdateDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new DBUpdateDialog { Owner = this };

            Guid? connId = step.Settings.TryGetValue("ConnectionId", out var cid) && cid != null
                ? Guid.TryParse(cid.ToString(), out var g) ? g : (Guid?)null
                : null;

            int commitSizeUpd = step.Settings.TryGetValue("CommitSize", out var csUpd)
                && int.TryParse(csUpd?.ToString(), out var csUpdVal) ? csUpdVal : 100;

            dialog.Initialize(
                step.Name,
                connId,
                step.Settings.TryGetValue("TableName", out var tn) ? tn?.ToString() ?? "" : "",
                step.Settings.TryGetValue("KeyFields", out var kf) ? kf?.ToString() ?? "" : "",
                step.Settings.TryGetValue("UpdateFields", out var uf) ? uf?.ToString() ?? "" : "",
                commitSizeUpd);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"] = dialog.TableName;
                step.Settings["KeyFields"] = dialog.KeyFields;
                step.Settings["UpdateFields"] = dialog.UpdateFields;
                step.Settings["CommitSize"] = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private void OpenSetVariableDialog(StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new SetVariableDialog { Owner = this };
            dialog.Initialize(
                step.Name,
                step.Settings.TryGetValue("Fields",     out var flds) ? flds?.ToString() ?? "" : "",
                step.Settings.TryGetValue("DateFormat", out var df)   ? df?.ToString()   ?? "yyyy/MM/dd" : "yyyy/MM/dd");

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["Fields"]     = dialog.Fields;
                step.Settings["DateFormat"] = dialog.DateFormat;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
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
            ScheduleStatusPanel.Children.Clear();
            var schedules = ScheduleRegistry.Instance.Schedules;

            if (schedules.Count == 0)
            {
                ScheduleStatusPanel.Children.Add(new TextBlock
                {
                    Text = "スケジュールがありません\n「スケジュール管理」から追加してください",
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
            // ステータス色の決定
            Color dotColor;
            if (!entry.IsEnabled)
                dotColor = Color.FromRgb(0x9E, 0x9E, 0x9E);      // gray
            else if (entry.LastRunSuccess == false)
                dotColor = Color.FromRgb(0xF4, 0x43, 0x36);      // red
            else if (entry.LastRunSuccess == true)
                dotColor = Color.FromRgb(0x4C, 0xAF, 0x50);      // green
            else
                dotColor = Color.FromRgb(0x21, 0x96, 0xF3);      // blue (未実行)

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
            leftStack.Children.Add(new Ellipse
            {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
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
            var dialog = new ConnectionManagerDialog { Owner = this };
            dialog.ShowDialog();
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

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RefreshSchedule_Click(object sender, RoutedEventArgs e)
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

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var text = string.Join(Environment.NewLine, _vm.LogMessages);
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        }
    }
}
