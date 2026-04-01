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
