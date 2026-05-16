using System.Windows;

namespace BreezeFlow.Controls
{
    /// <summary>
    /// <see cref="StepNodeControl.OutputPortDragStartedEvent"/> の払い出しイベント引数。
    /// 多ポート対応のため、どのポート (<see cref="OutputPort.Key"/>) からドラッグが始まったかを伝える。
    /// </summary>
    public class OutputPortDragEventArgs : RoutedEventArgs
    {
        /// <summary>ドラッグ開始元の出力ポート Key (BranchKey)。単一ポートのステップでは空文字。</summary>
        public string BranchKey { get; }

        public OutputPortDragEventArgs(RoutedEvent routedEvent, object source, string branchKey)
            : base(routedEvent, source)
        {
            BranchKey = branchKey ?? string.Empty;
        }
    }
}
