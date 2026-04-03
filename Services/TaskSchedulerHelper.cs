using System;
using System.Diagnostics;
using SampleELT.Models;

namespace SampleELT.Services
{
    /// <summary>
    /// schtasks.exe を使って Windows タスクスケジューラへのタスク登録・削除を行うヘルパー。
    /// </summary>
    public static class TaskSchedulerHelper
    {
        private const string TaskFolder = "SampleELT";

        public static string GetTaskName(ScheduleEntry entry)
            => $"{TaskFolder}\\{entry.Id}";

        /// <summary>タスクスケジューラにスケジュールを登録（既存の場合は上書き）。</summary>
        public static (bool Success, string Message) Register(ScheduleEntry entry)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return (false, "実行ファイルのパスを取得できませんでした");

            if (string.IsNullOrWhiteSpace(entry.PipelineFilePath))
                return (false, "パイプラインファイルが指定されていません");

            var schedArgs = BuildScheduleArgs(entry);
            if (schedArgs == null)
                return (false, "このスケジュール種別はタスクスケジューラに対応していません");

            var taskName = GetTaskName(entry);
            // /TR の引数内にダブルクォートがあるため、全体を \" でエスケープ
            var tr = $"\\\"{exePath}\\\" --run \\\"{entry.PipelineFilePath}\\\"";
            var args = $"/Create /TN \"{taskName}\" /TR \"{tr}\" {schedArgs} /F";

            return RunSchtasks(args);
        }

        /// <summary>タスクスケジューラからスケジュールを削除。</summary>
        public static (bool Success, string Message) Unregister(ScheduleEntry entry)
        {
            var taskName = GetTaskName(entry);
            var (success, message) = RunSchtasks($"/Delete /TN \"{taskName}\" /F");
            // タスクが存在しない場合のエラーは無視
            if (!success && message.Contains("存在しない", StringComparison.OrdinalIgnoreCase))
                return (true, "");
            return (success, message);
        }

        // ==================== private ====================

        private static string? BuildScheduleArgs(ScheduleEntry entry) => entry.Type switch
        {
            ScheduleType.Daily =>
                $"/SC DAILY /ST {entry.TimeHour:D2}:{entry.TimeMinute:D2}",
            ScheduleType.Weekly =>
                $"/SC WEEKLY /D {GetDayAbbr(entry.WeekDay)} /ST {entry.TimeHour:D2}:{entry.TimeMinute:D2}",
            ScheduleType.Hourly =>
                // 毎時 HourlyMinute 分に実行（開始を 00:MM にして 1時間ごとに繰り返す）
                $"/SC HOURLY /MO 1 /ST 00:{entry.HourlyMinute:D2}",
            ScheduleType.Interval =>
                $"/SC MINUTE /MO {entry.IntervalMinutes}",
            _ => null
        };

        private static string GetDayAbbr(DayOfWeek day) => day switch
        {
            DayOfWeek.Monday    => "MON",
            DayOfWeek.Tuesday   => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday  => "THU",
            DayOfWeek.Friday    => "FRI",
            DayOfWeek.Saturday  => "SAT",
            DayOfWeek.Sunday    => "SUN",
            _ => "MON"
        };

        private static (bool Success, string Message) RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "schtasks.exe を起動できませんでした");

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode == 0)
                    return (true, stdout.Trim());

                var errMsg = stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim();
                return (false, errMsg);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
