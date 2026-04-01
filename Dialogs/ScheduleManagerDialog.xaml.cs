using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SampleELT.Models;

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

            // 追加したアイテムを選択
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
            PipelineFileTextBox.Text = entry.PipelineFilePath;
            EnabledCheckBox.IsChecked = entry.IsEnabled;

            // スケジュール種類
            TypeComboBox.SelectedIndex = entry.Type switch
            {
                ScheduleType.Daily    => 0,
                ScheduleType.Weekly   => 1,
                ScheduleType.Hourly   => 2,
                ScheduleType.Interval => 3,
                _                     => 0
            };

            HourTextBox.Text = entry.TimeHour.ToString();
            MinuteTextBox.Text = entry.TimeMinute.ToString("D2");
            WeekDayComboBox.SelectedIndex = (int)entry.WeekDay == 0 ? 6 : (int)entry.WeekDay - 1;
            HourlyMinuteTextBox.Text = entry.HourlyMinute.ToString("D2");
            IntervalTextBox.Text = entry.IntervalMinutes.ToString();

            // 最終実行情報
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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null) return;

            _current.Name             = NameTextBox.Text.Trim();
            _current.PipelineFilePath = PipelineFileTextBox.Text.Trim();
            _current.IsEnabled        = EnabledCheckBox.IsChecked == true;
            _current.Type = TypeComboBox.SelectedIndex switch
            {
                1 => ScheduleType.Weekly,
                2 => ScheduleType.Hourly,
                3 => ScheduleType.Interval,
                _ => ScheduleType.Daily
            };

            if (int.TryParse(HourTextBox.Text, out int h) && h >= 0 && h <= 23)
                _current.TimeHour = h;
            if (int.TryParse(MinuteTextBox.Text, out int m) && m >= 0 && m <= 59)
                _current.TimeMinute = m;

            // 曜日: 月=0...日=6 → DayOfWeek 月=1...日=0
            _current.WeekDay = WeekDayComboBox.SelectedIndex switch
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
                _current.HourlyMinute = hm;
            if (int.TryParse(IntervalTextBox.Text, out int iv) && iv >= 1)
                _current.IntervalMinutes = iv;

            ScheduleRegistry.Instance.Save();
            RefreshList();

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
    }
}
