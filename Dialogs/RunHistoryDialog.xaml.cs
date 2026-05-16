using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BreezeFlow.Models;
using BreezeFlow.Services;

namespace BreezeFlow.Dialogs
{
    public partial class RunHistoryDialog : Window
    {
        /// <summary>
        /// 「ファイルを開く」が押されたときにメインウィンドウへ通知するためのパス。
        /// ダイアログを閉じた後、呼び出し側が読み込めるよう <see cref="SelectedPipelinePath"/> を参照する。
        /// </summary>
        public string? SelectedPipelinePath { get; private set; }

        private readonly RunHistoryStore? _store;

        public RunHistoryDialog()
        {
            InitializeComponent();
            _store = RunHistoryStore.Instance;

            // 既定: 過去 7 日
            FromDatePicker.SelectedDate = DateTime.Today.AddDays(-7);
            ToDatePicker.SelectedDate = DateTime.Today;

            Loaded += (_, _) => Refresh();
        }

        // ==================== フィルタ ====================

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            // IsLoaded 前のチェックボックス初期化イベントは無視
            if (!IsLoaded) return;
            Refresh();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (_store == null)
            {
                RunsGrid.ItemsSource = Array.Empty<RunRow>();
                ResultCountText.Text = "実行履歴が無効です (runs.db を初期化できませんでした)";
                return;
            }

            var filter = new RunSearchFilter
            {
                From = FromDatePicker.SelectedDate,
                To   = ToDatePicker.SelectedDate?.AddDays(1).AddSeconds(-1), // その日の 23:59:59 まで
                PipelineNameLike = string.IsNullOrWhiteSpace(SearchBox.Text) ? null : SearchBox.Text.Trim(),
                Statuses = CollectStatuses(),
                Triggers = CollectTriggers(),
                Limit = 500
            };

            var rows = _store.Search(filter).Select(r => new RunRow(r)).ToList();
            RunsGrid.ItemsSource = rows;
            ResultCountText.Text = $"{rows.Count} 件";
        }

        private List<RunStatus> CollectStatuses()
        {
            var list = new List<RunStatus>();
            if (StatusSuccess.IsChecked   == true) list.Add(RunStatus.Success);
            if (StatusFailed.IsChecked    == true) list.Add(RunStatus.Failed);
            if (StatusCancelled.IsChecked == true) list.Add(RunStatus.Cancelled);
            if (StatusRunning.IsChecked   == true) list.Add(RunStatus.Running);
            return list;
        }

        private List<string> CollectTriggers()
        {
            var list = new List<string>();
            if (TrigManual.IsChecked   == true) list.Add("manual");
            if (TrigSchedule.IsChecked == true) list.Add("schedule");
            if (TrigCli.IsChecked      == true) list.Add("cli");
            if (TrigJob.IsChecked      == true) list.Add("job");
            return list;
        }

        // ==================== 選択変更 → 詳細表示 ====================

        private void RunsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = RunsGrid.SelectedItem as RunRow;
            if (row == null || _store == null)
            {
                DetailTitleText.Text = "(選択なし)";
                DetailSubText.Text = "";
                DetailErrorText.Text = "";
                StepsGrid.ItemsSource = Array.Empty<StepRow>();
                LogsItemsControl.ItemsSource = Array.Empty<LogRow>();
                OpenFileButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
                return;
            }

            var detail = _store.GetDetail(row.Record.Id);
            if (detail == null) return;

            DetailTitleText.Text = row.DisplayName;
            DetailSubText.Text = BuildSubText(detail.Run);
            DetailErrorText.Text = string.IsNullOrEmpty(detail.Run.ErrorMessage)
                ? ""
                : "エラー: " + detail.Run.ErrorMessage;

            StepsGrid.ItemsSource = detail.Steps.Select(s => new StepRow(s)).ToList();
            LogsItemsControl.ItemsSource = detail.Logs.Select(l => new LogRow(l)).ToList();

