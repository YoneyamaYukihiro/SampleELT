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

    public partial class JobManagerDialog : Window
    {
        private Job? _currentJob;
        private JobStep? _currentStep;
        private bool _isJobDirty;

        public JobManagerDialog()
        {
            InitializeComponent();
            JobRegistry.Instance.Load();
            RefreshJobList();
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
            // ジョブ名にアスタリスクを表示
            var name = string.IsNullOrWhiteSpace(_currentJob.Name) ? "ジョブ" : _currentJob.Name;
            Title = $"ジョブ管理 - {name} *";
        }

        private void ClearJobDirty()
        {
            _isJobDirty = false;
            Title = "ジョブ管理";
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

        // ==================== ジョブ一覧操作 ====================

        private void RefreshJobList()
        {
            JobListBox.Items.Clear();
            foreach (var job in JobRegistry.Instance.Jobs)
            {
                JobListBox.Items.Add(new ListBoxItem
                {
                    Content = $"{(job.IsEnabled ? "●" : "○")} {job.Name}",
                    Tag = job
                });
            }
        }

        private void JobListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (JobListBox.SelectedItem is ListBoxItem item && item.Tag is Job job)
            {
                // 切替前に未保存チェック
                if (!ConfirmDiscardJobChanges())
                {
                    // 選択を元に戻す（イベント再発火を避けるため e.RemovedItems を利用）
                    if (e.RemovedItems.Count > 0)
                    {
                        JobListBox.SelectionChanged -= JobListBox_SelectionChanged;
                        JobListBox.SelectedItem = e.RemovedItems[0];
                        JobListBox.SelectionChanged += JobListBox_SelectionChanged;
                    }
                    return;
                }

                _currentJob = job;
                LoadJobToForm(job);
                EditPanel.IsEnabled = true;
                StepsPanel.IsEnabled = true;
                ClearJobDirty();
            }
            else
            {
                _currentJob = null;
                EditPanel.IsEnabled = false;
                StepsPanel.IsEnabled = false;
                StepEditPanel.IsEnabled = false;
                ClearJobDirty();
            }
        }

        private void AddJobButton_Click(object sender, RoutedEventArgs e)
        {
            var job = new Job { Name = "新しいジョブ" };
            JobRegistry.Instance.Jobs.Add(job);
            JobRegistry.Instance.Save();
            RefreshJobList();

            for (int i = 0; i < JobListBox.Items.Count; i++)
            {
                if (JobListBox.Items[i] is ListBoxItem li && li.Tag == job)
                {
                    JobListBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private void DeleteJobButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null) return;

            var result = MessageBox.Show(
                $"ジョブ「{_currentJob.Name}」を削除しますか？",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            JobRegistry.Instance.Jobs.Remove(_currentJob);
            JobRegistry.Instance.Save();
            _currentJob = null;
            _currentStep = null;
            EditPanel.IsEnabled = false;
            StepsPanel.IsEnabled = false;
            StepEditPanel.IsEnabled = false;
            RefreshJobList();
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

        private void SaveJobButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null) return;

            _currentJob.Name = NameTextBox.Text.Trim();
            _currentJob.IsEnabled = EnabledCheckBox.IsChecked == true;

            JobRegistry.Instance.Save();
            RefreshJobList();

            StatusTextBlock.Text = "保存しました";
            StatusTextBlock.Visibility = Visibility.Visible;
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) =>
            {
                StatusTextBlock.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
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

            // 追加したステップを選択
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

            // 編集したステップを再選択
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

        // ==================== ファイル操作 ====================

        private void NewJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            var job = new Job { Name = "新しいジョブ" };
            JobRegistry.Instance.Jobs.Add(job);
            JobRegistry.Instance.Save();
            RefreshJobList();

            for (int i = 0; i < JobListBox.Items.Count; i++)
            {
                if (JobListBox.Items[i] is ListBoxItem li && li.Tag == job)
                {
                    JobListBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private void OpenJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "ジョブファイルを開く",
                Filter = "Job files (*.job.json)|*.job.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var job = JobLoader.LoadFromFile(dialog.FileName);

                // 同じパスのジョブが既にあれば差し替え
                var existing = JobRegistry.Instance.Jobs.Find(j => j.FilePath == dialog.FileName);
                if (existing != null)
                    JobRegistry.Instance.Jobs.Remove(existing);

                JobRegistry.Instance.Jobs.Add(job);
                JobRegistry.Instance.Save();
                RefreshJobList();

                // 開いたジョブを選択
                for (int i = 0; i < JobListBox.Items.Count; i++)
                {
                    if (JobListBox.Items[i] is ListBoxItem li && li.Tag == job)
                    {
                        JobListBox.SelectedIndex = i;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルを開けませんでした:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveJobFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentJob == null) return;

            if (string.IsNullOrEmpty(_currentJob.FilePath))
            {
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

            // フォームの内容を先に反映
            _currentJob.Name = NameTextBox.Text.Trim();
            _currentJob.IsEnabled = EnabledCheckBox.IsChecked == true;

            try
            {
                JobLoader.SaveToFile(_currentJob, filePath);
                JobRegistry.Instance.Save();
                UpdateFilePathLabel(_currentJob);
                RefreshJobList();
                ClearJobDirty();

                StatusTextBlock.Text = $"保存しました: {Path.GetFileName(filePath)}";
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
            catch (Exception ex)
            {
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
