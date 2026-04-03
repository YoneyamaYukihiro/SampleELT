using System;

namespace SampleELT.Models
{
    public enum ScheduleMode
    {
        InApp,          // アプリ内スケジューラ（アプリ起動中のみ）
        TaskScheduler   // Windowsタスクスケジューラ（アプリ未起動でも動作）
    }

    public enum ScheduleType
    {
        Daily,    // 毎日 指定時刻
        Weekly,   // 毎週 指定曜日・時刻
        Hourly,   // 毎時 指定分
        Interval  // N分ごと
    }

    public class ScheduleEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string PipelineFilePath { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public ScheduleMode Mode { get; set; } = ScheduleMode.InApp;
        public ScheduleType Type { get; set; } = ScheduleType.Daily;

        // Daily / Weekly: time of day
        public int TimeHour { get; set; } = 9;
        public int TimeMinute { get; set; } = 0;

        // Weekly: day of week
        public DayOfWeek WeekDay { get; set; } = DayOfWeek.Monday;

        // Interval: minutes between runs
        public int IntervalMinutes { get; set; } = 60;

        // Hourly: minute within the hour
        public int HourlyMinute { get; set; } = 0;

        // Run tracking
        public DateTime? LastRunTime { get; set; }
        public bool? LastRunSuccess { get; set; }
        public string LastRunMessage { get; set; } = "";
    }
}
