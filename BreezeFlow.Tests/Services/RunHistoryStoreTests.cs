using System;
using System.IO;
using System.Linq;
using BreezeFlow.Models;
using BreezeFlow.Services;
using Xunit;

namespace BreezeFlow.Tests.Services
{
    /// <summary>
    /// 一時ファイルに <see cref="RunHistoryStore"/> を立てて CRUD と検索を検証する。
    /// </summary>
    public class RunHistoryStoreTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly RunHistoryStore _store;

        public RunHistoryStoreTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(),
                $"breezeflow_runs_test_{Guid.NewGuid():N}.db");
            _store = new RunHistoryStore(_dbPath);
            _store.EnsureSchema();
        }

        public void Dispose()
        {
            // Microsoft.Data.Sqlite はプール接続を保持するので、明示的に破棄する
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
            // -wal / -shm ファイルも掃除
            foreach (var ext in new[] { "-wal", "-shm" })
                try { var p = _dbPath + ext; if (File.Exists(p)) File.Delete(p); } catch { }
        }

        [Fact]
        public void EnsureSchema_IsIdempotent()
        {
            _store.EnsureSchema();
            _store.EnsureSchema();
            // 例外が出なければ OK
        }

        [Fact]
        public void BeginRun_ReturnsPositiveId_AndSearchFindsIt()
        {
            var id = _store.BeginRun(new RunRecord
            {
                PipelinePath = @"C:\fake\daily.json",
                PipelineName = "daily",
                Trigger = "manual",
                StartedAt = DateTime.Now
            });
            Assert.True(id > 0);

            var rows = _store.Search(new RunSearchFilter { Limit = 10 });
            Assert.Single(rows);
            Assert.Equal("manual", rows[0].Trigger);
            Assert.Equal("daily", rows[0].PipelineName);
            Assert.Equal(RunStatus.Running, rows[0].Status);
        }

        [Fact]
        public void EndRun_UpdatesStatusAndDuration()
        {
            var id = _store.BeginRun(new RunRecord
            {
                PipelineName = "p",
                Trigger = "manual",
                StartedAt = DateTime.Now.AddSeconds(-2)
            });
            _store.EndRun(id, RunStatus.Success, null, totalRows: 100);

            var detail = _store.GetDetail(id);
            Assert.NotNull(detail);
            Assert.Equal(RunStatus.Success, detail!.Run.Status);
            Assert.True(detail.Run.DurationMs >= 0);
            Assert.Equal(100, detail.Run.TotalRows);
        }

        [Fact]
        public void StepLifecycle_StartAndEnd_AreRecorded()
        {
            var runId = _store.BeginRun(new RunRecord
            {
                PipelineName = "p", Trigger = "manual", StartedAt = DateTime.Now
            });
            var sid = _store.RecordStepStart(runId, 1, "DB Input", "DBInput");
            Assert.True(sid > 0);
            _store.RecordStepEnd(sid, RunStatus.Success, rowCount: 42, errorMessage: null);

            var detail = _store.GetDetail(runId);
            Assert.NotNull(detail);
            Assert.Single(detail!.Steps);
            Assert.Equal("DB Input", detail.Steps[0].StepName);
            Assert.Equal(42, detail.Steps[0].RowCount);
            Assert.Equal(RunStatus.Success, detail.Steps[0].Status);
        }

        [Fact]
        public void AppendLog_StoresMessages()
        {
            var runId = _store.BeginRun(new RunRecord
            {
                PipelineName = "p", Trigger = "manual", StartedAt = DateTime.Now
            });
            _store.AppendLog(runId, "INFO", "開始");
            _store.AppendLog(runId, "ERROR", "失敗しました");

            var detail = _store.GetDetail(runId);
            Assert.NotNull(detail);
            Assert.Equal(2, detail!.Logs.Count);
            Assert.Equal("INFO",  detail.Logs[0].Level);
            Assert.Equal("ERROR", detail.Logs[1].Level);
        }

        [Fact]
        public void Search_FiltersByStatus()
        {
            var ok    = _store.BeginRun(new RunRecord { PipelineName = "ok",   Trigger = "manual", StartedAt = DateTime.Now });
            var fail  = _store.BeginRun(new RunRecord { PipelineName = "fail", Trigger = "manual", StartedAt = DateTime.Now });
            _store.EndRun(ok,   RunStatus.Success, null, null);
            _store.EndRun(fail, RunStatus.Failed,  "err", null);

            var successOnly = _store.Search(new RunSearchFilter
            {
                Statuses = new[] { RunStatus.Success }, Limit = 10
            });
            Assert.Single(successOnly);
            Assert.Equal("ok", successOnly[0].PipelineName);
        }

        [Fact]
        public void Search_FiltersByPipelineNameLike()
        {
            _store.BeginRun(new RunRecord { PipelineName = "daily_aggregate", Trigger = "manual", StartedAt = DateTime.Now });
            _store.BeginRun(new RunRecord { PipelineName = "weekly_report",   Trigger = "manual", StartedAt = DateTime.Now });

            var hits = _store.Search(new RunSearchFilter { PipelineNameLike = "daily", Limit = 10 });
            Assert.Single(hits);
            Assert.Equal("daily_aggregate", hits[0].PipelineName);
        }

        [Fact]
        public void DeleteRun_CascadesToStepsAndLogs()
        {
            var runId = _store.BeginRun(new RunRecord
            {
                PipelineName = "p", Trigger = "manual", StartedAt = DateTime.Now
            });
            var sid = _store.RecordStepStart(runId, 1, "S", "Filter");
            _store.RecordStepEnd(sid, RunStatus.Success, 1, null);
            _store.AppendLog(runId, "INFO", "x");

            _store.DeleteRun(runId);

            var detail = _store.GetDetail(runId);
            Assert.Null(detail);

            // 検索からも消える
            Assert.Empty(_store.Search(new RunSearchFilter { Limit = 10 }));
        }

        [Fact]
        public void ApplyRetention_DeletesOldEntries()
        {
            // 過去 60 日前の run を直接 INSERT する代わりに、started_at を遠い過去で BeginRun
            var oldId = _store.BeginRun(new RunRecord
            {
                PipelineName = "old", Trigger = "manual",
                StartedAt = DateTime.Now.AddDays(-60)
            });
            _store.EndRun(oldId, RunStatus.Success, null, null);

            var newId = _store.BeginRun(new RunRecord
            {
                PipelineName = "new", Trigger = "manual",
                StartedAt = DateTime.Now
            });
            _store.EndRun(newId, RunStatus.Success, null, null);

            int deleted = _store.ApplyRetention(maxAgeDays: 30, maxRows: null);
            Assert.True(deleted >= 1);

            var remaining = _store.Search(new RunSearchFilter { Limit = 10 });
            Assert.Single(remaining);
            Assert.Equal("new", remaining[0].PipelineName);
        }
    }
}
