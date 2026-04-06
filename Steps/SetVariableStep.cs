using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// 日付・期間などの初期値を1行のデータとして生成するステップ。
    /// 後続の SQL ステップで ? プレースホルダーに渡すパラメータ行を作るために使用する。
    ///
    /// Settings["Fields"]     : "フィールド名=式" を改行区切り
    /// Settings["DateFormat"] : 日付フォーマット (デフォルト: yyyy/MM/dd)
    ///
    /// 使用できる式:
    ///   TODAY          … 今日の日付
    ///   NOW            … 現在日時
    ///   YESTERDAY      … 昨日
    ///   TODAY-N        … N日前  (例: TODAY-7)
    ///   TODAY+N        … N日後  (例: TODAY+3)
    ///   MONTH_START    … 当月初日
    ///   MONTH_END      … 当月末日
    ///   YEAR_START     … 当年初日
    ///   YEAR_END       … 当年末日
    ///   TODAY_START    … 今日の開始時刻 (yyyy/MM/dd 00:00:00)
    ///   TODAY_END      … 今日の終了時刻 (yyyy/MM/dd 23:59:59)
    ///   YESTERDAY_START… 昨日の開始時刻 (yyyy/MM/dd 00:00:00)
    ///   YESTERDAY_END  … 昨日の終了時刻 (yyyy/MM/dd 23:59:59)
    ///   MONTH_START_DT … 当月初日の開始時刻 (yyyy/MM/dd 00:00:00)
    ///   MONTH_END_DT   … 当月末日の終了時刻 (yyyy/MM/dd 23:59:59)
    ///   PREV_MONTH_START… 前月初日
    ///   PREV_MONTH_END … 前月末日
    ///   その他         … リテラル文字列として使用
    /// </summary>
    public class SetVariableStep : StepBase
    {
        public override StepType StepType => StepType.SetVariable;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var fieldsRaw  = Settings.TryGetValue("Fields",     out var f)   ? f?.ToString()   ?? "" : "";
            var dateFormat = Settings.TryGetValue("DateFormat", out var df)  ? df?.ToString()  ?? "yyyy/MM/dd" : "yyyy/MM/dd";

            var now   = DateTime.Now;
            var today = now.Date;

            var row = new Dictionary<string, object?>();

            foreach (var line in fieldsRaw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) continue;

                var fieldName = trimmed.Substring(0, eqIdx).Trim();
                var expr      = eqIdx + 1 < trimmed.Length ? trimmed.Substring(eqIdx + 1).Trim() : "";

                if (string.IsNullOrEmpty(fieldName)) continue;

                row[fieldName] = EvaluateExpr(expr, today, now, dateFormat);
            }

            progress.Report($"Set Variable: {row.Count}個の変数をセット");
            return Task.FromResult(new List<Dictionary<string, object?>> { row });
        }

        private static string EvaluateExpr(string expr, DateTime today, DateTime now, string dateFmt)
        {
            if (string.Equals(expr, "TODAY", StringComparison.OrdinalIgnoreCase))
                return today.ToString(dateFmt);

            if (string.Equals(expr, "NOW", StringComparison.OrdinalIgnoreCase))
                return now.ToString(dateFmt + " HH:mm:ss");

            if (string.Equals(expr, "YESTERDAY", StringComparison.OrdinalIgnoreCase))
                return today.AddDays(-1).ToString(dateFmt);

            if (string.Equals(expr, "MONTH_START", StringComparison.OrdinalIgnoreCase))
                return new DateTime(today.Year, today.Month, 1).ToString(dateFmt);

            if (string.Equals(expr, "MONTH_END", StringComparison.OrdinalIgnoreCase))
                return new DateTime(today.Year, today.Month,
                    DateTime.DaysInMonth(today.Year, today.Month)).ToString(dateFmt);

            if (string.Equals(expr, "YEAR_START", StringComparison.OrdinalIgnoreCase))
                return new DateTime(today.Year, 1, 1).ToString(dateFmt);

            if (string.Equals(expr, "YEAR_END", StringComparison.OrdinalIgnoreCase))
                return new DateTime(today.Year, 12, 31).ToString(dateFmt);

            if (string.Equals(expr, "TODAY_START", StringComparison.OrdinalIgnoreCase))
                return today.ToString(dateFmt) + " 00:00:00";

            if (string.Equals(expr, "TODAY_END", StringComparison.OrdinalIgnoreCase))
                return today.ToString(dateFmt) + " 23:59:59";

            if (string.Equals(expr, "YESTERDAY_START", StringComparison.OrdinalIgnoreCase))
                return today.AddDays(-1).ToString(dateFmt) + " 00:00:00";

            if (string.Equals(expr, "YESTERDAY_END", StringComparison.OrdinalIgnoreCase))
                return today.AddDays(-1).ToString(dateFmt) + " 23:59:59";

            if (string.Equals(expr, "MONTH_START_DT", StringComparison.OrdinalIgnoreCase))
                return new DateTime(today.Year, today.Month, 1).ToString(dateFmt) + " 00:00:00";

            if (string.Equals(expr, "MONTH_END_DT", StringComparison.OrdinalIgnoreCase))
                return new DateTime(today.Year, today.Month,
                    DateTime.DaysInMonth(today.Year, today.Month)).ToString(dateFmt) + " 23:59:59";

            if (string.Equals(expr, "PREV_MONTH_START", StringComparison.OrdinalIgnoreCase))
            {
                var prev = today.AddMonths(-1);
                return new DateTime(prev.Year, prev.Month, 1).ToString(dateFmt);
            }

            if (string.Equals(expr, "PREV_MONTH_END", StringComparison.OrdinalIgnoreCase))
            {
                var prev = today.AddMonths(-1);
                return new DateTime(prev.Year, prev.Month,
                    DateTime.DaysInMonth(prev.Year, prev.Month)).ToString(dateFmt);
            }

            // TODAY±N
            var m = Regex.Match(expr, @"^TODAY([+-])(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[2].Value);
                if (m.Groups[1].Value == "-") n = -n;
                return today.AddDays(n).ToString(dateFmt);
            }

            // リテラル
            return expr;
        }

        public override string GetDisplayIcon() => "📅";
    }
}
