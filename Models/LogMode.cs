namespace SampleELT.Models
{
    /// <summary>
    /// CLI ヘッドレス実行 (--run / --run-job) でのログファイル出力モード。
    /// UI 実行時のログ表示には影響しない。
    /// </summary>
    public enum LogMode
    {
        /// <summary>エラー発生時のみ .log を書き出す（既定）。</summary>
        OnError = 0,

        /// <summary>常に .log を書き出す。</summary>
        Always = 1,

        /// <summary>常に .log を書き出さない（エラーでも出さない）。</summary>
        Never = 2
    }
}
