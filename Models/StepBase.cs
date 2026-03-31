using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SampleELT.Models
{
    public abstract class StepBase
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public abstract StepType StepType { get; }
        public double CanvasX { get; set; }
        public double CanvasY { get; set; }
        public Dictionary<string, object?> Settings { get; set; } = new();

        public abstract Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct);

        public virtual string GetDisplayIcon()
        {
            return StepType switch
            {
                StepType.OracleInput => "🔶",
                StepType.OracleOutput => "🔶",
                StepType.MySQLInput => "🐬",
                StepType.MySQLOutput => "🐬",
                StepType.ExcelInput => "📗",
                StepType.ExcelOutput => "📗",
                StepType.Filter => "🔍",
                StepType.Calculation => "🧮",
                _ => "⚙️"
            };
        }
    }
}
