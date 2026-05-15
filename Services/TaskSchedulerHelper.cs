using System;
using System.Diagnostics;
using BreezeFlow.Models;

namespace BreezeFlow.Services
{
    /// <summary>
    /// schtasks.exe を使って Windows タスクスケジューラへのタスク登録・削除を行うヘルパー。
    /// </summary>
    public static class TaskSchedulerHelper
    {
        private const string TaskFolder = "BreezeFlow";

        // Windows タスク名で使用できない文字: \ / : * ? " < > | （加えて先頭/末尾の空白とドット）
        private static readonly char[] InvalidTaskNameChars =
            { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// スケジュール名から Windows タスク名を生成する（フォルダ \ サニタイズ済み名前）。
        /// 名前が空の場合は Guid をフォールバックとして使用。
        /// </summary>
        public static string GetTaskName(ScheduleEntry entry)
        {
            var safe = SanitizeTaskName(entry.Name);
            if (string.IsNullOrEmpty(safe)) safe = entry.Id.ToString();
            return $"{TaskFolder}\\{safe}";
        }

        private static string SanitizeTaskName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(InvalidTaskNameChars, chars[i]) >= 0)
                    chars[i] = '_';
            }
            return new string(chars).TrimEnd('.', ' ');
        }

        /// <summary>タスクスケジューラにスケジュールを登録（既存の場合は上書き）。</summary>
        public static (bool Success, string Message) Register(ScheduleEntry entry)
        {
            string? exePath;
            try
            {
                // Environment.ProcessPath は MainModule.FileName より安全
                // (Windows Server の制限環境でも例外を投げない)
                exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch (Exception ex)
            {
                return (false, $"実行ファイルのパス取得に失敗: {ex.Message}");
            }

            if (string.IsNullOrEmpty(exePath))
                return (false, "実行ファイルのパスを取得できませんでした");

            // Target に応じて使用する CLI モードと対象ファイルパスを決定
            var (cliFlag, targetFile) = entry.Target == ScheduleTarget.Job
                ? ("--run-job", entry.JobFilePath)
                : ("--run",     entry.PipelineFilePath);

            if (string.IsNullOrWhiteSpace(targetFile))
                return (false, entry.Target == ScheduleTarget.Job
                    ? "ジョブファイルが指定されていません"
                    : "パイプラインファイルが指定されていません");

            var schedArgs = BuildScheduleArgs(entry);
            if (schedArgs == null)
                return (false, "このスケジュール種別はタスクスケジューラに対応していません");

            var taskName = GetTaskName(entry);

            // 名前変更時: 旧タスク名で登録済みなら削除する
            if (!string.IsNullOrEmpty(entry.LastTaskName) && entry.LastTaskName != taskName)
            {
                TryDeleteTask(entry.LastTaskName);
            }

            // /TR の引数内にダブルクォートがあるため、全体を \" でエスケープ
            var tr = $"\\\"{exePath}\\\" {cliFlag} \\\"{targetFile}\\\"";

            // 既定では「ユーザーがログオンしている時のみ実行」で登録される。
            // ログオン未状態でも動作させたい場合は、登録後に Windows タスクスケジューラ GUI で
            // 「ユーザーがログオンしているかどうかにかかわらず実行する」へ手動変更してください。
            var args = $"/Create /TN \"{taskName}\" /TR \"{tr}\" {schedArgs} /F";

            var result = RunSchtasks(args);
            if (result.Success)
                entry.LastTaskName = taskName;
            return result;
        }

        /// <summary>タスクスケジューラからスケジュールを削除。</summary>
        public static (bool Success, string Message) Unregister(ScheduleEntry entry)
        {
            // 最後に登録した名前があればそれを優先削除（リネーム後でも残骸を残さない）
            if (!string.IsNullOrEmpty(entry.LastTaskName))
                TryDeleteTask(entry.LastTaskName);

            // 旧バージョンで GUID 名登録された残骸も併せて削除
            TryDeleteTask($"{TaskFolder}\\{entry.Id}");

            // 現在の名前ベースのタスクも念のため削除
            var current = GetTaskName(entry);
            var (success, message) = RunSchtasks($"/Delete /TN \"{current}\" /F");

            entry.LastTaskName = "";

            if (!success && message.Contains("存在しない", StringComparison.OrdinalIgnoreCase))
                return (true, "");
            return (success, message);
        }

        /// <summary>指定タスク名を削除する（存在しなくてもエラーにしない）。</summary>
        private static void TryDeleteTask(string taskName)
        {
            try { RunSchtasks($"/Delete /TN \"{taskName}\" /F"); }
            catch { /* 失敗は無視 */ }
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
                    RedirectStandardInput  = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "schtasks.exe を起動できませんでした");

                // schtasks が stdin から入力（パスワード等）を待たないよう即時クローズ
                try { proc.StandardInput.Close(); } catch { /* ignore */ }

                // 同期 ReadToEnd だとパイプバッファ満杯時にデッドロックする可能性があるため
                // 非同期読み取りで先にバッファを空ける
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                const int timeoutMs = 30000;
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(true); } catch { /* ignore */ }
                    return (false, $"schtasks.exe が {timeoutMs / 1000} 秒以内に応答しませんでした。");
                }

                // WaitForExit 後にもう一度待ってストリームを完全に読み切る（推奨パターン）
                proc.WaitForExit();

                var stdout = stdoutTask.Result;
                var stderr = stderrTask.Result;

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
