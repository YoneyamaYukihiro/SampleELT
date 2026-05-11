using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SampleELT.Engine;
using SampleELT.Models;

namespace SampleELT.Dialogs
{
    // ListView 表示用ラッパー
    internal class JobStepViewModel
    {
        public JobStep Source { get; }
        public string DisplayOrder => (Source.Order + 1).ToString();
        public string Name => Source.Name;
        public string PipelineFilePath => Source.PipelineFilePath;
        public string ContinueOnErrorLabel => Source.ContinueOnError ? "続行" : "中断";

        public JobStepViewModel(JobStep source) => Source = source;
    }

    /// <summary>
    /// ジョブ管理ダイアログ。一度に 1 ジョブを編集する。
    /// ジョブ本体は <c>.job.json</c> ファイルとして保存される（履歴／レジストリは持たない）。
    /// </summary>
    public partial class JobManagerDialog : Window
    {
        private Job? _currentJob;
        private JobStep? _currentStep;
        private bool _isJobDirty;

        private readonly IProgress<string>? _externalLogger;
        private CancellationTokenSource? _runCts;
        private bool _isRunning;

        public JobManagerDialog() : this(null) { }

        public JobManagerDialog(IProgress<string>? externalLogger)
        {
            _externalLogger = externalLogger;
            InitializeComponent();
            // 起動直後は空: ユーザーが「新規」または「開く」を押すまで編集領域は無効
            DisableEditPanels();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isRunning)
            {
                MessageBox.Show("ジョブ実行中はダイアログを閉じられません。停止してから閉じてください。",
                    "実行中", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
                return;
            }
            if (!ConfirmDiscardJobChanges())
                e.Cancel = true;
            base.OnClosing(e);
        }

        private void MarkJobDirty()
        {
            if (_currentJob == null) return;
            _isJobDirty = true;
            var name = string.IsNullOrWhiteSpace(_currentJob.Name) ? "ジョブ" : _currentJob.Name;
            Title = $"ジョブ管理 - {name} *";
        }

        private void ClearJobDirty()
        {
            _isJobDirty = false;
            if (_currentJob != null)
            {
                var name = string.IsNullOrWhiteSpace(_currentJob.Name) ? "ジョブ" : _currentJob.Name;
                Title = $"ジョブ管理 - {name}";
            }
            else
            {
                Title = "ジョブ管理";
            }
        }

        private bool ConfirmDiscardJobChanges()
        {
            if (!_isJobDirty) return true;
            var result = MessageBox.Show(
                $"ジョブ「{_currentJob?.Name}」にファイルへ未保存の変更があります。\n変更を破棄して続行しますか？",
                "未保存の変更",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            return result == MessageBoxResult.Yes;
        }

        private void DisableEditPanels()
        {
            _currentJob = null;
            _currentStep = null;
            EditPanel.IsEnabled = false;
            StepsPanel.IsEnabled = false;
            StepEditPanel.IsEnabled = false;
            ClearJobDirty();
        }

        // ==================== 新規 / 開く ====================

        private void NewJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardJobChanges()) return;

            // メモリ上で新規作成。保存はユーザーが「💾 保存」または「📋 名前を付けて保存」を押した時。
            var job = new Job { Name = "新しいジョブ" };
            _currentJob = job;
            LoadJobToForm(job);
            EditPanel.IsEnabled = true;
            StepsPanel.IsEnabled = true;
            // 新規未保存はそのまま dirty として扱う
            MarkJobDirty();
        }

