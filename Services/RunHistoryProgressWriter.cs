using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BreezeFlow.Models;

namespace BreezeFlow.Services
{
    /// <summary>
    /// <see cref="IProgress{T}"/> をラップし、各ステップの開始/終了/エラーを正規表現で検出して
    /// <see cref="RunHistoryStore"/> に記録する。
    ///
    /// ExecutionEngine は次の固定パターンで progress.Report を呼ぶ:
    ///   [Name] 実行中...
    ///   [Name] 完了 (N行)
    ///   [Name] エラー: メッセージ
    ///   [Name] キャンセルされました
    /// これらを掴んで run_steps の INSERT / UPDATE に変換する。
    /// 取りこぼし (パイプライン中断時など) は <see cref="FinishUnclosedSteps"/> でまとめて閉じる。
    ///
    /// 元の progress (UI ログパネル等) には常にそのまま転送するため、副作用は記録だけ。
    /// </summary>
    public class RunHistoryProgressWriter : IProgress<string>
    {
        private readonly RunHistoryStore _store;
        private readonly long _runId;
        private readonly IProgress<string>? _passthrough;

        private long _currentStepId = -1;
        private string? _currentStepName;
        private int _stepOrder;
        private int _totalRows;

        // 同一ステップを別 progress.Report で更新できるよう、未終了ステップを名前→ID で覚える
        private readonly Dictionary<string, long> _openSteps = new(StringComparer.Ordinal);

        // ExecutionEngine 出力パターン
        private static readonly Regex StepStart  = new(@"^\[(?<name>[^\]]+)\]\s*実行中", RegexOptions.Compiled);
        private static readonly Regex StepDone   = new(@"^\[(?<name>[^\]]+)\]\s*完了\s*\((?<rows>\d+)行\)", RegexOptions.Compiled);
        private static readonly Regex StepError  = new(@"^\[(?<name>[^\]]+)\]\s*エラー:\s*(?<msg>.+)$", RegexOptions.Compiled);
        private static readonly Regex StepCancel = new(@"^\[(?<name>[^\]]+)\]\s*キャンセル", RegexOptions.Compiled);

        public RunHistoryProgressWriter(RunHistoryStore store, long runId, IProgress<string>? passthrough)
        {
            _store = store;
            _runId = runId;
            _passthrough = passthrough;
        }

        /// <summary>ここまでに観測された「[X] 完了 (N行)」の合計値。末端ステップの行数として返したい場合に使う。</summary>
        public int LastReportedRowCount { get; private set; }

        /// <summary>パイプライン実行で観測したステップごとの最終行数の合計。`runs.total_rows` 候補。</summary>
        public int AggregateRowCount => _totalRows;

        public void Report(string value)
        {
            _passthrough?.Report(value);
            if (string.IsNullOrEmpty(value)) return;

            try
            {
                var level = ClassifyLevel(value);
                _store.AppendLog(_runId, level, value);

                Match m;
                if ((m = StepStart.Match(value)).Success)
                {
                    var name = m.Groups["name"].Value;
                    _stepOrder++;
                    _currentStepName = name;
                    _currentStepId = _store.RecordStepStart(_runId, _stepOrder, name, "");
                    _openSteps[name] = _currentStepId;
                    return;
                }

                if ((m = StepDone.Match(value)).Success)
                {
                    var name = m.Groups["name"].Value;
                    var rows = int.Parse(m.Groups["rows"].Value);
                    LastReportedRowCount = rows;
                    _totalRows += rows;
                    if (_openSteps.TryGetValue(name, out var sid))
                    {
                        _store.RecordStepEnd(sid, RunStatus.Success, rows, null);
                        _openSteps.Remove(name);
                    }
                    return;
                }

                if ((m = StepError.Match(value)).Success)
                {
                    var name = m.Groups["name"].Value;
                    var msg = m.Groups["msg"].Value;
                    if (_openSteps.TryGetValue(name, out var sid))
                    {
                        _store.RecordStepEnd(sid, RunStatus.Failed, null, msg);
                        _openSteps.Remove(name);
                    }
                    return;
                }

                if ((m = StepCancel.Match(value)).Success)
                {
                    var name = m.Groups["name"].Value;
                    if (_openSteps.TryGetValue(name, out var sid))
                    {
                        _store.RecordStepEnd(sid, RunStatus.Cancelled, null, "キャンセル");
                        _openSteps.Remove(name);
                    }
                }
            }
            catch
            {
                // 記録失敗はパイプラインを壊さない
            }
        }

        /// <summary>未完了のままになっているステップを指定ステータスで強制クローズする (パイプライン例外時に呼ぶ)。</summary>
        public void FinishUnclosedSteps(RunStatus status, string? errorMessage)
        {
            foreach (var kv in _openSteps)
            {
                _store.RecordStepEnd(kv.Value, status, null, errorMessage);
            }
            _openSteps.Clear();
        }

        private static string ClassifyLevel(string msg)
        {
            if (msg.Contains("エラー:") || msg.Contains("===== エラー")) return "ERROR";
            if (msg.StartsWith("警告") || msg.Contains("警告:")) return "WARN";
            return "INFO";
        }
    }
}
