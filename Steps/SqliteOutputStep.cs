using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class SqliteOutputStep : StepBase
    {
        public override StepType StepType => StepType.DBOutput;

        public override IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            CancellationToken ct)
            => DbOutputExecutor.ExecuteStreamingAsync(SqliteProvider.Instance, Settings, input, progress, ct);

        public override string GetDisplayIcon() => "🪶";
    }
}
