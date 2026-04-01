using System;
using System.Collections.Generic;
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

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            progress.Report($"Dummy: {inputData.Count}行 通過");
            return Task.FromResult(inputData);
        }

        public override string GetDisplayIcon() => "⬜";
    }
}
