using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BreezeFlow.Models
{
    public abstract class StepBase
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public abstract StepType StepType { get; }
        public double CanvasX { get; set; }
        public double CanvasY { get; set; }
        public double NodeWidth { get; set; } = 150.0;
        public double NodeHeight { get; set; } = 70.0;
        public Dictionary<string, object?> Settings { get; set; } = new();

        /// <summary>
        /// ExecutionEngine が実行前にセットする複数入力ストリーム (IAsyncEnumerable のまま)。
        /// AllInputStreams[0] = 最初に接続されたストリーム (Left)
        /// AllInputStreams[1] = 2番目のストリーム (Right) ← MergeJoin / TableCompare で使用
        /// 単入力ステップは空のまま。
        /// 多入力ステップは各ストリームをそれぞれ最大 1 回だけ列挙すること (二度読みするには事前に List 化する)。
        /// </summary>
        public List<IAsyncEnumerable<Dictionary<string, object?>>> AllInputStreams { get; set; } = new();

        /// <summary>
        /// レガシー List ベースの実行 API。派生は <see cref="ExecuteAsync"/> か
        /// <see cref="ExecuteStreamingAsync"/> のどちらかを必ずオーバーライドする
        /// (オーバーライドしない場合、デフォルト実装同士の相互呼び出しでスタックオーバーフロー)。
        /// </summary>
        public virtual async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var list = new List<Dictionary<string, object?>>();
            await foreach (var row in ExecuteStreamingAsync(ToAsyncEnumerable(inputData), progress, ct)
                .WithCancellation(ct).ConfigureAwait(false))
            {
                list.Add(row);
            }
            return list;
        }

        /// <summary>
        /// ストリーミング実行 API。入力を IAsyncEnumerable で受け、出力を IAsyncEnumerable で返す。
        /// メモリピークを抑えたい場合はこちらをオーバーライドする (DB Input/Output や行単位変換)。
        /// デフォルト実装は入力を全部 List に集めて <see cref="ExecuteAsync"/> を呼ぶため、
        /// レガシー実装はそのまま動作する (ただしストリーミングのメモリ削減効果は無し)。
        /// </summary>
        public virtual async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var list = new List<Dictionary<string, object?>>();
            await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
                list.Add(row);
            var output = await ExecuteAsync(list, progress, ct).ConfigureAwait(false);
            foreach (var row in output)
            {
                ct.ThrowIfCancellationRequested();
                yield return row;
            }
        }

        public virtual string GetDisplayIcon()
        {
            return StepType switch
            {
                StepType.OracleInput => "🔶",
                StepType.OracleOutput => "🔶",
                StepType.MySQLInput => "🐬",
                StepType.MySQLOutput => "🐬",
                StepType.ExcelInput => "📗",
                StepType.ExcelOutput => "📗",
                StepType.Filter => "🔍",
                StepType.Calculation => "🧮",
                _ => "⚙️"
            };
        }

        private static async IAsyncEnumerable<Dictionary<string, object?>> ToAsyncEnumerable(
            IEnumerable<Dictionary<string, object?>> source)
        {
            foreach (var row in source)
                yield return row;
            await Task.CompletedTask;
        }
    }
}
