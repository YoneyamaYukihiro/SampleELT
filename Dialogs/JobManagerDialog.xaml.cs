using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
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

        public JobManagerDialog()
        {
            InitializeComponent();
            // 起動直後は空: ユーザーが「新規」または「開く」を押すまで編集領域は無効
            DisableEditPanels();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
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
                    : Path.GetFileNameWithoutExtension(_currentJob.FilePath)
            };
            if (dialog.ShowDialog() != true) return;

            SaveCurrentJobToFile(dialog.FileName);
        }

        private void SaveCurrentJobToFile(string filePath)
        {
            if (_currentJob == null) return;

            // フォームの内容を反映してから書き出し
            _currentJob.Name = NameTextBox.Text.Trim();
            _currentJob.IsEnabled = EnabledCheckBox.IsChecked == true;

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
            EnabledCheckBox.IsChecked = job.IsEnabled;

            if (job.LastRunTime.HasValue)
            {
                var status = job.LastRunSuccess == true ? "成功" : "失敗";
                LastRunTextBlock.Text =
                    $"{job.LastRunTime.Value:yyyy/MM/dd HH:mm:ss}  [{status}]\n{job.LastRunMessage}";
            }
            else
            {
                LastRunTextBlock.Text = "（未実行）";
            }

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
