using System;

namespace BreezeFlow.Models
{
    public class PipelineConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid SourceStepId { get; set; }
        public Guid TargetStepId { get; set; }
    }
}