            OpenFileButton.IsEnabled = !string.IsNullOrEmpty(detail.Run.PipelinePath) && File.Exists(detail.Run.PipelinePath);
            DeleteButton.IsEnabled = true;
        }

        private static string BuildSubText(RunRecord r)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(r.PipelinePath)) parts.Add($"📄 {r.PipelinePath}");
            if (!string.IsNullOrEmpty(r.JobName))      parts.Add($"📦 ジョブ: {r.JobName}");
            parts.Add($"トリガ: {RunRow.TriggerLabel(r.Trigger)}");
            if (r.DurationMs.HasValue) parts.Add($"所要 {FormatDuration(r.DurationMs.Value)}");
            if (r.TotalRows.HasValue)  parts.Add($"行数 {r.TotalRows.Value:N0}");
            return string.Join("  ·  ", parts);
        }

        // ==================== ボタン ====================

        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (RunsGrid.SelectedItem is RunRow row && !string.IsNullOrEmpty(row.Record.PipelinePath))
            {
                SelectedPipelinePath = row.Record.PipelinePath;
                DialogResult = true;
                Close();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_store == null) return;
            if (RunsGrid.SelectedItem is not RunRow row) return;

            var ans = MessageBox.Show(
                $"この実行履歴を削除します。よろしいですか？\n\n{row.DisplayName} ({row.StartedAtDisplay})",
                "削除確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes) return;

            _store.DeleteRun(row.Record.Id);
            Refresh();
        }

        private void VacuumButton_Click(object sender, RoutedEventArgs e)
        {
            if (_store == null) return;
            _store.Vacuum();
            MessageBox.Show("runs.db を最適化しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ==================== 表示用フォーマッタ ====================

        internal static string FormatDuration(long ms)
        {
            if (ms < 1000) return $"{ms}ms";
            if (ms < 60_000) return $"{ms / 1000.0:0.0}s";
            var min = ms / 60_000;
            var sec = (ms % 60_000) / 1000;
            return $"{min}m{sec:00}s";
        }

        // ==================== 行 ViewModel (DataGrid バインディング用) ====================

        public class RunRow
        {
            public RunRecord Record { get; }
            public RunRow(RunRecord r) { Record = r; }

            public string StatusIcon => Record.Status switch
            {
                RunStatus.Success   => "✓",
                RunStatus.Failed    => "✗",
                RunStatus.Cancelled => "■",
                RunStatus.Running   => "▶",
                _                   => "?"
            };

            public string StartedAtDisplay => Record.StartedAt.ToString("MM-dd HH:mm:ss");

            public string DurationDisplay => Record.DurationMs.HasValue
                ? FormatDuration(Record.DurationMs.Value) : "—";

            public string TriggerDisplay => TriggerLabel(Record.Trigger);

            public static string TriggerLabel(string trigger) => trigger switch
            {
                "manual"   => "手動",
                "schedule" => "スケジュール",
                "cli"      => "CLI",
                "job"      => "ジョブ",
                _          => trigger
            };

            public string DisplayName
            {
                get
                {
                    var name = Record.PipelineName ?? Path.GetFileNameWithoutExtension(Record.PipelinePath ?? "") ?? "";
                    if (!string.IsNullOrEmpty(Record.JobName))
                        return $"{name}  « {Record.JobName} »";
                    return name;
                }
            }

            public string? TotalRows => Record.TotalRows.HasValue ? Record.TotalRows.Value.ToString("N0") : "—";
        }

        public class StepRow
        {
            public RunStepRecord Record { get; }
            public StepRow(RunStepRecord r) { Record = r; }

            public string StatusIcon => Record.Status switch
            {
                RunStatus.Success   => "✓",
                RunStatus.Failed    => "✗",
                RunStatus.Cancelled => "■",
                RunStatus.Running   => "▶",
                _                   => "?"
            };

            public int StepOrder => Record.StepOrder;
            public string StepName => Record.StepName;
            public string DurationDisplay => Record.DurationMs.HasValue ? FormatDuration(Record.DurationMs.Value) : "—";
            public string? RowCount => Record.RowCount.HasValue ? Record.RowCount.Value.ToString("N0") : "—";
        }

        public class LogRow
        {
            public RunLogEntry Entry { get; }
            public LogRow(RunLogEntry e) { Entry = e; }

            public string Level => Entry.Level;
            public string DisplayLine => $"[{Entry.Timestamp:HH:mm:ss}] {Entry.Message}";
        }
    }
}
