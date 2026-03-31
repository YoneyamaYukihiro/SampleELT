using System.Collections.Generic;

namespace SampleELT.Models
{
    public class Pipeline
    {
        public string Name { get; set; } = "New Pipeline";
        public List<StepBase> Steps { get; set; } = new();
        public List<PipelineConnection> Connections { get; set; } = new();
    }
}
