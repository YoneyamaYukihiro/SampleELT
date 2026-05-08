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
    /// 接続の DbType に応じて適切な <see cref="DbProvider"/> を選択し、共通 Executor に委譲する統合 DB 入力ステップ。
    /// </summary>
    public class DBInputStep : StepBase
    {
        public override StepType StepType => StepType.DBInput;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connInfo = IConnectionStore.Default.FindConnection(Settings);
            var provider = DbProvider.For(connInfo?.DbType ?? DbType.MySQL);
            return DbInputExecutor.ExecuteAsync(provider, Settings, inputData, progress, ct);
        }

        public override string GetDisplayIcon() => "🔌";
    }
}
