using System;
using SampleELT.Models;
using Xunit;

namespace SampleELT.Tests.Models
{
    public class ScheduleRegistryTests
    {
        private static ScheduleRegistry Registry => ScheduleRegistry.Instance;

        // Helper: fixed reference time
        // 2026-04-03 10:30:00 (Friday)
        private static readonly DateTime RefTime = new DateTime(2026, 4, 3, 10, 30, 0);

        // ---- CalcLastDueTime: Daily ----

        [Fact]
        public void Daily_DueAlreadyPassedToday_ReturnsTodayDue()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Daily,
                TimeHour = 9,
                TimeMinute = 0
            };
            // 9:00 < 10:30 → today's 9:00
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 9, 0, 0), due);
        }

        [Fact]
        public void Daily_DueNotYetToday_ReturnsYesterdayDue()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Daily,
                TimeHour = 11,
                TimeMinute = 0
            };
            // 11:00 > 10:30 → yesterday's 11:00
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 2, 11, 0, 0), due);
        }

        [Fact]
        public void Daily_ExactlyOnTime_ReturnsTodayDue()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Daily,
                TimeHour = 10,
                TimeMinute = 30
            };
            // exactly 10:30 == 10:30 → today's 10:30
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 10, 30, 0), due);
        }

        // ---- CalcLastDueTime: Weekly ----

        [Fact]
        public void Weekly_SameDayBeforeDue_ReturnsPreviousWeek()
        {
            // RefTime is Friday 10:30. Schedule is Friday 11:00.
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Weekly,
                WeekDay = DayOfWeek.Friday,
                TimeHour = 11,
                TimeMinute = 0
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            // This Friday's 11:00 > 10:30, so last week's Friday 11:00
            Assert.Equal(new DateTime(2026, 3, 27, 11, 0, 0), due);
        }

        [Fact]
        public void Weekly_SameDayAfterDue_ReturnsTodayDue()
        {
            // RefTime is Friday 10:30. Schedule is Friday 09:00.
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Weekly,
                WeekDay = DayOfWeek.Friday,
                TimeHour = 9,
                TimeMinute = 0
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 9, 0, 0), due);
        }

        [Fact]
        public void Weekly_DifferentDay_ReturnsLastOccurrence()
        {
            // RefTime is Friday 10:30. Schedule is Monday 08:00.
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Weekly,
                WeekDay = DayOfWeek.Monday,
                TimeHour = 8,
                TimeMinute = 0
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            // Last Monday was 2026-03-30
            Assert.Equal(new DateTime(2026, 3, 30, 8, 0, 0), due);
        }

        // ---- CalcLastDueTime: Hourly ----

        [Fact]
        public void Hourly_MinutePassed_ReturnsCurrentHourDue()
        {
            // RefTime is 10:30. Schedule is minute=15 → this hour's 10:15 already passed
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Hourly,
                HourlyMinute = 15
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 10, 15, 0), due);
        }

        [Fact]
        public void Hourly_MinuteNotYet_ReturnsPreviousHourDue()
        {
            // RefTime is 10:30. Schedule is minute=45 → 10:45 not yet → return 09:45
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Hourly,
                HourlyMinute = 45
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 9, 45, 0), due);
        }

        // ---- CalcLastDueTime: Interval ----

        [Fact]
        public void Interval_NoLastRun_ReturnsNow()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Interval,
                IntervalMinutes = 30,
                LastRunTime = null
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            // 未実行は即実行 → asOf を返す
            Assert.Equal(RefTime, due);
        }

        [Fact]
        public void Interval_DuePassed_ReturnsDueTime()
        {
            // Last run was 10:00, interval 20 min → due at 10:20, now is 10:30 → return 10:20
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Interval,
                IntervalMinutes = 20,
                LastRunTime = new DateTime(2026, 4, 3, 10, 0, 0)
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 10, 20, 0), due);
        }

        [Fact]
        public void Interval_DueNotYet_ReturnsNull()
        {
            // Last run was 10:20, interval 60 min → due at 11:20, now is 10:30 → not yet
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Interval,
                IntervalMinutes = 60,
                LastRunTime = new DateTime(2026, 4, 3, 10, 20, 0)
            };
            var due = Registry.CalcLastDueTime(entry, RefTime);
            Assert.Null(due);
        }

        // ---- CalcNextRunTime ----

        [Fact]
        public void NextRunTime_Daily_AfterDue_ReturnsTomorrow()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Daily,
                TimeHour = 9,
                TimeMinute = 0
            };
            // now 10:30, daily 09:00 already passed → next is tomorrow 09:00
            var next = Registry.CalcNextRunTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 4, 9, 0, 0), next);
        }

        [Fact]
        public void NextRunTime_Daily_BeforeDue_ReturnsToday()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Daily,
                TimeHour = 14,
                TimeMinute = 0
            };
            // now 10:30, daily 14:00 not yet → today 14:00
            var next = Registry.CalcNextRunTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 14, 0, 0), next);
        }

        [Fact]
        public void NextRunTime_Interval_WithLastRun_ReturnsLastRunPlusInterval()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Interval,
                IntervalMinutes = 30,
                LastRunTime = new DateTime(2026, 4, 3, 10, 0, 0)
            };
            var next = Registry.CalcNextRunTime(entry, RefTime);
            Assert.Equal(new DateTime(2026, 4, 3, 10, 30, 0), next);
        }

        [Fact]
        public void NextRunTime_Interval_NoLastRun_ReturnsNow()
        {
            var entry = new ScheduleEntry
            {
                Type = ScheduleType.Interval,
                IntervalMinutes = 30,
                LastRunTime = null
            };
            var next = Registry.CalcNextRunTime(entry, RefTime);
            Assert.Equal(RefTime, next);
        }
    }
}
