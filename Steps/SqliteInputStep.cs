using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class SqliteInputStep : StepBase
    {
        public override StepType StepType => StepType.DBInput;

        public override IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
            => DbInputExecutor.ExecuteStreamingAsync(SqliteProvider.Instance, Settings, input, progress, ct);

        public override string GetDisplayIcon() => "🪶";
    }
}
