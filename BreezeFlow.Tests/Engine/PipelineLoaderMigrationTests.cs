using System.IO;
using BreezeFlow.Engine;
using BreezeFlow.Steps;
using Xunit;

namespace BreezeFlow.Tests.Engine
{
    /// <summary>
    /// 旧 JSON フォーマット (SourceBranchKey 無し) を読み込んだとき、
    /// Filter ステップから出ている接続が "pass" にマイグレーションされることを検証する。
    /// </summary>
    public class PipelineLoaderMigrationTests
    {
        [Fact]
        public void FilterConnection_WithoutBranchKey_MigratedToPass()
        {
            // 旧 JSON フォーマット: 接続に SourceBranchKey フィールドが無い
            // ($"" の Filter Id / Dummy Id はリフレクション復元される)
            var json = @"{
  ""Name"": ""legacy"",
  ""Steps"": [
    {
      ""Id"": ""11111111-1111-1111-1111-111111111111"",
      ""Name"": ""F"",
      ""StepType"": ""Filter"",
      ""Settings"": { ""FieldName"": ""v"", ""Operator"": ""equals"", ""Value"": ""ok"" }
    },
    {
      ""Id"": ""22222222-2222-2222-2222-222222222222"",
      ""Name"": ""D"",
      ""StepType"": ""Dummy"",
      ""Settings"": {}
    }
  ],
  ""Connections"": [
    {
      ""SourceStepId"": ""11111111-1111-1111-1111-111111111111"",
      ""TargetStepId"": ""22222222-2222-2222-2222-222222222222""
    }
  ]
}";
            var tmpPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpPath, json);
                var p = PipelineLoader.LoadFromFile(tmpPath);

                Assert.Single(p.Connections);
                Assert.Equal(FilterStep.PassBranchKey, p.Connections[0].SourceBranchKey);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [Fact]
        public void FilterConnection_WithExplicitBranchKey_PreservesIt()
        {
            // 新規保存された JSON で fail が指定されている場合はそのまま尊重する。
            var json = @"{
  ""Name"": ""new"",
  ""Steps"": [
    {
      ""Id"": ""11111111-1111-1111-1111-111111111111"",
      ""Name"": ""F"",
      ""StepType"": ""Filter"",
      ""Settings"": { ""FieldName"": ""v"", ""Operator"": ""equals"", ""Value"": ""ok"" }
    },
    {
      ""Id"": ""22222222-2222-2222-2222-222222222222"",
      ""Name"": ""D"",
      ""StepType"": ""Dummy"",
      ""Settings"": {}
    }
  ],
  ""Connections"": [
    {
      ""SourceStepId"": ""11111111-1111-1111-1111-111111111111"",
      ""TargetStepId"": ""22222222-2222-2222-2222-222222222222"",
      ""SourceBranchKey"": ""fail""
    }
  ]
}";
            var tmpPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpPath, json);
                var p = PipelineLoader.LoadFromFile(tmpPath);

                Assert.Single(p.Connections);
                Assert.Equal(FilterStep.FailBranchKey, p.Connections[0].SourceBranchKey);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        [Fact]
        public void NonFilterConnection_WithoutBranchKey_RemainsNull()
        {
            // 単一ポートのステップから出る接続は null のまま (= 既定ポート)。
            var json = @"{
  ""Name"": ""s"",
  ""Steps"": [
    {
      ""Id"": ""11111111-1111-1111-1111-111111111111"",
      ""Name"": ""V"",
      ""StepType"": ""SetVariable"",
      ""Settings"": { ""Fields"": ""X=1"" }
    },
    {
      ""Id"": ""22222222-2222-2222-2222-222222222222"",
      ""Name"": ""D"",
      ""StepType"": ""Dummy"",
      ""Settings"": {}
    }
  ],
  ""Connections"": [
    {
      ""SourceStepId"": ""11111111-1111-1111-1111-111111111111"",
      ""TargetStepId"": ""22222222-2222-2222-2222-222222222222""
    }
  ]
}";
            var tmpPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tmpPath, json);
                var p = PipelineLoader.LoadFromFile(tmpPath);

                Assert.Single(p.Connections);
                Assert.Null(p.Connections[0].SourceBranchKey);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }
    }
}
