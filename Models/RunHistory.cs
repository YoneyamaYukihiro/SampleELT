using System;
using System.Collections.Generic;

namespace BreezeFlow.Models
{
    /// <summary>パイプライン / ジョブ 1 回分の実行ヘッダ。</summary>
    public class RunRecord
    {
        public long Id { get; set; }
        public Guid RunGuid { get; set; } = Guid.NewGuid();

        /// <summary>パイプライン JSON のフルパス (パイプライン実行時)。</summary>
        public string? PipelinePath { get; set; }
        public string? PipelineName { get; set; }

        /// <summary>ジョブ JSON のフルパス (ジョブ内のパイプライン実行時のみセット)。</summary>
        public string? JobPath { get; set; }
        public string? JobName { get; set; }

        /// <summary>"manual" / "schedule" / "cli" / "job" のいずれか。</summary>
        public string Trigger { get; set; } = "manual";

        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public long? DurationMs { get; set; }

        public RunStatus Status { get; set; } = RunStatus.Running;
        public string? ErrorMessage { get; set; }
        public int? TotalRows { get; set; }
    }

    public class RunStepRecord
    {
        public long Id { get; set; }
        public long RunId { get; set; }
        public int StepOrder { get; set; }
        public string StepName { get; set; } = "";
        public string StepType { get; set; } = "";
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public long? DurationMs { get; set; }
        public int? RowCount { get; set; }
        public RunStatus Status { get; set; } = RunStatus.Running;
        public string? ErrorMessage { get; set; }
    }

    public class RunLogEntry
    {
        public long Id { get; set; }
        public long RunId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "INFO";
        public string Message { get; set; } = "";
    }

    public enum RunStatus
    {
        Running,
        Success,
        Failed,
        Cancelled
    }

    /// <summary>RunHistoryStore.Search() に渡す検索条件。</summary>
    public class RunSearchFilter
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string? PipelineNameLike { get; set; }
        public string? JobNameLike { get; set; }
        public IReadOnlyCollection<RunStatus>? Statuses { get; set; }
        public IReadOnlyCollection<string>? Triggers { get; set; }
        public int Limit { get; set; } = 200;
    }

    /// <summary>1 run の詳細 (ヘッダ + ステップ + ログ)。RunHistoryDialog の右ペインで表示。</summary>
    public class RunDetail
    {
        public RunRecord Run { get; set; } = new();
        public List<RunStepRecord> Steps { get; set; } = new();
        public List<RunLogEntry> Logs { get; set; } = new();
    }
}
