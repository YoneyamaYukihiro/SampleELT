using System;
using System.IO;
using BreezeFlow.Models;
using BreezeFlow.Services;
using Xunit;

namespace BreezeFlow.Tests.Services
{
    /// <summary>
    /// <see cref="RunHistoryProgressWriter"/> が ExecutionEngine の progress.Report 文字列を
    /// 正しく解析して run_steps / run_logs に記録することを検証する。
    /// </summary>
    public class RunHistoryProgressWriterTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly RunHistoryStore _store;
        private readonly long _runId;

        public RunHistoryProgressWriterTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(),
                $"breezeflow_writer_test_{Guid.NewGuid():N}.db");
            _store = new RunHistoryStore(_dbPath);
            _store.EnsureSchema();
            _runId = _store.BeginRun(new RunRecord
            {
                PipelineName = "test", Trigger = "manual", StartedAt = DateTime.Now
            });
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
            foreach (var ext in new[] { "-wal", "-shm" })
                try { var p = _dbPath + ext; if (File.Exists(p)) File.Delete(p); } catch { }
        }

        [Fact]
        public void StepStartAndDone_RecordedAsSuccess()
        {
            var w = new RunHistoryProgressWriter(_store, _runId, passthrough: null);
            w.Report("[DB Input] 実行中...");
            w.Report("[DB Input] 完了 (1234行)");

            var d = _store.GetDetail(_runId);
            Assert.NotNull(d);
            Assert.Single(d!.Steps);
            Assert.Equal("DB Input", d.Steps[0].StepName);
            Assert.Equal(RunStatus.Success, d.Steps[0].Status);
            Assert.Equal(1234, d.Steps[0].RowCount);
            Assert.Equal(1234, w.LastReportedRowCount);
        }

        [Fact]
        public void StepError_RecordedAsFailed_WithMessage()
        {
            var w = new RunHistoryProgressWriter(_store, _runId, passthrough: null);
            w.Report("[Calc] 実行中...");
            w.Report("[Calc] エラー: 0 で割れません");

            var d = _store.GetDetail(_runId);
            Assert.NotNull(d);
            Assert.Single(d!.Steps);
            Assert.Equal(RunStatus.Failed, d.Steps[0].Status);
            Assert.Contains("0 で割れません", d.Steps[0].ErrorMessage);
        }

        [Fact]
        public void Cancel_RecordedAsCancelled()
        {
            var w = new RunHistoryProgressWriter(_store, _runId, passthrough: null);
            w.Report("[X] 実行中...");
            w.Report("[X] キャンセルされました");

            var d = _store.GetDetail(_runId);
            Assert.Equal(RunStatus.Cancelled, d!.Steps[0].Status);
        }

        [Fact]
        public void FinishUnclosedSteps_ClosesOrphans()
        {
            // 完了ログが来なかったケース (パイプライン中断時)
            var w = new RunHistoryProgressWriter(_store, _runId, passthrough: null);
            w.Report("[A] 実行中...");
            w.Report("[B] 実行中...");
            w.FinishUnclosedSteps(RunStatus.Failed, "panic");

            var d = _store.GetDetail(_runId);
            Assert.NotNull(d);
            Assert.Equal(2, d!.Steps.Count);
            Assert.All(d.Steps, s =>
            {
                Assert.Equal(RunStatus.Failed, s.Status);
                Assert.Equal("panic", s.ErrorMessage);
            });
        }

        [Fact]
        public void AllLogs_ArePersisted()
        {
            var w = new RunHistoryProgressWriter(_store, _runId, passthrough: null);
            w.Report("パイプライン実行開始...");
            w.Report("[A] 実行中...");
            w.Report("[A] 完了 (10行)");
            w.Report("パイプライン実行完了");

            var d = _store.GetDetail(_runId);
            Assert.Equal(4, d!.Logs.Count);
        }

        [Fact]
        public void Passthrough_ReceivesAllMessages()
        {
            var received = new System.Collections.Generic.List<string>();
            var pass = new Progress<string>(received.Add);
            var w = new RunHistoryProgressWriter(_store, _runId, pass);

            w.Report("[A] 実行中...");
            w.Report("[A] 完了 (3行)");
            w.Report("パイプライン実行完了");

            // Progress<T> は SynchronizationContext 経由で非同期に発火する場合があるため軽く待つ
            for (int i = 0; i < 20 && received.Count < 3; i++) System.Threading.Thread.Sleep(10);
            Assert.Equal(3, received.Count);
        }

        [Fact]
        public void AggregateRowCount_SumsAllStepRows()
        {
            var w = new RunHistoryProgressWriter(_store, _runId, null);
            w.Report("[A] 実行中..."); w.Report("[A] 完了 (10行)");
            w.Report("[B] 実行中..."); w.Report("[B] 完了 (20行)");
            w.Report("[C] 実行中..."); w.Report("[C] 完了 (5行)");

            Assert.Equal(35, w.AggregateRowCount);
            Assert.Equal(5,  w.LastReportedRowCount);
        }
    }
}
