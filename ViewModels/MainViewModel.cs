using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SampleELT.Engine;
using SampleELT.Models;
using SampleELT.Models.Serialization;
using SampleELT.Services;
using SampleELT.Steps;

namespace SampleELT.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<StepNodeViewModel> _steps = new();

        [ObservableProperty]
        private ObservableCollection<ConnectionViewModel> _connections = new();

        [ObservableProperty]
        private ObservableCollection<string> _logMessages = new();

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private StepNodeViewModel? _selectedStep;

        [ObservableProperty]
        private string _statusMessage = "準備完了";

        [ObservableProperty]
        private string _windowTitle = "SampleELT";

        [ObservableProperty]
        private bool _isModified;

        [ObservableProperty]
        private string? _currentFilePath;

        [ObservableProperty]
        private string _currentFileDisplay = "(未保存)";

        public Pipeline CurrentPipeline { get; private set; } = new Pipeline();

        private CancellationTokenSource? _cts;

        // Event fired when a step's settings dialog should open
        public event Action<StepNodeViewModel>? OpenSettingsRequested;

        // Event fired when the schedule manager dialog should open
        public event Action? OpenScheduleManagerRequested;

        // Event fired when the job manager dialog should open
        public event Action? OpenJobManagerRequested;

        // Event fired when schedule status changes (run completed / registry updated)
        public event Action? ScheduleStatusChanged;

        // In-app scheduler (責務分離: タイマーと実行ロジックは PipelineSchedulerService が持つ)
        private readonly PipelineSchedulerService _scheduler;

        public MainViewModel()
        {
            _scheduler = new PipelineSchedulerService(new Progress<string>(msg =>
            {
                AddLog(msg);
                StatusMessage = msg;
            }));
            _scheduler.StatusChanged += () => ScheduleStatusChanged?.Invoke();
            _scheduler.Start();
        }

        // ==================== SCHEDULE MANAGER ====================

        [RelayCommand]
        private void OpenScheduleManager()
        {
            OpenScheduleManagerRequested?.Invoke();
        }

        [RelayCommand]
        private void OpenJobManager()
        {
            OpenJobManagerRequested?.Invoke();
        }

        [RelayCommand]
        private void AddStep(StepType stepType)
        {
            double offsetX = 100 + (Steps.Count % 5) * 160;
            double offsetY = 100 + (Steps.Count / 5) * 100;

            StepBase step = stepType switch
            {
                StepType.OracleInput => new OracleInputStep { Name = "Oracle Input", CanvasX = offsetX, CanvasY = offsetY },
                StepType.MySQLInput => new MySQLInputStep { Name = "MySQL Input", CanvasX = offsetX, CanvasY = offsetY },
                StepType.ExcelInput => new ExcelInputStep { Name = "File Input", CanvasX = offsetX, CanvasY = offsetY },
                StepType.OracleOutput => new OracleOutputStep { Name = "Oracle Output", CanvasX = offsetX, CanvasY = offsetY },
                StepType.MySQLOutput => new MySQLOutputStep { Name = "MySQL Output", CanvasX = offsetX, CanvasY = offsetY },
                StepType.ExcelOutput => new ExcelOutputStep { Name = "File Output", CanvasX = offsetX, CanvasY = offsetY },
                StepType.Filter => new FilterStep { Name = "Filter", CanvasX = offsetX, CanvasY = offsetY },
                StepType.Calculation => new CalculationStep { Name = "Calculation", CanvasX = offsetX, CanvasY = offsetY },
                StepType.SelectValues => new SelectValuesStep { Name = "Select Values", CanvasX = offsetX, CanvasY = offsetY },
                StepType.DBDelete => new DBDeleteStep { Name = "DB Delete", CanvasX = offsetX, CanvasY = offsetY },
                StepType.InsertUpdate => new InsertUpdateStep { Name = "Insert/Update", CanvasX = offsetX, CanvasY = offsetY },
                StepType.ExecSQL => new ExecSQLStep { Name = "Exec SQL", CanvasX = offsetX, CanvasY = offsetY },
                StepType.Dummy => new DummyStep { Name = "Dummy", CanvasX = offsetX, CanvasY = offsetY },
                StepType.MergeJoin => new MergeJoinStep { Name = "Merge Join", CanvasX = offsetX, CanvasY = offsetY },
                StepType.DBUpdate => new DBUpdateStep { Name = "DB Update", CanvasX = offsetX, CanvasY = offsetY },
                StepType.SetVariable => new SetVariableStep { Name = "Set Variable", CanvasX = offsetX, CanvasY = offsetY },
                StepType.DBInput    => new DBInputStep  { Name = "DB Input",   CanvasX = offsetX, CanvasY = offsetY },
                StepType.DBOutput   => new DBOutputStep { Name = "DB Output",  CanvasX = offsetX, CanvasY = offsetY },
                _ => throw new ArgumentOutOfRangeException(nameof(stepType))
            };

            CurrentPipeline.Steps.Add(step);
            var vm = new StepNodeViewModel(step);
            Steps.Add(vm);
            SelectedStep = vm;
            MarkModified();

            AddLog($"ステップ追加: {step.Name}");
            StatusMessage = $"ステップ追加: {step.Name}";
        }

        [RelayCommand]
        private void DeleteSelectedStep()
        {
            if (SelectedStep == null) return;

            var stepId = SelectedStep.Step.Id;

            // Remove connections that involve this step
            var connsToRemove = Connections.Where(c =>
                c.Connection.SourceStepId == stepId ||
                c.Connection.TargetStepId == stepId).ToList();

            foreach (var conn in connsToRemove)
            {
                Connections.Remove(conn);
                CurrentPipeline.Connections.Remove(conn.Connection);
            }

            // Remove step
            CurrentPipeline.Steps.Remove(SelectedStep.Step);
            Steps.Remove(SelectedStep);

            AddLog($"ステップ削除: {SelectedStep.Step.Name}");
            StatusMessage = "ステップを削除しました";
            SelectedStep = null;
            MarkModified();
        }

        [RelayCommand]
        private async Task RunPipeline()
        {
            if (IsRunning) return;

            // 手動実行前に上書き保存（ファイルが既存の場合のみ）
            if (!string.IsNullOrEmpty(CurrentFilePath))
            {
                SaveToFile(CurrentFilePath);
                AddLog($"パイプライン保存: {CurrentFilePath}");
            }

            IsRunning = true;
            StatusMessage = "実行中...";
            _cts = new CancellationTokenSource();

            AddLog("===== パイプライン実行開始 =====");

            var progress = new Progress<string>(msg =>
            {
                AddLog(msg);
                StatusMessage = msg;
            });

            try
            {
                var engine = new ExecutionEngine();
                await engine.ExecuteAsync(CurrentPipeline, progress, _cts.Token);
                StatusMessage = "実行完了";
                AddLog("===== 実行完了 =====");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "キャンセルされました";
                AddLog("===== 実行キャンセル =====");
            }
            catch (Exception ex)
            {
                StatusMessage = $"エラー: {ex.Message}";
                AddLog($"エラー: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        [RelayCommand]
        private void Stop()
        {
            _cts?.Cancel();
            StatusMessage = "停止中...";
            AddLog("停止リクエスト送信");
        }

        [RelayCommand]
        private void ClearCanvas()
        {
            if (!ConfirmDiscardChanges()) return;
            Steps.Clear();
            Connections.Clear();
            CurrentPipeline.Steps.Clear();
            CurrentPipeline.Connections.Clear();
            SelectedStep = null;
            IsModified = false;
            StatusMessage = "キャンバスをクリアしました";
            AddLog("キャンバスをクリアしました");
        }

        [RelayCommand]
        private void SavePipeline() => TrySave();

        [RelayCommand]
        private void SavePipelineAs() => TrySaveAs();

        /// <summary>現在のファイルに保存。未保存の場合は名前を付けて保存にフォールバック。</summary>
        public bool TrySave()
        {
            return !string.IsNullOrEmpty(CurrentFilePath)
                ? SaveToFile(CurrentFilePath)
                : TrySaveAs();
        }

        /// <summary>名前を付けて保存。ユーザーがファイル選択をキャンセルした場合は false。</summary>
        public bool TrySaveAs()
        {
            var dialog = new SaveFileDialog
            {
                Title = "名前を付けてパイプラインを保存",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = CurrentPipeline.Name
            };

            if (dialog.ShowDialog() != true) return false;

            return SaveToFile(dialog.FileName);
        }

        private bool SaveToFile(string filePath)
        {
            try
            {
                var pipelineData = new PipelineSerializationModel
                {
                    Name = CurrentPipeline.Name,
                    Steps = CurrentPipeline.Steps.Select(s => new StepSerializationModel
                    {
                        Id = s.Id,
                        Name = s.Name,
                        StepType = s.StepType.ToString(),
                        CanvasX = s.CanvasX,
                        CanvasY = s.CanvasY,
                        NodeWidth = s.NodeWidth,
                        NodeHeight = s.NodeHeight,
                        Settings = s.Settings.ToDictionary(
                            kv => kv.Key,
                            kv => kv.Value?.ToString()
                        )
                    }).ToList(),
                    Connections = CurrentPipeline.Connections.Select(c => new ConnectionSerializationModel
                    {
                        Id = c.Id,
                        SourceStepId = c.SourceStepId,
                        TargetStepId = c.TargetStepId
                    }).ToList()
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(pipelineData, options);
                File.WriteAllText(filePath, json);

                CurrentFilePath = filePath;
                CurrentPipeline.Name = Path.GetFileNameWithoutExtension(filePath);
                IsModified = false;
                StatusMessage = $"保存完了: {filePath}";
                AddLog($"パイプライン保存: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存エラー: {ex.Message}";
                AddLog($"保存エラー: {ex.Message}");
                System.Windows.MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        [RelayCommand]
        private void LoadPipeline()
        {
            if (!ConfirmDiscardChanges()) return;

            var dialog = new OpenFileDialog
            {
                Title = "パイプラインを読み込む",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;
            LoadPipelineFromFile(dialog.FileName);
        }

        public void LoadPipelineFromFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var pipelineData = JsonSerializer.Deserialize<PipelineSerializationModel>(json, options);
                if (pipelineData == null) return;

                // Clear current state
                Steps.Clear();
                Connections.Clear();
                CurrentPipeline = new Pipeline { Name = pipelineData.Name };

                // Rebuild steps
                foreach (var stepData in pipelineData.Steps)
                {
                    StepBase? step = stepData.StepType switch
                    {
                        "OracleInput" => new OracleInputStep(),
                        "MySQLInput" => new MySQLInputStep(),
                        "ExcelInput" => new ExcelInputStep(),
                        "OracleOutput" => new OracleOutputStep(),
                        "MySQLOutput" => new MySQLOutputStep(),
                        "ExcelOutput" => new ExcelOutputStep(),
                        "Filter" => new FilterStep(),
                        "Calculation" => new CalculationStep(),
                        "SelectValues" => new SelectValuesStep(),
                        "DBDelete" => new DBDeleteStep(),
                        "InsertUpdate" => new InsertUpdateStep(),
                        "ExecSQL" => new ExecSQLStep(),
                        "Dummy" => new DummyStep(),
                        "MergeJoin" => new MergeJoinStep(),
                        "DBUpdate" => new DBUpdateStep(),
                        "SetVariable" => new SetVariableStep(),
                        "DBInput"     => new DBInputStep(),
                        "DBOutput"    => new DBOutputStep(),
                        _ => null
                    };

                    if (step == null) continue;

                    step.Name = stepData.Name;
                    step.CanvasX = stepData.CanvasX;
                    step.CanvasY = stepData.CanvasY;
                    step.NodeWidth = stepData.NodeWidth > 0 ? stepData.NodeWidth : 150.0;
                    step.NodeHeight = stepData.NodeHeight > 0 ? stepData.NodeHeight : 70.0;
                    step.Settings = stepData.Settings.ToDictionary(
                        kv => kv.Key,
                        kv => (object?)kv.Value
                    );

                    // We need to set the Id - using reflection since it has getter only
                    var idField = typeof(StepBase).GetField("<Id>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    idField?.SetValue(step, stepData.Id);

                    CurrentPipeline.Steps.Add(step);
                    Steps.Add(new StepNodeViewModel(step));
                }

                // Rebuild connections
                foreach (var connData in pipelineData.Connections)
                {
                    var sourceVm = Steps.FirstOrDefault(s => s.Step.Id == connData.SourceStepId);
                    var targetVm = Steps.FirstOrDefault(s => s.Step.Id == connData.TargetStepId);

                    if (sourceVm == null || targetVm == null) continue;

                    var conn = new PipelineConnection
                    {
                        SourceStepId = connData.SourceStepId,
                        TargetStepId = connData.TargetStepId
                    };

                    CurrentPipeline.Connections.Add(conn);
                    Connections.Add(new ConnectionViewModel(conn, sourceVm, targetVm));
                }

                SelectedStep = null;
                CurrentFilePath = filePath;
                IsModified = false;
                StatusMessage = $"読み込み完了: {filePath}";
                AddLog($"パイプライン読み込み: {filePath}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"読み込みエラー: {ex.Message}";
                AddLog($"読み込みエラー: {ex.Message}");
                System.Windows.MessageBox.Show($"読み込みに失敗しました:\n{ex.Message}", "エラー",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ConnectSteps(Tuple<Guid, Guid> stepPair)
        {
            var sourceId = stepPair.Item1;
            var targetId = stepPair.Item2;

            if (sourceId == targetId) return;

            // Prevent duplicate connections
            bool exists = CurrentPipeline.Connections.Any(c =>
                c.SourceStepId == sourceId && c.TargetStepId == targetId);
            if (exists) return;

            var sourceVm = Steps.FirstOrDefault(s => s.Step.Id == sourceId);
            var targetVm = Steps.FirstOrDefault(s => s.Step.Id == targetId);

            if (sourceVm == null || targetVm == null) return;

            var conn = new PipelineConnection
            {
                SourceStepId = sourceId,
                TargetStepId = targetId
            };

            CurrentPipeline.Connections.Add(conn);
            Connections.Add(new ConnectionViewModel(conn, sourceVm, targetVm));

            MarkModified();
            AddLog($"接続: {sourceVm.DisplayName} → {targetVm.DisplayName}");
            StatusMessage = $"接続作成: {sourceVm.DisplayName} → {targetVm.DisplayName}";
        }

        [RelayCommand]
        private void OpenStepSettings(StepNodeViewModel stepVm)
        {
            OpenSettingsRequested?.Invoke(stepVm);
        }

        [RelayCommand]
        private void NewPipeline()
        {
            if (!ConfirmDiscardChanges()) return;
            Steps.Clear();
            Connections.Clear();
            CurrentPipeline = new Pipeline();
            SelectedStep = null;
            CurrentFilePath = null;
            IsModified = false;
            StatusMessage = "新しいパイプライン";
            AddLog("新しいパイプラインを作成しました");
        }

        // ==================== DIRTY TRACKING ====================

        public void MarkModified()
        {
            IsModified = true;
        }

        partial void OnIsModifiedChanged(bool value)
        {
            UpdateWindowTitleAndFileDisplay();
        }

        partial void OnCurrentFilePathChanged(string? value)
        {
            UpdateWindowTitleAndFileDisplay();
        }

        private void UpdateWindowTitleAndFileDisplay()
        {
            var fileLabel = string.IsNullOrEmpty(CurrentFilePath)
                ? (string.IsNullOrWhiteSpace(CurrentPipeline.Name) ? "新しいパイプライン" : CurrentPipeline.Name)
                : Path.GetFileName(CurrentFilePath);

            WindowTitle = IsModified ? $"SampleELT - {fileLabel} *" : $"SampleELT - {fileLabel}";

            CurrentFileDisplay = string.IsNullOrEmpty(CurrentFilePath)
                ? "(未保存)"
                : CurrentFilePath!;
        }

        /// <summary>未保存の変更がある場合、ユーザーに確認する。続行してよければ true を返す。</summary>
        public bool ConfirmDiscardChanges()
        {
            if (!IsModified) return true;
            var result = System.Windows.MessageBox.Show(
                "パイプラインに未保存の変更があります。変更を破棄して続行しますか？",
                "未保存の変更",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            return result == System.Windows.MessageBoxResult.Yes;
        }

        /// <summary>
        /// 終了時に未保存の変更があれば「保存して終了 / 保存せず終了 / キャンセル」を確認する。
        /// 「保存して終了」を選んだ場合は保存処理まで実行する。終了してよい場合 true を返す。
        /// </summary>
        public bool TryConfirmClose()
        {
            if (!IsModified) return true;
            var result = System.Windows.MessageBox.Show(
                "パイプラインに未保存の変更があります。\n\n" +
                "「はい」: 保存して終了\n" +
                "「いいえ」: 保存せず終了\n" +
                "「キャンセル」: 終了をキャンセル",
                "未保存の変更",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            return result switch
            {
                System.Windows.MessageBoxResult.Yes    => TrySave(), // 保存に成功した場合のみ終了
                System.Windows.MessageBoxResult.No     => true,      // 保存せず終了
                _                                       => false     // キャンセル
            };
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogMessages.Add($"[{timestamp}] {message}");
        }

        public void DeleteStep(StepNodeViewModel stepVm)
        {
            SelectedStep = stepVm;
            DeleteSelectedStep();
        }
    }
}
