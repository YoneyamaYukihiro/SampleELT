using System.Collections.Generic;

namespace BreezeFlow.Models
{
    /// <summary>
    /// ステップの出力ポート。多ポート (分岐) ステップはこれを複数返す。
    /// 単一出力のステップ (大多数) は <see cref="SinglePort"/> をそのまま使う。
    /// </summary>
    public sealed class OutputPort
    {
        /// <summary>
        /// 接続線が記録する識別子 (<see cref="PipelineConnection.SourceBranchKey"/>)。
        /// 空文字 = 既定 (単一出力ステップ)。
        /// </summary>
        public string Key { get; }

        /// <summary>UI に表示する短いラベル。指定が無ければ <see cref="Key"/> と同じ。</summary>
        public string Label { get; }

        public OutputPort(string key, string? label = null)
        {
            Key = key ?? string.Empty;
            Label = string.IsNullOrEmpty(label) ? Key : label!;
        }

        /// <summary>既定の単一出力ポート (Key="" / Label="")。</summary>
        public static readonly OutputPort Default = new(string.Empty);

        /// <summary>単一出力ステップ用の使い回し可能なリスト。</summary>
        public static readonly IReadOnlyList<OutputPort> SinglePort = new[] { Default };
    }

    /// <summary>
    /// 分岐ステップが出力する行を「どのポートから出たか」とセットで表す。
    /// <see cref="StepBase.ExecuteRoutedAsync"/> の戻り値で使う。
    /// </summary>
    public readonly record struct RoutedRow(string BranchKey, Dictionary<string, object?> Row);
}
