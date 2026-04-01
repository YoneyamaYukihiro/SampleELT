using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// フィールドの選択・リネームを行うステップ。
    /// Settings["FieldMappings"] には "source=dest" または "fieldname" を改行区切りで指定。
    /// 記述されたフィールドのみ出力し、未記述のフィールドは除去する。
    /// </summary>
    public class SelectValuesStep : StepBase
    {
        public override StepType StepType => StepType.SelectValues;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var mappingsRaw = Settings.TryGetValue("FieldMappings", out var m) ? m?.ToString() ?? "" : "";

            // Parse mappings: each line is "source=dest" or just "fieldname"
            var mappings = new List<(string Source, string Dest)>();
            foreach (var line in mappingsRaw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var src = trimmed.Substring(0, eqIdx).Trim();
                    var dst = trimmed.Substring(eqIdx + 1).Trim();
                    if (!string.IsNullOrEmpty(src))
                        mappings.Add((src, string.IsNullOrEmpty(dst) ? src : dst));
                }
                else
                {
                    mappings.Add((trimmed, trimmed));
                }
            }

            var result = new List<Dictionary<string, object?>>();

            if (mappings.Count == 0)
            {
                // No mapping defined: pass through all data unchanged
                progress.Report($"Select Values: マッピング未定義 - {inputData.Count}行 そのまま通過");
                return Task.FromResult(inputData);
            }

            foreach (var row in inputData)
            {
                ct.ThrowIfCancellationRequested();
                var newRow = new Dictionary<string, object?>();
                foreach (var (src, dst) in mappings)
                {
                    if (row.TryGetValue(src, out var val))
                        newRow[dst] = val;
                }
                result.Add(newRow);
            }

            progress.Report($"Select Values: {result.Count}行 変換完了");
            return Task.FromResult(result);
        }

        public override string GetDisplayIcon() => "📋";
    }
}
