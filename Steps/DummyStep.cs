using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// 何もしないパススルーステップ。分岐の終端や処理の区切りに使用。
    /// </summary>
    public class DummyStep : StepBase
    {
        public override StepType StepType => StepType.Dummy;

        public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            int count = 0;
            await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
            {
                count++;
                yield return row;
            }
            progress.Report($"Dummy: {count}行 通過");
        }

        public override string GetDisplayIcon() => "⬜";
    }
}
