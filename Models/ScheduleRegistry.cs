using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SampleELT.Models
{
    public class ScheduleRegistry
    {
        private static readonly Lazy<ScheduleRegistry> _lazy = new(() => new ScheduleRegistry());
        public static ScheduleRegistry Instance => _lazy.Value;

        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "schedules.json");

        public List<ScheduleEntry> Schedules { get; private set; } = new();

        private ScheduleRegistry() { }

        public void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                var json = File.ReadAllText(FilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                Schedules = JsonSerializer.Deserialize<List<ScheduleEntry>>(json, options) ?? new();
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Schedules, options));
            }
            catch { }
        }

        /// <summary>
        /// 直近の実行予定時刻（asOf 以前）を返す。インターバル型でまだ期限が来ていない場合は null。
        /// Tick ハンドラはこの時刻と LastRunTime を比較して未実行かどうかを判定する。
        /// </summary>
        public DateTime? CalcLastDueTime(ScheduleEntry entry, DateTime? asOf = null)
        {
            var now = asOf ?? DateTime.Now;
            return entry.Type switch
            {
                ScheduleType.Daily    => CalcLastDailyDue(entry, now),
                ScheduleType.Weekly   => CalcLastWeeklyDue(entry, now),
                ScheduleType.Hourly   => CalcLastHourlyDue(entry, now),
                ScheduleType.Interval => CalcLastIntervalDue(entry, now),
                _ => null
            };
        }

        /// <summary>
        /// 次回実行時刻を計算する（UI 表示用）。
        /// </summary>
        public DateTime? CalcNextRunTime(ScheduleEntry entry, DateTime? from = null)
        {
            var now = from ?? DateTime.Now;
            return entry.Type switch
            {
                ScheduleType.Daily    => CalcNextDaily(entry, now),
                ScheduleType.Weekly   => CalcNextWeekly(entry, now),
                ScheduleType.Hourly   => CalcNextHourly(entry, now),
                ScheduleType.Interval => entry.LastRunTime.HasValue
                    ? entry.LastRunTime.Value.AddMinutes(entry.IntervalMinutes)
                    : now,
                _ => null
            };
        }

        // ---- CalcLastDueTime helpers ----

        private static DateTime CalcLastDailyDue(ScheduleEntry e, DateTime now)
        {
            var candidate = now.Date.AddHours(e.TimeHour).AddMinutes(e.TimeMinute);
            return candidate <= now ? candidate : candidate.AddDays(-1);
        }

        private static DateTime CalcLastWeeklyDue(ScheduleEntry e, DateTime now)
        {
            int daysPast = ((int)now.DayOfWeek - (int)e.WeekDay + 7) % 7;
            var candidate = now.Date.AddDays(-daysPast).AddHours(e.TimeHour).AddMinutes(e.TimeMinute);
            if (candidate > now) candidate = candidate.AddDays(-7);
            return candidate;
        }

        private static DateTime CalcLastHourlyDue(ScheduleEntry e, DateTime now)
        {
            var candidate = now.Date.AddHours(now.Hour).AddMinutes(e.HourlyMinute);
            return candidate <= now ? candidate : candidate.AddHours(-1);
        }

        private static DateTime? CalcLastIntervalDue(ScheduleEntry e, DateTime now)
        {
            if (!e.LastRunTime.HasValue) return now; // 未実行なら即実行
            var due = e.LastRunTime.Value.AddMinutes(e.IntervalMinutes);
            return due <= now ? due : (DateTime?)null; // まだ期限でなければ null
        }

        // ---- CalcNextRunTime helpers ----

        private static DateTime CalcNextDaily(ScheduleEntry e, DateTime now)
        {
            var candidate = now.Date.AddHours(e.TimeHour).AddMinutes(e.TimeMinute);
            return candidate > now ? candidate : candidate.AddDays(1);
        }

        private static DateTime CalcNextWeekly(ScheduleEntry e, DateTime now)
        {
            int daysUntil = ((int)e.WeekDay - (int)now.DayOfWeek + 7) % 7;
            var candidate = now.Date.AddDays(daysUntil).AddHours(e.TimeHour).AddMinutes(e.TimeMinute);
            if (candidate <= now) candidate = candidate.AddDays(7);
            return candidate;
        }

        private static DateTime CalcNextHourly(ScheduleEntry e, DateTime now)
        {
            var candidate = now.Date.AddHours(now.Hour).AddMinutes(e.HourlyMinute);
            return candidate > now ? candidate : candidate.AddHours(1);
        }
    }
}