        private void OpenJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardJobChanges()) return;

            var dialog = new OpenFileDialog
            {
                Title = "ジョブファイルを開く",
                Filter = "Job files (*.job.json)|*.job.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var job = JobLoader.LoadFromFile(dialog.FileName);
                _currentJob = job;
                LoadJobToForm(job);
                EditPanel.IsEnabled = true;
                StepsPanel.IsEnabled = true;
                ClearJobDirty();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルを開けませんでした:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 保存 ====================

        private void SaveJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null) return;
            if (string.IsNullOrEmpty(_currentJob.FilePath))
            {
                // 未保存の新規ジョブは SaveAs にフォールバック
                SaveAsJobFileButton_Click(sender, e);
                return;
            }
            SaveCurrentJobToFile(_currentJob.FilePath);
        }

        private void SaveAsJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "ジョブファイルに保存",
                Filter = "Job files (*.job.json)|*.job.json|JSON files (*.json)|*.json",
                FileName = string.IsNullOrEmpty(_currentJob.FilePath)
                    ? _currentJob.Name
                    : StripJobJsonExtension(Path.GetFileName(_currentJob.FilePath))
            };
            if (dialog.ShowDialog() != true) return;

            // 名前を付けて保存ではジョブ名をファイル名に合わせる (Pipeline と同じ挙動)。
            // タイトルや FilePathLabel が新ファイルの内容を反映するように NameTextBox を先に更新する。
            var newPath = EnsureJobJsonExtension(dialog.FileName);
            NameTextBox.Text = StripJobJsonExtension(Path.GetFileName(newPath));
            SaveCurrentJobToFile(newPath);
        }

        /// <summary>"foo.job.json" → "foo"。Windows の SaveFileDialog の FileName は単一拡張子しか扱えないため自前で剥がす。</summary>
        private static string StripJobJsonExtension(string fileName)
        {
            if (fileName.EndsWith(".job.json", StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - ".job.json".Length);
            if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return fileName.Substring(0, fileName.Length - ".json".Length);
            return Path.GetFileNameWithoutExtension(fileName);
        }

        /// <summary>SaveFileDialog の返却値を必ず ".job.json" 終端に正規化する。</summary>
        private static string EnsureJobJsonExtension(string filePath)
        {
            if (filePath.EndsWith(".job.json", StringComparison.OrdinalIgnoreCase))
                return filePath;
            if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return filePath.Substring(0, filePath.Length - ".json".Length) + ".job.json";
            return filePath + ".job.json";
        }

        private void SaveCurrentJobToFile(string filePath)
        {
            if (_currentJob == null) return;

            // フォームの内容を反映してから書き出し
            _currentJob.Name = NameTextBox.Text.Trim();
            _currentJob.LogMode = LogModeComboBox.SelectedValue is LogMode mode ? mode : LogMode.OnError;

            try
            {
                JobLoader.SaveToFile(_currentJob, filePath);
                UpdateFilePathLabel(_currentJob);
                ClearJobDirty();
                ShowStatus($"保存しました: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== ジョブフォーム ====================

        private void LoadJobToForm(Job job)
        {
            NameTextBox.Text = job.Name;
            LogModeComboBox.SelectedValue = job.LogMode;

            RefreshStepsList(job);
            _currentStep = null;
            StepEditPanel.IsEnabled = false;
            UpdateFilePathLabel(job);
        }

        private void JobForm_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentJob != null) MarkJobDirty();
        }

        // ==================== パイプラインステップ操作 ====================

        private void RefreshStepsList(Job job)
        {
            StepsListView.Items.Clear();
            foreach (var step in job.Steps.OrderBy(s => s.Order))
            {
                StepsListView.Items.Add(new JobStepViewModel(step));
            }
        }

        private void StepsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StepsListView.SelectedItem is JobStepViewModel vm)
            {
                _currentStep = vm.Source;
                StepNameTextBox.Text = vm.Source.Name;
                StepFileTextBox.Text = vm.Source.PipelineFilePath;
                ContinueOnErrorCheckBox.IsChecked = vm.Source.ContinueOnError;
                StepEditPanel.IsEnabled = true;
            }
            else
            {
                _currentStep = null;
                StepEditPanel.IsEnabled = false;
            }
        }

        private void AddStepButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null) return;

            var step = new JobStep
            {
                Order = _currentJob.Steps.Count,
                Name = $"パイプライン {_currentJob.Steps.Count + 1}"
            };
            _currentJob.Steps.Add(step);
            MarkJobDirty();
            RefreshStepsList(_currentJob);

            foreach (JobStepViewModel vm in StepsListView.Items)
            {
                if (vm.Source == step)
                {
                    StepsListView.SelectedItem = vm;
                    break;
                }
            }
        }

        private void DeleteStepButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null || _currentStep == null) return;

            _currentJob.Steps.Remove(_currentStep);
            ReorderSteps(_currentJob);
            _currentStep = null;
            StepEditPanel.IsEnabled = false;
            MarkJobDirty();
            RefreshStepsList(_currentJob);
        }

        private void ApplyStepButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == null) return;

            _currentStep.Name = StepNameTextBox.Text.Trim();
            _currentStep.PipelineFilePath = StepFileTextBox.Text.Trim();
            _currentStep.ContinueOnError = ContinueOnErrorCheckBox.IsChecked == true;

            if (_currentJob != null)
            {
                MarkJobDirty();
                RefreshStepsList(_currentJob);
            }

            foreach (JobStepViewModel vm in StepsListView.Items)
            {
                if (vm.Source == _currentStep)
                {
                    StepsListView.SelectedItem = vm;
                    break;
                }
            }
        }

        private void StepBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "パイプラインファイルを選択",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                StepFileTextBox.Text = dialog.FileName;
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null || _currentStep == null) return;
            if (_currentStep.Order <= 0) return;

            var prev = _currentJob.Steps.FirstOrDefault(s => s.Order == _currentStep.Order - 1);
            if (prev != null)
            {
                prev.Order++;
                _currentStep.Order--;
            }
            MarkJobDirty();
            RefreshStepsList(_currentJob);
            RestoreStepSelection();
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null || _currentStep == null) return;
            if (_currentStep.Order >= _currentJob.Steps.Count - 1) return;

            var next = _currentJob.Steps.FirstOrDefault(s => s.Order == _currentStep.Order + 1);
            if (next != null)
            {
                next.Order--;
                _currentStep.Order++;
            }
            MarkJobDirty();
            RefreshStepsList(_currentJob);
            RestoreStepSelection();
        }

        private void RestoreStepSelection()
        {
            foreach (JobStepViewModel vm in StepsListView.Items)
            {
                if (vm.Source == _currentStep)
                {
                    StepsListView.SelectedItem = vm;
                    break;
                }
            }
        }

        private static void ReorderSteps(Job job)
        {
            var sorted = job.Steps.OrderBy(s => s.Order).ToList();
            for (int i = 0; i < sorted.Count; i++)
                sorted[i].Order = i;
        }

        // ==================== 手動実行 ====================

        private async void RunJobButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _currentJob == null) return;

            // フォームをジョブに反映してから保存（dirty なら確認、未保存ならファイル保存ダイアログ）
            _currentJob.Name = NameTextBox.Text.Trim();
            _currentJob.LogMode = LogModeComboBox.SelectedValue is LogMode mode ? mode : LogMode.OnError;

            if (_isJobDirty || string.IsNullOrEmpty(_currentJob.FilePath))
            {
                var ans = MessageBox.Show(
                    "ジョブに未保存の変更があります。保存してから実行しますか？\n（はい=保存して実行 / いいえ=保存せず実行 / キャンセル=中止）",
                    "未保存の変更",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (ans == MessageBoxResult.Cancel) return;
                if (ans == MessageBoxResult.Yes)
                {
                    SaveJobFileButton_Click(sender, e);
                    if (_isJobDirty) return; // 保存ダイアログがキャンセルされた場合
                }
            }

            _runCts = new CancellationTokenSource();
            SetRunningState(true);
            _externalLogger?.Report($"===== ジョブ実行開始: {_currentJob.Name} =====");

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    _externalLogger?.Report(msg);
                    StatusTextBlock.Text = msg;
                    StatusTextBlock.Visibility = Visibility.Visible;
                });

                var executor = new JobExecutor();
                await executor.ExecuteAsync(_currentJob, progress, _runCts.Token);

                _externalLogger?.Report($"===== ジョブ実行完了: {_currentJob.Name} =====");
                ShowStatus("実行完了");
            }
            catch (OperationCanceledException)
            {
                _externalLogger?.Report($"===== ジョブ実行キャンセル: {_currentJob.Name} =====");
                ShowStatus("キャンセルされました");
            }
            catch (Exception ex)
            {
                _externalLogger?.Report($"===== ジョブ実行エラー [{_currentJob.Name}]: {ex.Message} =====");
                ShowStatus($"エラー: {ex.Message}");
            }
            finally
            {
                _runCts?.Dispose();
                _runCts = null;
                SetRunningState(false);
            }
        }

        private void StopJobButton_Click(object sender, RoutedEventArgs e)
        {
            _runCts?.Cancel();
            StatusTextBlock.Text = "停止中...";
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            RunJobButton.IsEnabled  = !running;
            StopJobButton.IsEnabled = running;
            NewJobFileButton.IsEnabled    = !running;
            OpenJobFileButton.IsEnabled   = !running;
            SaveJobFileButton.IsEnabled   = !running;
            SaveAsJobFileButton.IsEnabled = !running;
            EditPanel.IsEnabled  = !running && _currentJob != null;
            StepsPanel.IsEnabled = !running && _currentJob != null;
            StepEditPanel.IsEnabled = !running && _currentStep != null;
        }

        // ==================== 補助 ====================

        private void ShowStatus(string text)
        {
            StatusTextBlock.Text = text;
            StatusTextBlock.Visibility = Visibility.Visible;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (_, _) =>
            {
                StatusTextBlock.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }

        private void UpdateFilePathLabel(Job? job)
        {
            FilePathLabel.Text = string.IsNullOrEmpty(job?.FilePath)
                ? "（ファイル未保存）"
                : job.FilePath;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
