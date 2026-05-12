using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SampleELT.Models
{
    public class Job
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Comment { get; set; } = "";
        public LogMode LogMode { get; set; } = LogMode.OnError;
        public List<JobStep> Steps { get; set; } = new();

        /// <summary>現在開いているファイルパス。JSON には含めない。</summary>
        [JsonIgnore]
        public string? FilePath { get; set; }
    }

    public class JobStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public int Order { get; set; }
        public string Name { get; set; } = "";
        public string PipelineFilePath { get; set; } = "";
        public bool ContinueOnError { get; set; } = false;
    }
}
