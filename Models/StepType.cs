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
        MergeJoin,
        DBUpdate,
        SetVariable,
        DBInput,      // Oracle/MySQL 統合入力
        DBOutput,     // Oracle/MySQL 統合出力
        TableCompare  // 2 ストリームをキー + 列値で突き合わせ、差分を抽出
    }
}
