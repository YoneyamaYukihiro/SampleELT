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
using BreezeFlow.Engine;
using BreezeFlow.Models;
using BreezeFlow.Models.Serialization;
using BreezeFlow.Services;
using BreezeFlow.Steps;

namespace BreezeFlow.ViewModels
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
        private string _windowTitle = "BreezeFlow";

        [ObservableProperty]
        private bool _isModified;

        [ObservableProperty]
        private string? _currentFilePath;

        [ObservableProperty]
        private string _currentFileDisplay = "(未保存)";

        public Pipeline CurrentPipeline { get; private set; } = new Pipeline();

        /// <summary>
        /// ステップ追加・削除・接続・移動・設定変更などを取り消し可能にするマネージャ。
        /// すべてのキャンバス変更はこれを経由して実行する。
        /// パイプライン読み込み / 新規 / クリアの際は <see cref="UndoManager.Clear"/> で履歴を破棄する。
        /// </summary>
        public UndoManager UndoManager { get; } = new UndoManager();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
        private bool _canUndo;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
        private bool _canRedo;

        /// <summary>次回 Undo の説明 (ボタンの ToolTip で表示する用途)。</summary>
        [ObservableProperty]
        private string _undoTooltip = "取り消し (Ctrl+Z)";

        [ObservableProperty]
        private string _redoTooltip = "やり直し (Ctrl+Y)";

        /// <summary>パイプラインのログ出力モード（CLI ヘッドレス実行時のログファイル出力制御）。UI からバインドするためのプロキシ。</summary>
        public LogMode LogMode
        {
            get => CurrentPipeline.LogMode;
            set
            {
                if (CurrentPipeline.LogMode == value) return;
                CurrentPipeline.LogMode = value;
                OnPropertyChanged();
                MarkModified();
            }
        }

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

            UndoManager.Changed += OnUndoChanged;
            OnUndoChanged();
        }

        private void OnUndoChanged()
        {
            CanUndo = UndoManager.CanUndo;
            CanRedo = UndoManager.CanRedo;
            var u = UndoManager.NextUndoDescription;
            var r = UndoManager.NextRedoDescription;
            UndoTooltip = u != null ? $"取り消し: {u} (Ctrl+Z)" : "取り消し (Ctrl+Z)";
            RedoTooltip = r != null ? $"やり直し: {r} (Ctrl+Y)" : "やり直し (Ctrl+Y)";
        }

        // ==================== UNDO / REDO ====================

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            var cmd = UndoManager.Undo();
            if (cmd == null) return;
            AddLog($"取り消し: {cmd.Description}");
            StatusMessage = $"取り消し: {cmd.Description}";
            MarkModified();
        }

        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo()
        {
            var cmd = UndoManager.Redo();
            if (cmd == null) return;
            AddLog($"やり直し: {cmd.Description}");
            StatusMessage = $"やり直し: {cmd.Description}";
            MarkModified();
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
                StepType.TableCompare => new TableCompareStep { Name = "Table Compare", CanvasX = offsetX, CanvasY = offsetY },
                StepType.Switch => new SwitchStep { Name = "Switch", CanvasX = offsetX, CanvasY = offsetY },
                _ => throw new ArgumentOutOfRangeException(nameof(stepType))
            };

            // 既に確定した step / vm を Undo / Redo で出し入れする
            var vm = new StepNodeViewModel(step);
            UndoManager.Execute(
                $"ステップ追加: {step.Name}",
                doAction: () =>
                {
                    if (!CurrentPipeline.Steps.Contains(step)) CurrentPipeline.Steps.Add(step);
                    if (!Steps.Contains(vm)) Steps.Add(vm);
                    SelectedStep = vm;
                },
                undoAction: () =>
                {
                    if (SelectedStep == vm) SelectedStep = null;
                    Steps.Remove(vm);
                    CurrentPipeline.Steps.Remove(step);
                });
            MarkModified();

            AddLog($"ステップ追加: {step.Name}");
            StatusMessage = $"ステップ追加: {step.Name}";
        }

        /// <summary>選択中のステップ全件 (複数選択対応)。<see cref="SelectedStep"/> は最後にクリックされた "primary" を指す。</summary>
        public IList<StepNodeViewModel> GetSelectedSteps()
            => Steps.Where(s => s.IsSelected).ToList();

        [RelayCommand]
        private void DeleteSelectedStep()
        {
            var targets = GetSelectedSteps();
            if (targets.Count == 0 && SelectedStep != null) targets = new List<StepNodeViewModel> { SelectedStep };
            if (targets.Count == 0) return;

            // 削除対象 step と関連接続のスナップショット
            var stepIds = new HashSet<Guid>(targets.Select(t => t.Step.Id));
            var removedConns = Connections.Where(c =>
                stepIds.Contains(c.Connection.SourceStepId) ||
                stepIds.Contains(c.Connection.TargetStepId)).ToList();

            var desc = targets.Count == 1
                ? $"ステップ削除: {targets[0].Step.Name}"
                : $"ステップ削除: {targets.Count}件";

            UndoManager.Execute(
                desc,
                doAction: () =>
                {
                    foreach (var conn in removedConns)
                    {
                        Connections.Remove(conn);
                        CurrentPipeline.Connections.Remove(conn.Connection);
                    }
                    foreach (var vm in targets)
                    {
                        Steps.Remove(vm);
                        CurrentPipeline.Steps.Remove(vm.Step);
                    }
                    if (SelectedStep != null && stepIds.Contains(SelectedStep.Step.Id))
                        SelectedStep = null;
                },
                undoAction: () =>
                {
                    foreach (var vm in targets)
                    {
                        CurrentPipeline.Steps.Add(vm.Step);
                        Steps.Add(vm);
                    }
                    foreach (var conn in removedConns)
                    {
                        CurrentPipeline.Connections.Add(conn.Connection);
                        Connections.Add(conn);
                    }
                });

            AddLog(desc);
            StatusMessage = "ステップを削除しました";
            MarkModified();
        }

        [RelayCommand]
        private async Task RunPipeline()
        {
            if (IsRunning) return;

            // 実行前安全検査: 未解決接続 / Read-only / Production 書き込みをチェック
            var issues = BreezeFlow.Engine.PipelineSafetyChecker.Check(CurrentPipeline);
            var blockers = issues.Where(i => i.Severity == BreezeFlow.Engine.PipelineSafetyChecker.IssueSeverity.Block).ToList();
            var confirms = issues.Where(i => i.Severity == BreezeFlow.Engine.PipelineSafetyChecker.IssueSeverity.Confirm).ToList();

            if (blockers.Count > 0)
            {
                var msg = "以下の理由で実行できません:\n\n"
                    + BreezeFlow.Engine.PipelineSafetyChecker.Format(blockers);
                System.Windows.MessageBox.Show(msg, "実行ブロック",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLog("===== 実行ブロック (事前検査エラー) =====");
                foreach (var b in blockers) AddLog($"  - [{b.StepName}] {b.Message}");
                return;
            }

            if (confirms.Count > 0)
            {
                var msg = "Production 接続に対する書き込み操作が含まれます。実行してよろしいですか？\n\n"
                    + BreezeFlow.Engine.PipelineSafetyChecker.Format(confirms);
                var result = System.Windows.MessageBox.Show(msg, "Production 書き込みの最終確認",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    AddLog("===== 実行キャンセル (Production 書き込み確認で No) =====");
                    return;
                }
                AddLog("Production 書き込みを承認:");
                foreach (var c in confirms) AddLog($"  ✓ [{c.StepName}] {c.Message}");
            }

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

            var rawProgress = new Progress<string>(msg =>
            {
                AddLog(msg);
                StatusMessage = msg;
            });

            // 実行履歴ストアが有効ならパイプライン単位で記録する
            long runId = -1;
            BreezeFlow.Services.RunHistoryProgressWriter? historyWriter = null;
            IProgress<string> progress = rawProgress;
            if (BreezeFlow.Services.RunHistoryStore.Instance != null)
            {
                runId = BreezeFlow.Services.RunHistoryStore.Instance.BeginRun(new RunRecord
                {
                    PipelinePath = CurrentFilePath,
                    PipelineName = CurrentPipeline.Name,
                    Trigger = "manual",
                    StartedAt = DateTime.Now
                });
                historyWriter = new BreezeFlow.Services.RunHistoryProgressWriter(
                    BreezeFlow.Services.RunHistoryStore.Instance, runId, rawProgress);
                progress = historyWriter;
            }

            RunStatus finalStatus = RunStatus.Failed;
            string? finalError = null;
            try
            {
                var engine = new ExecutionEngine();
                await engine.ExecuteAsync(CurrentPipeline, progress, _cts.Token);
                finalStatus = RunStatus.Success;
                StatusMessage = "実行完了";
                AddLog("===== 実行完了 =====");
            }
            catch (OperationCanceledException)
            {
                finalStatus = RunStatus.Cancelled;
                finalError = "キャンセル";
                StatusMessage = "キャンセルされました";
                AddLog("===== 実行キャンセル =====");
            }
            catch (Exception ex)
            {
                finalStatus = RunStatus.Failed;
                finalError = ex.Message;
                StatusMessage = $"エラー: {ex.Message}";
                AddLog($"エラー: {ex.Message}");
            }
            finally
            {
                historyWriter?.FinishUnclosedSteps(finalStatus, finalError);
                if (runId > 0 && BreezeFlow.Services.RunHistoryStore.Instance != null)
                    BreezeFlow.Services.RunHistoryStore.Instance.EndRun(
                        runId, finalStatus, finalError, historyWriter?.LastReportedRowCount);

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
            UndoManager.Clear();
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
                    LogMode = CurrentPipeline.LogMode,
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
                        TargetStepId = c.TargetStepId,
                        SourceBranchKey = c.SourceBranchKey
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
                Filter = "Pipeline files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            // 選択されたファイルがジョブファイルだった場合は別の経路に誘導する
            // (OpenFileDialog の Win32 フィルタでは .job.json を除外できないため、選択後に検出する)
            if (LooksLikeJobFile(dialog.FileName))
            {
                var ans = System.Windows.MessageBox.Show(
                    "選択したファイルはジョブファイル (.job.json) です。\n" +
                    "パイプラインの「開く」では読み込めません。\n\n" +
                    "ジョブマネージャー (🔗 ジョブ) を開きますか？",
                    "ジョブファイルです",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);
                if (ans == System.Windows.MessageBoxResult.Yes)
                    OpenJobManagerRequested?.Invoke();
                return;
            }

            LoadPipelineFromFile(dialog.FileName);
        }

        /// <summary>
        /// 指定ファイルがジョブファイルらしいかを判定する。
        /// 拡張子 (.job.json) と内容 (Steps[0] に PipelineFilePath があるか) で判定。
        /// </summary>
        private static bool LooksLikeJobFile(string filePath)
        {
            // 1. 拡張子で判定 (典型的な命名規則)
            if (filePath.EndsWith(".job.json", StringComparison.OrdinalIgnoreCase))
                return true;

            // 2. 内容で判定 (Steps[0] に PipelineFilePath プロパティがあれば Job)
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("Steps", out var steps)
                    && steps.ValueKind == JsonValueKind.Array
                    && steps.GetArrayLength() > 0)
                {
                    var first = steps[0];
                    if (first.ValueKind == JsonValueKind.Object
                        && first.TryGetProperty("PipelineFilePath", out _))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 読み込み・解析エラー時は Job ではないとみなす (LoadPipelineFromFile 側で通常のエラー処理)
            }
            return false;
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
                CurrentPipeline = new Pipeline
                {
                    Name = pipelineData.Name,
                    LogMode = pipelineData.LogMode
                };

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
                        "TableCompare" => new TableCompareStep(),
                        "Switch"      => new SwitchStep(),
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
                // 旧 JSON との後方互換: SourceBranchKey 未指定の接続で、ソースが Filter のものは "pass" を補う。
                // 旧 Filter は単一出力 (一致行のみ通過) だったため、それと等価な動作を維持するための移行。
                foreach (var connData in pipelineData.Connections)
                {
                    var sourceVm = Steps.FirstOrDefault(s => s.Step.Id == connData.SourceStepId);
                    var targetVm = Steps.FirstOrDefault(s => s.Step.Id == connData.TargetStepId);

                    if (sourceVm == null || targetVm == null) continue;

                    var branchKey = connData.SourceBranchKey;
                    if (branchKey == null && sourceVm.Step is FilterStep)
                        branchKey = FilterStep.PassBranchKey;

                    var conn = new PipelineConnection
                    {
                        SourceStepId = connData.SourceStepId,
                        TargetStepId = connData.TargetStepId,
                        SourceBranchKey = branchKey
                    };

                    CurrentPipeline.Connections.Add(conn);
                    Connections.Add(new ConnectionViewModel(conn, sourceVm, targetVm));
                }

                SelectedStep = null;
                CurrentFilePath = filePath;
                IsModified = false;
                UndoManager.Clear();
                OnPropertyChanged(nameof(LogMode));
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
            ConnectStepsFromPort(stepPair.Item1, null, stepPair.Item2);
        }

        /// <summary>
        /// 多ポート対応の接続作成。出力元ステップのどのポート (BranchKey) から伸びる接続かを記録する。
        /// branchKey = null の場合、ソースが多ポートステップなら出力ポート一覧の先頭を採用する。
        /// 単ポートステップでは null = "" (既定ポート) のまま保存する。
        /// </summary>
        public void ConnectStepsFromPort(Guid sourceId, string? branchKey, Guid targetId)
        {
            if (sourceId == targetId) return;

            var sourceVm = Steps.FirstOrDefault(s => s.Step.Id == sourceId);
            var targetVm = Steps.FirstOrDefault(s => s.Step.Id == targetId);
            if (sourceVm == null || targetVm == null) return;

            // BranchKey 正規化: 単ポートステップなら null/任意の値を null (= 既定ポート) に集約する
            var ports = sourceVm.Step.OutputPorts;
            bool sourceIsMultiPort = ports.Count > 1 || (ports.Count == 1 && !string.IsNullOrEmpty(ports[0].Key));
            string? effectiveBranch;
            if (!sourceIsMultiPort)
            {
                effectiveBranch = null;
            }
            else if (branchKey == null)
            {
                // 多ポートステップで明示指定が無ければ先頭ポートをデフォルトに採用
                effectiveBranch = ports[0].Key;
            }
            else
            {
                effectiveBranch = branchKey;
            }

            // 重複接続を抑制 (同じ source/branch → target は 1 本のみ)
            bool exists = CurrentPipeline.Connections.Any(c =>
                c.SourceStepId == sourceId
                && c.TargetStepId == targetId
                && string.Equals(c.SourceBranchKey ?? string.Empty, effectiveBranch ?? string.Empty, StringComparison.Ordinal));
            if (exists) return;

            var conn = new PipelineConnection
            {
                SourceStepId = sourceId,
                TargetStepId = targetId,
                SourceBranchKey = effectiveBranch
            };
            var connVm = new ConnectionViewModel(conn, sourceVm, targetVm);

            var portLabel = string.IsNullOrEmpty(effectiveBranch) ? "" : $" [{effectiveBranch}]";
            UndoManager.Execute(
                $"接続作成: {sourceVm.DisplayName}{portLabel} → {targetVm.DisplayName}",
                doAction: () =>
                {
                    if (!CurrentPipeline.Connections.Contains(conn)) CurrentPipeline.Connections.Add(conn);
                    if (!Connections.Contains(connVm)) Connections.Add(connVm);
                },
                undoAction: () =>
                {
                    Connections.Remove(connVm);
                    CurrentPipeline.Connections.Remove(conn);
                });

            MarkModified();
            AddLog($"接続: {sourceVm.DisplayName}{portLabel} → {targetVm.DisplayName}");
            StatusMessage = $"接続作成: {sourceVm.DisplayName}{portLabel} → {targetVm.DisplayName}";
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
            UndoManager.Clear();
            OnPropertyChanged(nameof(LogMode));
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

            WindowTitle = IsModified ? $"BreezeFlow - {fileLabel} *" : $"BreezeFlow - {fileLabel}";

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

        public void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogMessages.Add($"[{timestamp}] {message}");
        }

        public void DeleteStep(StepNodeViewModel stepVm)
        {
            SelectedStep = stepVm;
            DeleteSelectedStep();
        }

        // ==================== COPY / PASTE / DUPLICATE ====================
        //
        // クリップボードには PipelineSerializationModel と同形式の JSON を入れる。
        // 内部接続 (= コピー対象の step 間にあるもの) も保持し、Paste 時に
        // 新しい Guid に振り直して再構築する。

        private const string ClipboardFormat = "BreezeFlowSteps";

        /// <summary>編集メニュー / Ctrl+A 用: キャンバス上の全ステップを選択する。</summary>
        [RelayCommand]
        public void SelectAll()
        {
            if (Steps.Count == 0) return;
            foreach (var s in Steps) s.IsSelected = true;
            SelectedStep = Steps[Steps.Count - 1];
            StatusMessage = $"{Steps.Count}件 選択";
        }

        /// <summary>編集メニュー用: 全選択解除。</summary>
        [RelayCommand]
        public void DeselectAll()
        {
            foreach (var s in Steps) s.IsSelected = false;
            SelectedStep = null;
        }

        // ==================== コマンドエスポージ ====================
        // メニュー / コンテキストメニューから RelayCommand で呼べるように、
        // 既存の Copy/Paste/Duplicate メソッドをラップしたコマンドを公開する。

        [RelayCommand] private void Copy()      => CopySelected();
        [RelayCommand] private void Paste()     => PasteFromClipboard();
        [RelayCommand] private void Duplicate() => DuplicateSelected();
        [RelayCommand] private void FindOpen()  => OpenFindRequested?.Invoke();

        /// <summary>MainWindow に Ctrl+F の検索バーを開かせるためのイベント。</summary>
        public event Action? OpenFindRequested;

        public void CopySelected()
        {
            var selected = GetSelectedSteps();
            if (selected.Count == 0) return;

            var json = SerializeStepsToJson(selected);
            try
            {
                System.Windows.Clipboard.SetData(ClipboardFormat, json);
                // フォールバックとして通常のテキストにも書く (他アプリでテキストとして見たい場合のため)
                System.Windows.Clipboard.SetText(json, System.Windows.TextDataFormat.UnicodeText);
                StatusMessage = $"コピー: {selected.Count}件";
                AddLog($"コピー: {selected.Count}件");
            }
            catch (Exception ex)
            {
                AddLog($"コピーに失敗: {ex.Message}");
            }
        }

        public void PasteFromClipboard()
        {
            try
            {
                string? json = null;
                if (System.Windows.Clipboard.ContainsData(ClipboardFormat))
                    json = System.Windows.Clipboard.GetData(ClipboardFormat) as string;
                else if (System.Windows.Clipboard.ContainsText())
                    json = System.Windows.Clipboard.GetText();

                if (string.IsNullOrWhiteSpace(json)) return;
                PasteFromJson(json, offsetX: 30, offsetY: 30, description: "貼り付け");
            }
            catch (Exception ex)
            {
                AddLog($"貼り付けに失敗: {ex.Message}");
            }
        }

        /// <summary>選択中のステップを複製する (クリップボードは触らない)。</summary>
        public void DuplicateSelected()
        {
            var selected = GetSelectedSteps();
            if (selected.Count == 0) return;
            var json = SerializeStepsToJson(selected);
            PasteFromJson(json, offsetX: 24, offsetY: 24, description: "複製");
        }

        private string SerializeStepsToJson(IList<StepNodeViewModel> selectedVms)
        {
            var selectedIds = new HashSet<Guid>(selectedVms.Select(v => v.Step.Id));
            var model = new PipelineSerializationModel
            {
                Steps = selectedVms.Select(v => new StepSerializationModel
                {
                    Id = v.Step.Id,
                    Name = v.Step.Name,
                    StepType = v.Step.StepType.ToString(),
                    CanvasX = v.Step.CanvasX,
                    CanvasY = v.Step.CanvasY,
                    NodeWidth = v.Step.NodeWidth,
                    NodeHeight = v.Step.NodeHeight,
                    Settings = v.Step.Settings.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString())
                }).ToList(),
                // 内部接続のみ (両端が選択中の step) を保持
                Connections = CurrentPipeline.Connections
                    .Where(c => selectedIds.Contains(c.SourceStepId) && selectedIds.Contains(c.TargetStepId))
                    .Select(c => new ConnectionSerializationModel
                    {
                        Id = c.Id,
                        SourceStepId = c.SourceStepId,
                        TargetStepId = c.TargetStepId,
                        SourceBranchKey = c.SourceBranchKey
                    }).ToList()
            };
            var opts = new JsonSerializerOptions { WriteIndented = false };
            return JsonSerializer.Serialize(model, opts);
        }

        private void PasteFromJson(string json, double offsetX, double offsetY, string description)
        {
            PipelineSerializationModel? model;
            try
            {
                model = JsonSerializer.Deserialize<PipelineSerializationModel>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                AddLog("貼り付け対象が BreezeFlow のステップではありませんでした");
                return;
            }
            if (model == null || model.Steps.Count == 0) return;

            // 旧 Id → 新 Id へのマッピング
            var idMap = new Dictionary<Guid, Guid>();
            var newSteps = new List<(StepBase Step, StepNodeViewModel Vm)>();
            foreach (var sm in model.Steps)
            {
                var step = CreateStepFromTypeName(sm.StepType);
                if (step == null) continue;

                step.Name     = sm.Name;
                step.CanvasX  = sm.CanvasX + offsetX;
                step.CanvasY  = sm.CanvasY + offsetY;
                step.NodeWidth  = sm.NodeWidth  > 0 ? sm.NodeWidth  : 150.0;
                step.NodeHeight = sm.NodeHeight > 0 ? sm.NodeHeight : 70.0;
                step.Settings = sm.Settings.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                // 新 Guid に振り直す (StepBase.Id は getter-only)
                var newId = Guid.NewGuid();
                idMap[sm.Id] = newId;
                var idField = typeof(StepBase).GetField("<Id>k__BackingField",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                idField?.SetValue(step, newId);

                newSteps.Add((step, new StepNodeViewModel(step)));
            }
            if (newSteps.Count == 0) return;

            // 接続も新 Id で再構築 (両端が貼り付けたステップ内に存在する場合のみ)
            var newConns = new List<(PipelineConnection Conn, ConnectionViewModel Vm)>();
            foreach (var cm in model.Connections)
            {
                if (!idMap.TryGetValue(cm.SourceStepId, out var srcId)) continue;
                if (!idMap.TryGetValue(cm.TargetStepId, out var tgtId)) continue;

                var srcVm = newSteps.First(p => p.Step.Id == srcId).Vm;
                var tgtVm = newSteps.First(p => p.Step.Id == tgtId).Vm;
                var conn = new PipelineConnection
                {
                    SourceStepId = srcId,
                    TargetStepId = tgtId,
                    SourceBranchKey = cm.SourceBranchKey
                };
                newConns.Add((conn, new ConnectionViewModel(conn, srcVm, tgtVm)));
            }

            // まとめて 1 つの Undo コマンドにする
            UndoManager.Execute(
                $"{description}: {newSteps.Count}件",
                doAction: () =>
                {
                    foreach (var s in newSteps)
                    {
                        CurrentPipeline.Steps.Add(s.Step);
                        Steps.Add(s.Vm);
                    }
                    foreach (var c in newConns)
                    {
                        CurrentPipeline.Connections.Add(c.Conn);
                        Connections.Add(c.Vm);
                    }
                    // 貼り付けた step を選択状態にする
                    foreach (var s in Steps) s.IsSelected = false;
                    foreach (var s in newSteps) s.Vm.IsSelected = true;
                    SelectedStep = newSteps.Last().Vm;
                },
                undoAction: () =>
                {
                    foreach (var c in newConns)
                    {
                        Connections.Remove(c.Vm);
                        CurrentPipeline.Connections.Remove(c.Conn);
                    }
                    foreach (var s in newSteps)
                    {
                        Steps.Remove(s.Vm);
                        CurrentPipeline.Steps.Remove(s.Step);
                    }
                });

            StatusMessage = $"{description}: {newSteps.Count}件";
            AddLog($"{description}: {newSteps.Count}件");
            MarkModified();
        }

        /// <summary>StepType 文字列から StepBase インスタンスを生成する (PipelineLoader と同じディスパッチ表)。</summary>
        private static StepBase? CreateStepFromTypeName(string typeName) => typeName switch
        {
            "OracleInput"  => new OracleInputStep(),
            "MySQLInput"   => new MySQLInputStep(),
            "ExcelInput"   => new ExcelInputStep(),
            "OracleOutput" => new OracleOutputStep(),
            "MySQLOutput"  => new MySQLOutputStep(),
            "ExcelOutput"  => new ExcelOutputStep(),
            "Filter"       => new FilterStep(),
            "Calculation"  => new CalculationStep(),
            "SelectValues" => new SelectValuesStep(),
            "DBDelete"     => new DBDeleteStep(),
            "InsertUpdate" => new InsertUpdateStep(),
            "ExecSQL"      => new ExecSQLStep(),
            "Dummy"        => new DummyStep(),
            "MergeJoin"    => new MergeJoinStep(),
            "DBUpdate"     => new DBUpdateStep(),
            "SetVariable"  => new SetVariableStep(),
            "DBInput"      => new DBInputStep(),
            "DBOutput"     => new DBOutputStep(),
            "TableCompare" => new TableCompareStep(),
            "Switch"       => new SwitchStep(),
            _              => null
        };

        // ==================== FIND ====================

        /// <summary>
        /// キャンバスから query にマッチするステップを検索して選択状態にする。
        /// マッチ対象: ステップ名 / 接続設定名 / Settings の値 (SQL / FieldName / Cases など) / TypeLabel。
        /// </summary>
        public int FindInPipeline(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (var s in Steps) s.IsSelected = false;
                SelectedStep = null;
                return 0;
            }
            int hits = 0;
            StepNodeViewModel? firstHit = null;
            foreach (var s in Steps)
            {
                bool match = MatchesSearch(s, query);
                s.IsSelected = match;
                if (match)
                {
                    hits++;
                    firstHit ??= s;
                }
            }
            SelectedStep = firstHit;
            return hits;
        }

        private static bool MatchesSearch(StepNodeViewModel vm, string query)
        {
            var q = query;
            if (Contains(vm.Step.Name, q)) return true;
            if (Contains(vm.TypeLabel,   q)) return true;
            if (Contains(vm.ConnectionLabel, q)) return true;
            foreach (var kv in vm.Step.Settings)
            {
                var s = kv.Value?.ToString();
                if (Contains(s, q)) return true;
            }
            return false;
        }

        private static bool Contains(string? haystack, string needle)
            => !string.IsNullOrEmpty(haystack)
               && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        // ==================== KEYBOARD MOVE ====================

        /// <summary>選択中のステップを (dx, dy) ピクセル動かす。1 つの Undo コマンドにまとめる。</summary>
        public void MoveSelectedSteps(double dx, double dy)
        {
            var targets = GetSelectedSteps();
            if (targets.Count == 0) return;

            // 開始位置を覚えて Undo 用にする
            var snapshot = targets.Select(t => (Vm: t, X: t.X, Y: t.Y)).ToList();

            UndoManager.Execute(
                $"キー移動: {targets.Count}件",
                doAction: () =>
                {
                    foreach (var (vm, x, y) in snapshot)
                    {
                        vm.X = Math.Max(0, x + dx);
                        vm.Y = Math.Max(0, y + dy);
                    }
                },
                undoAction: () =>
                {
                    foreach (var (vm, x, y) in snapshot)
                    {
                        vm.X = x;
                        vm.Y = y;
                    }
                });
            MarkModified();
        }

        public void DeleteConnection(ConnectionViewModel connVm)
        {
            if (!Connections.Contains(connVm)) return;
            var conn = connVm.Connection;
            var srcVm = connVm.Source;
            var tgtVm = connVm.Target;

            UndoManager.Execute(
                $"接続削除: {srcVm.DisplayName} → {tgtVm.DisplayName}",
                doAction: () =>
                {
                    Connections.Remove(connVm);
                    CurrentPipeline.Connections.Remove(conn);
                    srcVm.NotifyConnectionChanged();
                    tgtVm.NotifyConnectionChanged();
                },
                undoAction: () =>
                {
                    if (!CurrentPipeline.Connections.Contains(conn)) CurrentPipeline.Connections.Add(conn);
                    if (!Connections.Contains(connVm)) Connections.Add(connVm);
                    srcVm.NotifyConnectionChanged();
                    tgtVm.NotifyConnectionChanged();
                });

            AddLog($"接続削除: {srcVm.DisplayName} → {tgtVm.DisplayName}");
            IsModified = true;
        }

        /// <summary>
        /// 全ノードの接続設定名ラベル (StepNodeViewModel.ConnectionLabel) を再評価させる。
        /// ConnectionManagerDialog で接続名を変更した直後など、ConnectionRegistry の
        /// 状態が変わったときに呼ぶ。
        /// </summary>
        public void RefreshAllConnectionLabels()
        {
            foreach (var stepVm in Steps)
                stepVm.NotifyConnectionChanged();
        }
    }
}
