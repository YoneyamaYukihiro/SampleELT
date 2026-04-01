namespace SampleELT.Models
{
    public enum StepType
    {
        OracleInput,
        MySQLInput,
        ExcelInput,
        OracleOutput,
        MySQLOutput,
        ExcelOutput,
        Filter,
        Calculation,
        SelectValues,
        DBDelete,
        InsertUpdate,
        ExecSQL,
        Dummy,
        GenerateRows,
        MergeJoin,
        DBUpdate,
        SetVariable
    }
}
