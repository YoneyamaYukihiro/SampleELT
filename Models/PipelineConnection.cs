using System;

namespace BreezeFlow.Models
{
    public class PipelineConnection
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid SourceStepId { get; set; }
        public Guid TargetStepId { get; set; }

        /// <summary>
        /// 出力元ステップのどのポートから出ているか (<see cref="OutputPort.Key"/>)。
        /// null / 空 = 既定の単一ポート。旧 JSON との後方互換のため null を許す。
        /// Filter ステップに対してはロード時に null → "pass" へマイグレーションされる。
        /// </summary>
        public string? SourceBranchKey { get; set; }
    }
}
