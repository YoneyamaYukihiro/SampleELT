using SampleELT.Models;

namespace SampleELT.ViewModels
{
    /// <summary>
    /// Provides static StepType values for use in XAML CommandParameter bindings.
    /// </summary>
    public static class StepTypeHelper
    {
        public static StepType OracleInput => StepType.OracleInput;
        public static StepType MySQLInput => StepType.MySQLInput;
        public static StepType ExcelInput => StepType.ExcelInput;
        public static StepType OracleOutput => StepType.OracleOutput;
        public static StepType MySQLOutput => StepType.MySQLOutput;
        public static StepType ExcelOutput => StepType.ExcelOutput;
        public static StepType Filter => StepType.Filter;
        public static StepType Calculation => StepType.Calculation;
    }
}
