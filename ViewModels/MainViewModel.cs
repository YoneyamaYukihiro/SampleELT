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

        public Pipeline CurrentPipeline { get; private set; } = new Pipeline();

        private CancellationTokenSource? _cts;

        // Event fired when a step's settings dialog should open
        public event Action<StepNodeViewModel>? OpenSettingsRequested;

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
                StepType.GenerateRows => new GenerateRowsStep { Name = "Generate Rows", CanvasX = offsetX, CanvasY = offsetY },
                StepType.MergeJoin => new MergeJoinStep { Name = "Merge Join", CanvasX = offsetX, CanvasY = offsetY },
                StepType.DBUpdate => new DBUpdateStep { Name = "DB Update", CanvasX = offsetX, CanvasY = offsetY },
                StepType.SetVariable => new SetVariableStep { Name = "Set Variable", CanvasX = offsetX, CanvasY = offsetY },
                _ => throw new ArgumentOutOfRangeException(nameof(stepType))
            };

            CurrentPipeline.Steps.Add(step);
            var vm = new StepNodeViewModel(step);
            Steps.Add(vm);
            SelectedStep = vm;

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
        }

        [RelayCommand]
        private async Task RunPipeline()
        {
            if (IsRunning) return;

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
            Steps.Clear();
            Connections.Clear();
            CurrentPipeline.Steps.Clear();
            CurrentPipeline.Connections.Clear();
            SelectedStep = null;
            StatusMessage = "キャンバスをクリアしました";
            AddLog("キャンバスをクリアしました");
        }

        [RelayCommand]
        private void SavePipeline()
        {
            var dialog = new SaveFileDialog
            {
                Title = "パイプラインを保存",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json",
                FileName = CurrentPipeline.Name
            };

            if (dialog.ShowDialog() != true) return;

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
                File.WriteAllText(dialog.FileName, json);

                CurrentPipeline.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
                StatusMessage = $"保存完了: {dialog.FileName}";
                AddLog($"パイプライン保存: {dialog.FileName}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存エラー: {ex.Message}";
                AddLog($"保存エラー: {ex.Message}");
                System.Windows.MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void LoadPipeline()
        {
            var dialog = new OpenFileDialog
            {
                Title = "パイプラインを読み込む",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var pipelineData = JsonSerializer.Deserialize<PipelineSerializationModel>(json);
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
                        "GenerateRows" => new GenerateRowsStep(),
                        "MergeJoin" => new MergeJoinStep(),
                        "DBUpdate" => new DBUpdateStep(),
                        "SetVariable" => new SetVariableStep(),
                        _ => null
                    };

                    if (step == null) continue;

                    step.Name = stepData.Name;
                    step.CanvasX = stepData.CanvasX;
                    step.CanvasY = stepData.CanvasY;
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
                StatusMessage = $"読み込み完了: {dialog.FileName}";
                AddLog($"パイプライン読み込み: {dialog.FileName}");
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
            Steps.Clear();
            Connections.Clear();
            CurrentPipeline = new Pipeline();
            SelectedStep = null;
            StatusMessage = "新しいパイプライン";
            AddLog("新しいパイプラインを作成しました");
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

    // Serialization helper models
    public class PipelineSerializationModel
    {
        public string Name { get; set; } = "New Pipeline";
        public List<StepSerializationModel> Steps { get; set; } = new();
        public List<ConnectionSerializationModel> Connections { get; set; } = new();
    }

    public class StepSerializationModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string StepType { get; set; } = "";
        public double CanvasX { get; set; }
        public double CanvasY { get; set; }
        public Dictionary<string, string?> Settings { get; set; } = new();
    }

    public class ConnectionSerializationModel
    {
        public Guid Id { get; set; }
        public Guid SourceStepId { get; set; }
        public Guid TargetStepId { get; set; }
    }
}
