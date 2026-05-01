using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SampleELT.Models;
using SampleELT.Services;

namespace SampleELT.Dialogs
{
    public partial class ScheduleManagerDialog : Window
    {
        private ScheduleEntry? _current;
        private bool _suppressEvents;

        public ScheduleManagerDialog()
        {
            InitializeComponent();
            RefreshList();
        }

        // ==================== 一覧操作 ====================

        private void RefreshList()
        {
            ScheduleListBox.Items.Clear();
            foreach (var entry in ScheduleRegistry.Instance.Schedules)
            {
                var item = new ListBoxItem
                {
                    Content = $"{(entry.IsEnabled ? "●" : "○")} {entry.Name}",
                    Tag = entry
                };
                ScheduleListBox.Items.Add(item);
            }
        }

        private void ScheduleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScheduleListBox.SelectedItem is ListBoxItem item && item.Tag is ScheduleEntry entry)
            {
                _current = entry;
                LoadEntryToForm(entry);
                EditPanel.IsEnabled = true;
            }
            else
            {
                _current = null;
                EditPanel.IsEnabled = false;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var entry = new ScheduleEntry { Name = "新しいスケジュール" };
            ScheduleRegistry.Instance.Schedules.Add(entry);
            ScheduleRegistry.Instance.Save();
            RefreshList();

            for (int i = 0; i < ScheduleListBox.Items.Count; i++)
            {
                if (ScheduleListBox.Items[i] is ListBoxItem li && li.Tag == entry)
                {
                    ScheduleListBox.SelectedIndex = i;
                    break;
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            var result = MessageBox.Show(
                $"スケジュール「{_current.Name}」を削除しますか？",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            if (_current.Mode == ScheduleMode.TaskScheduler)
                TaskSchedulerHelper.Unregister(_current);

            ScheduleRegistry.Instance.Schedules.Remove(_current);
            ScheduleRegistry.Instance.Save();
            _current = null;
            EditPanel.IsEnabled = false;
            RefreshList();
        }

        // ==================== フォーム操作 ====================

        private void LoadEntryToForm(ScheduleEntry entry)
        {
            _suppressEvents = true;

            NameTextBox.Text = entry.Name;
            EnabledCheckBox.IsChecked = entry.IsEnabled;

            // 実行対象
            var isPipeline = entry.Target != ScheduleTarget.Job;
            TargetPipelineRadio.IsChecked = isPipeline;
            TargetJobRadio.IsChecked = !isPipeline;
            UpdateTargetPanelVisibility(entry.Target);

            PipelineFileTextBox.Text = entry.PipelineFilePath;
            JobFileTextBox.Text = entry.JobFilePath;

            // スケジュール種類
            TypeComboBox.SelectedIndex = entry.Type switch
            {
                ScheduleType.Daily    => 0,
                ScheduleType.Weekly   => 1,
                ScheduleType.Hourly   => 2,
                ScheduleType.Interval => 3,
                _                     => 0
            };

            ModeInAppRadio.IsChecked        = entry.Mode != ScheduleMode.TaskScheduler;
            ModeTaskSchedulerRadio.IsChecked = entry.Mode == ScheduleMode.TaskScheduler;

            HourTextBox.Text = entry.TimeHour.ToString();
            MinuteTextBox.Text = entry.TimeMinute.ToString("D2");
            WeekDayComboBox.SelectedIndex = (int)entry.WeekDay == 0 ? 6 : (int)entry.WeekDay - 1;
            HourlyMinuteTextBox.Text = entry.HourlyMinute.ToString("D2");
            IntervalTextBox.Text = entry.IntervalMinutes.ToString();

            if (entry.LastRunTime.HasValue)
            {
                var status = entry.LastRunSuccess == true ? "成功" : "失敗";
                LastRunTextBlock.Text =
                    $"{entry.LastRunTime.Value:yyyy/MM/dd HH:mm:ss}  [{status}]\n{entry.LastRunMessage}";
            }
            else
            {
                LastRunTextBlock.Text = "（未実行）";
            }

            _suppressEvents = false;
            UpdatePanelVisibility(entry.Type);
        }

        private void TargetRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (PipelinePanel == null) return;
            var target = TargetJobRadio.IsChecked == true ? ScheduleTarget.Job : ScheduleTarget.Pipeline;
            UpdateTargetPanelVisibility(target);
        }

        private void UpdateTargetPanelVisibility(ScheduleTarget target)
        {
            if (PipelinePanel == null) return;
            PipelinePanel.Visibility = target == ScheduleTarget.Pipeline ? Visibility.Visible : Visibility.Collapsed;
            JobPanel.Visibility      = target == ScheduleTarget.Job      ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ModeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ModeInAppNote == null) return;
            var isTaskScheduler = ModeTaskSchedulerRadio.IsChecked == true;
            ModeInAppNote.Visibility        = isTaskScheduler ? Visibility.Collapsed : Visibility.Visible;
            ModeTaskSchedulerNote.Visibility = isTaskScheduler ? Visibility.Visible   : Visibility.Collapsed;
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;
            var type = TypeComboBox.SelectedIndex switch
            {
                1 => ScheduleType.Weekly,
                2 => ScheduleType.Hourly,
                3 => ScheduleType.Interval,
                _ => ScheduleType.Daily
            };
            UpdatePanelVisibility(type);
        }

        private void UpdatePanelVisibility(ScheduleType type)
        {
            TimePanel.Visibility     = type is ScheduleType.Daily or ScheduleType.Weekly
                ? Visibility.Visible : Visibility.Collapsed;
            WeekDayPanel.Visibility  = type == ScheduleType.Weekly
                ? Visibility.Visible : Visibility.Collapsed;
            HourlyPanel.Visibility   = type == ScheduleType.Hourly
                ? Visibility.Visible : Visibility.Collapsed;
            IntervalPanel.Visibility = type == ScheduleType.Interval
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title  = "パイプラインファイルを選択",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                PipelineFileTextBox.Text = dialog.FileName;
        }

        private void BrowseJobButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title  = "ジョブファイルを選択",
                Filter = "Job files (*.job.json)|*.job.json|JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                JobFileTextBox.Text = dialog.FileName;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            // RefreshList() がリスト選択を解除すると _current が null 化されるため、
            // ローカルにキャプチャしてから処理を進める
            var entry = _current;
            var prevMode = entry.Mode;

            // 重複名チェック（上書き / 別名で保存 / キャンセル）
            var desiredName = NameTextBox.Text.Trim();
            if (!TryResolveDuplicateName(entry, ref desiredName))
                return;
            if (NameTextBox.Text.Trim() != desiredName)
                NameTextBox.Text = desiredName;

            entry.Name      = desiredName;
            entry.IsEnabled = EnabledCheckBox.IsChecked == true;
            entry.Target    = TargetJobRadio.IsChecked == true ? ScheduleTarget.Job : ScheduleTarget.Pipeline;

            if (entry.Target == ScheduleTarget.Pipeline)
            {
                entry.PipelineFilePath = PipelineFileTextBox.Text.Trim();
                entry.JobFilePath = "";
            }
            else
            {
                entry.PipelineFilePath = "";
                entry.JobFilePath = JobFileTextBox.Text.Trim();
            }

            entry.Mode = ModeTaskSchedulerRadio.IsChecked == true
                         ? ScheduleMode.TaskScheduler : ScheduleMode.InApp;
            entry.Type = TypeComboBox.SelectedIndex switch
            {
                1 => ScheduleType.Weekly,
                2 => ScheduleType.Hourly,
                3 => ScheduleType.Interval,
                _ => ScheduleType.Daily
            };

            if (int.TryParse(HourTextBox.Text, out int h) && h >= 0 && h <= 23)
                entry.TimeHour = h;
            if (int.TryParse(MinuteTextBox.Text, out int m) && m >= 0 && m <= 59)
                entry.TimeMinute = m;

            entry.WeekDay = WeekDayComboBox.SelectedIndex switch
            {
                0 => DayOfWeek.Monday,
                1 => DayOfWeek.Tuesday,
                2 => DayOfWeek.Wednesday,
                3 => DayOfWeek.Thursday,
                4 => DayOfWeek.Friday,
                5 => DayOfWeek.Saturday,
                _ => DayOfWeek.Sunday
            };

            if (int.TryParse(HourlyMinuteTextBox.Text, out int hm) && hm >= 0 && hm <= 59)
                entry.HourlyMinute = hm;
            if (int.TryParse(IntervalTextBox.Text, out int iv) && iv >= 1)
                entry.IntervalMinutes = iv;

            ScheduleRegistry.Instance.Save();

            // タスクスケジューラ連携（パイプラインのみ対応）
            // schtasks.exe 起動や OS 例外でアプリが落ちないよう全体を try/catch で保護
            try
            {
                if (entry.Mode == ScheduleMode.TaskScheduler && entry.Target == ScheduleTarget.Pipeline)
                {
                    var (ok, msg) = TaskSchedulerHelper.Register(entry);
                    if (!ok)
                    {
                        MessageBox.Show(
                            $"タスクスケジューラへの登録に失敗しました:\n{msg}",
                            "登録エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        var taskName = TaskSchedulerHelper.GetTaskName(entry);
                        MessageBox.Show(
                            $"タスクスケジューラへ登録しました。\n登録名: {taskName}\n\n" +
                            "現在は「ユーザーがログオンしている時のみ実行」で登録されています。\n" +
                            "未ログオン時にも実行したい場合は、以下の手順で手動変更してください:\n\n" +
                            "  1. タスクスケジューラを開く\n" +
                            "     ([Win]+R → 「taskschd.msc」)\n" +
                            "  2. 左ペインで「タスク スケジューラ ライブラリ」→「SampleELT」フォルダを選択\n" +
                            "  3. 対象タスクを右クリック →「プロパティ」\n" +
                            "  4. 「全般」タブで\n" +
                            "     「ユーザーがログオンしているかどうかにかかわらず実行する」を選択\n" +
                            "  5. （必要に応じて）「最上位の特権で実行する」にチェック\n" +
                            "  6. [OK] → パスワード入力プロンプトでログオンユーザーのパスワードを入力",
                            "登録完了", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (entry.Mode == ScheduleMode.TaskScheduler && entry.Target == ScheduleTarget.Job)
                {
                    MessageBox.Show(
                        "ジョブはWindowsタスクスケジューラに対応していません。\nインアプリモードに切り替えてください。",
                        "非対応", MessageBoxButton.OK, MessageBoxImage.Information);
                    entry.Mode = ScheduleMode.InApp;
                    ModeInAppRadio.IsChecked = true;
                    ScheduleRegistry.Instance.Save();
                }
                else if (prevMode == ScheduleMode.TaskScheduler)
                {
                    TaskSchedulerHelper.Unregister(entry);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"タスクスケジューラの操作中にエラーが発生しました:\n{ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Register / Unregister 内で entry.LastTaskName が更新されるため再保存
            ScheduleRegistry.Instance.Save();

            // 一覧を再描画し、保存したエントリを再選択
            RefreshList();
            for (int i = 0; i < ScheduleListBox.Items.Count; i++)
            {
                if (ScheduleListBox.Items[i] is ListBoxItem li && li.Tag == entry)
                {
                    ScheduleListBox.SelectedIndex = i;
                    break;
                }
            }

            StatusTextBlock.Text       = "保存しました";
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

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // ==================== 重複名処理 ====================

        /// <summary>
        /// 他のスケジュールと名前が重複している場合に対応を確認する。
        /// 戻り値 false でキャンセル（保存中止）、true で続行可能。
        /// 上書き時は重複エントリを削除（タスクスケジューラ登録があれば解除）。
        /// 別名で保存時は desiredName を一意な名前に置き換える。
        /// </summary>
        private bool TryResolveDuplicateName(ScheduleEntry entry, ref string desiredName)
        {
            if (string.IsNullOrEmpty(desiredName)) return true;

            // ref パラメータはラムダ内で参照できないためローカルにコピー
            var nameToCheck = desiredName;
            var conflicts = ScheduleRegistry.Instance.Schedules
                .Where(s => !ReferenceEquals(s, entry)
                            && string.Equals(s.Name, nameToCheck, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (conflicts.Count == 0) return true;

            var msg =
                $"同じ名前のスケジュール「{conflicts[0].Name}」が既に存在します。\n\n" +
                "・[はい] 既存のスケジュールを上書き（削除して置き換え）\n" +
                "・[いいえ] 別名で保存（自動的に番号を追加）\n" +
                "・[キャンセル] 保存しない";

            var result = MessageBox.Show(msg, "重複したスケジュール名",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;

            if (result == MessageBoxResult.Yes)
            {
                // 上書き: 衝突したスケジュールを削除（タスク登録もあれば解除）
                foreach (var c in conflicts)
                {
                    if (c.Mode == ScheduleMode.TaskScheduler)
                    {
                        try { TaskSchedulerHelper.Unregister(c); }
                        catch { /* 失敗は無視して続行 */ }
                    }
                    ScheduleRegistry.Instance.Schedules.Remove(c);
                }
                return true;
            }

            // 別名で保存: 一意な名前を生成
            desiredName = FindUniqueName(entry, desiredName);
            return true;
        }

        private static string FindUniqueName(ScheduleEntry entry, string baseName)
        {
            for (int i = 2; i < 1000; i++)
            {
                var candidate = $"{baseName} ({i})";
                bool taken = ScheduleRegistry.Instance.Schedules.Any(s =>
                    !ReferenceEquals(s, entry)
                    && string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase));
                if (!taken) return candidate;
            }
            return $"{baseName} ({Guid.NewGuid():N})"; // フォールバック（実質到達しない）
        }
    }
}
