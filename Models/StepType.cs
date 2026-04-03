namespace SampleELT.Models
{
    public enum StepType
    {
        OracleInput,  // 後方互換のため保持
        MySQLInput,   // 後方互換のため保持
        ExcelInput,
        OracleOutput, // 後方互換のため保持
        MySQLOutput,  // 後方互換のため保持
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
        SetVariable,
        DBInput,      // Oracle/MySQL 統合入力
        DBOutput      // Oracle/MySQL 統合出力
    }
}
