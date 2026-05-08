using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class MySQLInputStep : StepBase
    {
        public override StepType StepType => StepType.MySQLInput;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
            => DbInputExecutor.ExecuteAsync(MySqlProvider.Instance, Settings, inputData, progress, ct);

        public override string GetDisplayIcon() => "🐬";
    }
}
