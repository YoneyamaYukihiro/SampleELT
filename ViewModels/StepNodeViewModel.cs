using CommunityToolkit.Mvvm.ComponentModel;
using SampleELT.Models;

namespace SampleELT.ViewModels
{
    public partial class StepNodeViewModel : ObservableObject
    {
        public StepBase Step { get; }

        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isConnectionTarget;

        [ObservableProperty]
        private bool _isConnectingMode;

        public string DisplayName => Step.Name;
        public string DisplayIcon => Step.GetDisplayIcon();

        public string TypeLabel => Step.StepType switch
        {
            StepType.OracleInput => "Oracle Input",
            StepType.MySQLInput => "MySQL Input",
            StepType.ExcelInput => "Excel Input",
            StepType.OracleOutput => "Oracle Output",
            StepType.MySQLOutput => "MySQL Output",
            StepType.ExcelOutput => "Excel Output",
            StepType.Filter => "Filter",
            StepType.Calculation => "Calculation",
            StepType.SelectValues => "Select Values",
            StepType.DBDelete => "DB Delete",
            StepType.InsertUpdate => "Insert/Update",
            StepType.ExecSQL => "Exec SQL",
            _ => "Unknown"
        };

        public StepNodeViewModel(StepBase step)
        {
            Step = step;
            _x = step.CanvasX;
            _y = step.CanvasY;
        }

        partial void OnXChanged(double value)
        {
            Step.CanvasX = value;
        }

        partial void OnYChanged(double value)
        {
            Step.CanvasY = value;
        }

        public void NotifyNameChanged()
        {
            OnPropertyChanged(nameof(DisplayName));
        }
    }
}
