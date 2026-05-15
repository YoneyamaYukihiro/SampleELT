using System;
using System.Collections.Generic;
using System.Linq;
using BreezeFlow.Engine;
using BreezeFlow.Models;
using BreezeFlow.Models.Stores;
using BreezeFlow.Steps;
using Xunit;

namespace BreezeFlow.Tests.Engine
{
    /// <summary>
    /// PipelineSafetyChecker が「未解決接続 / Read-only 接続への書き込み / Production 書き込み」を
    /// 適切に検出することを検証する。
    /// 静的シングルトン (IConnectionStore.Default) を差し替えるため、同じシングルトンを
    /// 差し替える他テストと並列に走らないよう ConnectionStoreCollection に属させる。
    /// </summary>
    [Collection("ConnectionStore")]
    public class PipelineSafetyCheckerTests : IDisposable
    {
        private readonly IConnectionStore _originalStore;
        private readonly FakeStore _fake;

        public PipelineSafetyCheckerTests()
        {
            _originalStore = IConnectionStore.Default;
            _fake = new FakeStore();
            IConnectionStore.Default = _fake;
        }

        public void Dispose()
        {
            IConnectionStore.Default = _originalStore;
        }

        [Fact]
        public void EmptyPipeline_NoIssues()
        {
            var pipeline = new Pipeline();
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Empty(issues);
        }

        [Fact]
        public void StepWithoutConnectionId_NotChecked()
        {
            var pipeline = new Pipeline();
            pipeline.Steps.Add(new FilterStep { Name = "filter1" }); // ConnectionId 持たない
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Empty(issues);
        }

        [Fact]
        public void UnresolvedConnectionId_IsBlock()
        {
            var pipeline = new Pipeline();
            pipeline.Steps.Add(MakeStep<DBOutputStep>("out1", Guid.NewGuid())); // 未登録
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Single(issues);
            Assert.Equal(PipelineSafetyChecker.IssueSeverity.Block, issues[0].Severity);
        }

        [Fact]
        public void ReadOnlyConnection_OnWriteStep_IsBlock()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo
            {
                Id = id,
                Name = "ro-conn",
                IsReadOnly = true,
                Environment = DbEnvironment.Development
            });
            var pipeline = new Pipeline();
            pipeline.Steps.Add(MakeStep<DBDeleteStep>("delete1", id));
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Single(issues);
            Assert.Equal(PipelineSafetyChecker.IssueSeverity.Block, issues[0].Severity);
        }

        [Fact]
        public void ReadOnlyConnection_OnReadStep_NoIssue()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo { Id = id, Name = "ro-conn", IsReadOnly = true });
            var pipeline = new Pipeline();
            pipeline.Steps.Add(MakeStep<DBInputStep>("in1", id));
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Empty(issues);
        }

        [Fact]
        public void ProductionConnection_OnWriteStep_IsConfirm()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo
            {
                Id = id,
                Name = "prod-conn",
                Environment = DbEnvironment.Production
            });
            var pipeline = new Pipeline();
            pipeline.Steps.Add(MakeStep<DBOutputStep>("out1", id));
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Single(issues);
            Assert.Equal(PipelineSafetyChecker.IssueSeverity.Confirm, issues[0].Severity);
        }

        [Fact]
        public void ProductionConnection_OnReadStep_NoIssue()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo
            {
                Id = id,
                Name = "prod-read",
                Environment = DbEnvironment.Production
            });
            var pipeline = new Pipeline();
            pipeline.Steps.Add(MakeStep<DBInputStep>("in1", id));
            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Empty(issues);
        }

        [Fact]
        public void ExecSql_SelectOnly_OnProduction_NoIssue()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo
            {
                Id = id,
                Name = "prod-conn",
                Environment = DbEnvironment.Production
            });
            var pipeline = new Pipeline();
            var step = MakeStep<ExecSQLStep>("sql1", id);
            step.Settings["SQL"] = "SELECT COUNT(*) FROM orders";
            pipeline.Steps.Add(step);

            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Empty(issues);
        }

        [Fact]
        public void ExecSql_Update_OnProduction_IsConfirm()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo
            {
                Id = id,
                Name = "prod-conn",
                Environment = DbEnvironment.Production
            });
            var pipeline = new Pipeline();
            var step = MakeStep<ExecSQLStep>("sql1", id);
            step.Settings["SQL"] = "UPDATE orders SET status='OK' WHERE id=1";
            pipeline.Steps.Add(step);

            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Single(issues);
            Assert.Equal(PipelineSafetyChecker.IssueSeverity.Confirm, issues[0].Severity);
        }

        [Fact]
        public void ExecSql_DeleteOnReadOnly_IsBlock()
        {
            var id = Guid.NewGuid();
            _fake.Add(new DbConnectionInfo
            {
                Id = id,
                Name = "ro-conn",
                IsReadOnly = true,
                Environment = DbEnvironment.Production
            });
            var pipeline = new Pipeline();
            var step = MakeStep<ExecSQLStep>("sql1", id);
            step.Settings["SQL"] = "DELETE FROM orders";
            pipeline.Steps.Add(step);

            var issues = PipelineSafetyChecker.Check(pipeline);
            Assert.Single(issues);
            Assert.Equal(PipelineSafetyChecker.IssueSeverity.Block, issues[0].Severity);
        }

        private static T MakeStep<T>(string name, Guid connId) where T : StepBase, new()
        {
            var step = new T { Name = name };
            step.Settings["ConnectionId"] = connId.ToString();
            return step;
        }

        private class FakeStore : IConnectionStore
        {
            private readonly Dictionary<Guid, DbConnectionInfo> _map = new();
            public void Add(DbConnectionInfo info) => _map[info.Id] = info;

            public DbConnectionInfo? GetById(Guid id) => _map.TryGetValue(id, out var c) ? c : null;
            public string? GetConnectionString(Guid id) => GetById(id)?.ConnectionString;
            public DbConnectionInfo? FindConnection(Dictionary<string, object?> settings)
            {
                if (settings.TryGetValue("ConnectionId", out var idObj)
                    && idObj != null
                    && Guid.TryParse(idObj.ToString(), out var id))
                    return GetById(id);
                return null;
            }
            public string ResolveConnectionString(Dictionary<string, object?> settings)
                => FindConnection(settings)?.ConnectionString ?? "";
        }
    }
}
