using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;
using SampleELT.Models.Stores;

namespace SampleELT.Steps
{
    /// <summary>
    /// 接続の DbType に応じて適切な <see cref="DbProvider"/> を選択し、共通 Executor に委譲する統合 DB 出力ステップ。
    /// 入力をストリーミングのまま受け取り、1 行ずつ INSERT/UPSERT して下流へ流す。
    /// </summary>
    public class DBOutputStep : StepBase
    {
        public override StepType StepType => StepType.DBOutput;

        public override IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connInfo = IConnectionStore.Default.FindConnection(Settings);
            var provider = DbProvider.For(connInfo?.DbType ?? DbType.MySQL);
            return DbOutputExecutor.ExecuteStreamingAsync(provider, Settings, input, progress, ct);
        }

        public override string GetDisplayIcon() => "🔌";
    }
}
