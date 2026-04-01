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
        public static StepType SelectValues => StepType.SelectValues;
        public static StepType DBDelete => StepType.DBDelete;
        public static StepType InsertUpdate => StepType.InsertUpdate;
        public static StepType ExecSQL => StepType.ExecSQL;
        public static StepType Dummy => StepType.Dummy;
        public static StepType GenerateRows => StepType.GenerateRows;
        public static StepType MergeJoin => StepType.MergeJoin;
        public static StepType DBUpdate => StepType.DBUpdate;
        public static StepType JavaScript => StepType.JavaScript;
    }
}
