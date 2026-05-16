using System;
using CommunityToolkit.Mvvm.ComponentModel;
using BreezeFlow.Models;

namespace BreezeFlow.ViewModels
{
    public partial class ConnectionViewModel : ObservableObject
    {
        public PipelineConnection Connection { get; }
        public StepNodeViewModel Source { get; }
        public StepNodeViewModel Target { get; }

        // Source: right edge, vertically at the port position (multi-port aware)
        public double X1 => Source.X + Source.NodeWidth;
        public double Y1 => Source.Y + Source.NodeHeight * Source.GetPortRelativeY(Connection.SourceBranchKey);

        // Target: left-center edge (input port position)
        public double X2 => Target.X;
        public double Y2 => Target.Y + Target.NodeHeight / 2;

        /// <summary>
        /// Angle (in degrees) for the arrowhead at the target end.
        /// 0 degrees = pointing right.
        /// </summary>
        public double ArrowAngle
        {
            get
            {
                var dx = X2 - X1;
                var dy = Y2 - Y1;
                return Math.Atan2(dy, dx) * 180.0 / Math.PI;
            }
        }

        /// <summary>
        /// 多ポートステップから出ている接続線に表示する短いラベル (BranchKey)。
        /// 単一ポート (BranchKey 空) の接続線では空文字を返す → XAML 側で非表示にする。
        /// </summary>
        public string BranchLabel => string.IsNullOrEmpty(Connection.SourceBranchKey)
            ? string.Empty
            : Connection.SourceBranchKey!;

        /// <summary>線の根元 (出力ポート寄り) にラベルを置く X 座標。</summary>
        public double LabelX => X1 + 4;

        /// <summary>線の根元にラベルを置く Y 座標 (ポートの少し下にオフセット)。</summary>
        public double LabelY => Y1 - 14;

        public ConnectionViewModel(PipelineConnection connection, StepNodeViewModel source, StepNodeViewModel target)
        {
            Connection = connection;
            Source = source;
            Target = target;

            Source.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(StepNodeViewModel.X) || e.PropertyName == nameof(StepNodeViewModel.Y)
                    || e.PropertyName == nameof(StepNodeViewModel.NodeWidth) || e.PropertyName == nameof(StepNodeViewModel.NodeHeight)
                    || e.PropertyName == nameof(StepNodeViewModel.OutputPorts))
                {
                    OnPropertyChanged(nameof(X1));
                    OnPropertyChanged(nameof(Y1));
                    OnPropertyChanged(nameof(LabelX));
                    OnPropertyChanged(nameof(LabelY));
                    OnPropertyChanged(nameof(ArrowAngle));
                }
            };

            Target.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(StepNodeViewModel.X) || e.PropertyName == nameof(StepNodeViewModel.Y)
                    || e.PropertyName == nameof(StepNodeViewModel.NodeWidth) || e.PropertyName == nameof(StepNodeViewModel.NodeHeight))
                {
                    OnPropertyChanged(nameof(X2));
                    OnPropertyChanged(nameof(Y2));
                    OnPropertyChanged(nameof(ArrowAngle));
                }
            };
        }

        /// <summary>BranchKey が外部から書き換えられた際に呼ぶ (ポート位置とラベルを再計算)。</summary>
        public void NotifyBranchKeyChanged()
        {
            OnPropertyChanged(nameof(Y1));
            OnPropertyChanged(nameof(LabelX));
            OnPropertyChanged(nameof(LabelY));
            OnPropertyChanged(nameof(BranchLabel));
            OnPropertyChanged(nameof(ArrowAngle));
        }
    }
}
