using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Engine;
using BreezeFlow.Models;

namespace BreezeFlow.Steps
{
    public class MySQLOutputStep : StepBase
    {
        public override StepType StepType => StepType.MySQLOutput;

        public override IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
            => DbOutputExecutor.ExecuteStreamingAsync(MySqlProvider.Instance, Settings, input, progress, ct);

        public override string GetDisplayIcon() => "🐬";
    }
}
