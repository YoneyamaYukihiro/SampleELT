using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Engine;
using BreezeFlow.Models;
using BreezeFlow.Models.Stores;

namespace BreezeFlow.Steps
{
    /// <summary>
    /// 入力データのキーで検索し、一致した場合のみ UPDATE するステップ (INSERT しない)。
    /// 接続の DbType に応じた <see cref="DbProvider"/> を選び、<see cref="DbUpdateExecutor"/> に委譲する。
    ///
    /// Settings["ConnectionId"]  : 接続ID
    /// Settings["TableName"]     : 対象テーブル名
    /// Settings["KeyFields"]     : カンマ区切りのキーフィールド名
    /// Settings["UpdateFields"]  : カンマ区切りの更新フィールド名 (空ならキー以外の全フィールド)
    /// Settings["CommitSize"]    : コミットサイズ (省略時 100)
    /// </summary>
    public class DBUpdateStep : StepBase
    {
        public override StepType StepType => StepType.DBUpdate;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connInfo = IConnectionStore.Default.FindConnection(Settings);
            var provider = DbProvider.For(connInfo?.DbType ?? DbType.MySQL);
            await DbUpdateExecutor.ExecuteAsync(provider, Settings, inputData, progress, ct);
            return inputData;
        }

        public override string GetDisplayIcon() => "✏️";
    }
}
