using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SampleELT.Models;

namespace SampleELT.ViewModels
{
    public partial class ConnectionViewModel : ObservableObject
    {
        public PipelineConnection Connection { get; }
        public StepNodeViewModel Source { get; }
        public StepNodeViewModel Target { get; }

        public double X1 => Source.X + Source.NodeWidth / 2;
        public double Y1 => Source.Y + Source.NodeHeight / 2;
        public double X2 => Target.X + Target.NodeWidth / 2;
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

        public ConnectionViewModel(PipelineConnection connection, StepNodeViewModel source, StepNodeViewModel target)
        {
            Connection = connection;
            Source = source;
            Target = target;

            Source.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(StepNodeViewModel.X) || e.PropertyName == nameof(StepNodeViewModel.Y)
                    || e.PropertyName == nameof(StepNodeViewModel.NodeWidth) || e.PropertyName == nameof(StepNodeViewModel.NodeHeight))
                {
                    OnPropertyChanged(nameof(X1));
                    OnPropertyChanged(nameof(Y1));
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
    }
}
