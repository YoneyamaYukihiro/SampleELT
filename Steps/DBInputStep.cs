using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Engine;
using BreezeFlow.Models;
using BreezeFlow.Models.Stores;

namespace BreezeFlow.Steps
{
    /// <summary>
    /// 接続の DbType に応じて適切な <see cref="DbProvider"/> を選択し、共通 Executor に委譲する統合 DB 入力ステップ。
    /// ExecuteStreamingAsync を直接オーバーライドして DbDataReader からの行単位ストリーミングを下流に流す。
    /// </summary>
    public class DBInputStep : StepBase
    {
        public override StepType StepType => StepType.DBInput;

        public override IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connInfo = IConnectionStore.Default.FindConnection(Settings);
            var provider = DbProvider.For(connInfo?.DbType ?? DbType.MySQL);
            return DbInputExecutor.ExecuteStreamingAsync(provider, Settings, input, progress, ct);
        }

        public override string GetDisplayIcon() => "🔌";
    }
}
