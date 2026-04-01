using System;
using System.Collections.Generic;

namespace SampleELT.Tests
{
    /// <summary>
    /// Progress&lt;T&gt; は非同期ポストのため await 後でもコールバック未実行の場合がある。
    /// テストでは同期実行する実装を使用する。
    /// </summary>
    internal sealed class SyncProgress : IProgress<string>
    {
        private readonly List<string>? _log;
        public SyncProgress(List<string>? log = null) => _log = log;
        public void Report(string value) => _log?.Add(value);
    }
}
