using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// 固定値の行を N 件生成するステップ。
    /// Settings["Fields"]   : "field=value" を改行区切り
    /// Settings["RowCount"] : 生成行数 (デフォルト 1)
    /// </summary>
    public class GenerateRowsStep : StepBase
    {
        public override StepType StepType => StepType.GenerateRows;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var fieldsRaw = Settings.TryGetValue("Fields", out var f) ? f?.ToString() ?? "" : "";
            int rowCount = 1;
            if (Settings.TryGetValue("RowCount", out var rc) && int.TryParse(rc?.ToString(), out var n))
                rowCount = Math.Max(1, n);

            // Parse field definitions
            var fieldDefs = new Dictionary<string, string?>();
            foreach (var line in fieldsRaw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = trimmed.Substring(0, eqIdx).Trim();
                    var val = eqIdx + 1 < trimmed.Length ? trimmed.Substring(eqIdx + 1).Trim() : null;
                    if (!string.IsNullOrEmpty(key))
                        fieldDefs[key] = val;
                }
                else
                {
                    fieldDefs[trimmed] = null;
                }
            }

            var result = new List<Dictionary<string, object?>>();
            for (int i = 0; i < rowCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var row = new Dictionary<string, object?>(fieldDefs.Count);
                foreach (var kv in fieldDefs)
                    row[kv.Key] = kv.Value;
                result.Add(row);
            }

            progress.Report($"Generate Rows: {result.Count}行 生成完了");
            return Task.FromResult(result);
        }

        public override string GetDisplayIcon() => "🔢";
    }
}
