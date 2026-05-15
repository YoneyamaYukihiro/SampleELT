using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Engine;
using BreezeFlow.Models;

namespace BreezeFlow.Steps
{
    public class OracleOutputStep : StepBase
    {
        public override StepType StepType => StepType.OracleOutput;

        public override IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
            => DbOutputExecutor.ExecuteStreamingAsync(OracleProvider.Instance, Settings, input, progress, ct);

        public override string GetDisplayIcon() => "🔶";
    }
}
