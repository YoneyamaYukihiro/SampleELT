using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;

namespace BreezeFlow.Steps
{
    /// <summary>
    /// フィールド値で行を複数の出力ポートへ振り分けるステップ。
    ///
    /// Settings["FieldName"] : 評価対象フィールド名
    /// Settings["Cases"]     : 1 行 1 ケース。`値|BranchKey[|表示名]` の形式 (改行区切り)。
    ///                          BranchKey 省略時は値そのものを Key として使用。
    /// Settings["IncludeDefault"] : false なら default ポートを出力ポート一覧から除外し、
    ///                              どの Case にも一致しない行は破棄する (既定: true)。
    ///
    /// 評価ルール: 上から順に最初に一致した Case の BranchKey へ流す。同一値の Case は最初のものが勝つ。
    /// 比較は文字列の Ordinal 完全一致。NULL/欠損フィールドは "" として比較する。
    /// </summary>
    public class SwitchStep : StepBase
    {
        public override StepType StepType => StepType.Switch;

        public const string DefaultBranchKey = "default";

        public override IReadOnlyList<OutputPort> OutputPorts
        {
            get
            {
                var ports = new List<OutputPort>();
                foreach (var c in ParseCases())
                    ports.Add(new OutputPort(c.BranchKey, c.Label));
                if (IncludeDefaultPort)
                    ports.Add(new OutputPort(DefaultBranchKey, "default"));
                // 1 件も無いなら最低 1 ポートは保証 (UI 上の見栄えのため)
                if (ports.Count == 0)
                    ports.Add(new OutputPort(DefaultBranchKey, "default"));
                return ports;
            }
        }

        private bool IncludeDefaultPort
        {
            get
            {
                if (!Settings.TryGetValue("IncludeDefault", out var v) || v == null) return true;
                return !string.Equals(v.ToString(), "false", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Settings["Cases"] を `(Value, BranchKey, Label)` のリストにパースする。
        /// 値が空の行はスキップ。BranchKey が空なら Value をそのまま Key として使う。
        /// 同一 BranchKey が複数現れた場合、ポート一覧では 1 つに集約する (後方の Label は破棄)。
        /// </summary>
        internal List<CaseEntry> ParseCases()
        {
            var raw = Settings.TryGetValue("Cases", out var c) ? c?.ToString() ?? "" : "";
            var result = new List<CaseEntry>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                var value = parts.Length > 0 ? parts[0].Trim() : "";
                if (value.Length == 0) continue;

                var key = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : value;
                // 予約語 "default" は default ポートと衝突するため拒否
                if (string.Equals(key, DefaultBranchKey, StringComparison.OrdinalIgnoreCase))
                    key = key + "_case";

                var label = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2])
                    ? parts[2].Trim()
                    : $"{key} (= {value})";

                if (!seenKeys.Add(key))
                {
                    // 既に同じ Key のポートがある: 値リストにこの value も含めるため、追加だけする
                    result.Add(new CaseEntry(value, key, label, IsDuplicateKey: true));
                    continue;
                }
                result.Add(new CaseEntry(value, key, label, IsDuplicateKey: false));
            }
            return result;
        }

        internal sealed record CaseEntry(string Value, string BranchKey, string Label, bool IsDuplicateKey);

        public override async IAsyncEnumerable<RoutedRow> ExecuteRoutedAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var fieldName = Settings.TryGetValue("FieldName", out var f) ? f?.ToString() ?? "" : "";
            var cases = ParseCases();
            var includeDefault = IncludeDefaultPort;

            int total = 0;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
            {
                total++;
                row.TryGetValue(fieldName, out var v);
                var s = v?.ToString() ?? string.Empty;

                string? branch = null;
                foreach (var c in cases)
                {
                    if (string.Equals(c.Value, s, StringComparison.Ordinal))
                    {
                        branch = c.BranchKey;
                        break;
                    }
                }

                if (branch == null)
                {
                    if (!includeDefault)
                    {
                        // default 出力が無効なら破棄
                        continue;
                    }
                    branch = DefaultBranchKey;
                }

                counts[branch] = (counts.TryGetValue(branch, out var n) ? n : 0) + 1;
                yield return new RoutedRow(branch, row);
            }

            var summary = string.Join(", ", counts.Select(kv => $"{kv.Key}={kv.Value}"));
            progress.Report($"Switch: {total}行 → " + (summary.Length == 0 ? "(全件破棄)" : summary));
        }

        /// <summary>
        /// レガシー呼び出し用 (テスト等の <see cref="ExecuteAsync"/> 経由)。
        /// 多ポートのコンテキスト無しでは振り分け先が決まらないため、入力をそのまま素通しする。
        /// 実際のパイプライン実行は ExecutionEngine が <see cref="ExecuteRoutedAsync"/> を呼ぶ。
        /// </summary>
        public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
                yield return row;
        }

        public override string GetDisplayIcon() => "🔀";
    }
}
