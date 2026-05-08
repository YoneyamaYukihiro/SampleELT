using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Engine;
using SampleELT.Models;
using SampleELT.Models.Stores;

namespace SampleELT.Steps
{
    /// <summary>
    /// 入力データをキーで検索し、一致すれば UPDATE、なければ INSERT するステップ (UPSERT)。
    /// 接続の DbType に応じた <see cref="DbProvider"/> を選び、<see cref="DbInsertUpdateExecutor"/> に委譲する。
    ///
    /// Settings["ConnectionId"]  : 接続ID
    /// Settings["TableName"]     : 対象テーブル名
    /// Settings["KeyFields"]     : カンマ区切りのキーフィールド名 (例: "id")
    /// Settings["UpdateFields"]  : カンマ区切りの更新フィールド名 (空ならキー以外の全フィールド)
    /// Settings["CommitSize"]    : コミットサイズ (省略時 100)
    /// </summary>
    public class InsertUpdateStep : StepBase
    {
        public override StepType StepType => StepType.InsertUpdate;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var connInfo = IConnectionStore.Default.FindConnection(Settings);
            var provider = DbProvider.For(connInfo?.DbType ?? DbType.MySQL);
            await DbInsertUpdateExecutor.ExecuteAsync(provider, Settings, inputData, progress, ct);
            return inputData;
        }

        public override string GetDisplayIcon() => "🔄";
    }
}
