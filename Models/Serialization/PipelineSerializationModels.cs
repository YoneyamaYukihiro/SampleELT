using System;
using System.Collections.Generic;

namespace SampleELT.Models.Serialization
{
    /// <summary>パイプライン JSON のルート DTO。</summary>
    public class PipelineSerializationModel
    {
        public string Name { get; set; } = "New Pipeline";
        public LogMode LogMode { get; set; } = LogMode.OnError;
        public List<StepSerializationModel> Steps { get; set; } = new();
        public List<ConnectionSerializationModel> Connections { get; set; } = new();
    }

    public class StepSerializationModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string StepType { get; set; } = "";
        public double CanvasX { get; set; }
        public double CanvasY { get; set; }
        public double NodeWidth { get; set; } = 150.0;
        public double NodeHeight { get; set; } = 70.0;
        public Dictionary<string, string?> Settings { get; set; } = new();
    }

    public class ConnectionSerializationModel
    {
        public Guid Id { get; set; }
        public Guid SourceStepId { get; set; }
        public Guid TargetStepId { get; set; }
    }
}
