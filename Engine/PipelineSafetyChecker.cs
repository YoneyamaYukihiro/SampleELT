using System;
using System.Collections.Generic;
using System.Linq;
using BreezeFlow.Models;

namespace BreezeFlow.Engine
{
    /// <summary>
    /// パイプライン実行前に DB 接続まわりの安全性を検査するヘルパー。
    /// 「誤った DB に書き込む」事故を防ぐため、実行 GO ボタンを押した直後に
    /// <see cref="Check"/> を呼び、結果に応じて停止 / 確認 / 続行を判断する。
    /// </summary>
    public static class PipelineSafetyChecker
    {
        public enum IssueSeverity
        {
            /// <summary>
            /// 修正なしに進めると確実に事故になる。実行をブロックする。
            /// 例: 接続未解決、Read-only 接続への書き込み。
            /// </summary>
            Block,
            /// <summary>
            /// 意図に反する可能性がある。ユーザーに最終確認を取る。
            /// 例: Production 接続への書き込み。
            /// </summary>
            Confirm
        }

        public class Issue
        {
            public IssueSeverity Severity { get; }
            public string StepName { get; }
            public string Message { get; }
            public DbConnectionInfo? Connection { get; }

            public Issue(IssueSeverity severity, string stepName, string message, DbConnectionInfo? connection)
            {
                Severity = severity;
                StepName = stepName;
                Message  = message;
                Connection = connection;
            }
        }

        public static List<Issue> Check(Pipeline pipeline)
        {
            var issues = new List<Issue>();
            foreach (var step in pipeline.Steps)
                CheckStep(step, issues);
            return issues;
        }

        private static void CheckStep(StepBase step, List<Issue> issues)
        {
            // ConnectionId を使うステップだけが対象
            if (!step.Settings.TryGetValue("ConnectionId", out var idObj) || idObj == null)
                return;
            var raw = idObj.ToString() ?? "";
            if (string.IsNullOrEmpty(raw)) return;

            if (!Guid.TryParse(raw, out var id))
            {
                issues.Add(new Issue(
                    IssueSeverity.Block,
                    step.Name,
                    $"ConnectionId '{raw}' が Guid として解釈できません。",
                    null));
                return;
            }

            var conn = Models.Stores.IConnectionStore.Default.GetById(id);
            if (conn == null)
            {
                issues.Add(new Issue(
                    IssueSeverity.Block,
                    step.Name,
                    $"接続設定 '{id}' が見つかりません。接続マネージャで設定し直してください。",
                    null));
                return;
            }

            // 書き込み系ステップだけが対象の追加チェック
            if (!ConnectionSafety.IsWriteStep(step.StepType)) return;

            // Read-only 接続への書き込みはブロック
            if (conn.IsReadOnly)
            {
                issues.Add(new Issue(
                    IssueSeverity.Block,
                    step.Name,
                    $"接続「{conn.Name}」は Select 専用に設定されているため、{step.StepType} は実行できません。",
                    conn));
                return;
            }

            // ExecSQL は実行 SQL も検査 (SELECT のみなら Production 確認をスキップしてよい)
            if (step.StepType == StepType.ExecSQL)
            {
                var sql = step.Settings.TryGetValue("SQL", out var s) ? s?.ToString() ?? "" : "";
                if (!ConnectionSafety.ContainsWriteSql(sql))
                    return; // SELECT のみと判定 → 確認不要
            }

            // Production 接続への書き込みは確認
            if (conn.Environment == DbEnvironment.Production)
            {
                issues.Add(new Issue(
                    IssueSeverity.Confirm,
                    step.Name,
                    $"[PRD] {ConnectionSafety.DescribeConnection(conn)} に対する {step.StepType} を実行します。",
                    conn));
            }
        }

        /// <summary>
        /// Issue のリストを「Block (赤) → Confirm (オレンジ)」の順で 1 行ずつ整形する。
        /// 確認ダイアログにそのまま貼り付ける用途。
        /// </summary>
        public static string Format(IEnumerable<Issue> issues)
        {
            var lines = issues
                .OrderBy(i => i.Severity == IssueSeverity.Block ? 0 : 1)
                .Select(i =>
                {
                    var prefix = i.Severity == IssueSeverity.Block ? "❌ ブロック" : "⚠ 要確認";
                    return $"{prefix} [{i.StepName}] {i.Message}";
                });
            return string.Join("\n", lines);
        }
    }
}
