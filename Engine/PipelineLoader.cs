using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BreezeFlow.Models;
using BreezeFlow.Models.Serialization;
using BreezeFlow.Steps;

namespace BreezeFlow.Engine
{
    /// <summary>
    /// JSON ファイルからパイプラインを復元する共有ユーティリティ。
    /// UI（MainViewModel）とヘッドレス CLI モード双方から使用する。
    /// </summary>
    public static class PipelineLoader
    {
        public static Pipeline LoadFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var pipelineData = JsonSerializer.Deserialize<PipelineSerializationModel>(json)
                ?? throw new InvalidOperationException("パイプラインの読み込みに失敗しました");

            var pipeline = new Pipeline
            {
                Name = pipelineData.Name,
                LogMode = pipelineData.LogMode
            };

            foreach (var stepData in pipelineData.Steps)
            {
                StepBase? step = stepData.StepType switch
                {
                    "OracleInput"  => new OracleInputStep(),
                    "MySQLInput"   => new MySQLInputStep(),
                    "ExcelInput"   => new ExcelInputStep(),
                    "OracleOutput" => new OracleOutputStep(),
                    "MySQLOutput"  => new MySQLOutputStep(),
                    "ExcelOutput"  => new ExcelOutputStep(),
                    "Filter"       => new FilterStep(),
                    "Calculation"  => new CalculationStep(),
                    "SelectValues" => new SelectValuesStep(),
                    "DBDelete"     => new DBDeleteStep(),
                    "InsertUpdate" => new InsertUpdateStep(),
                    "ExecSQL"      => new ExecSQLStep(),
                    "Dummy"        => new DummyStep(),
                    "MergeJoin"    => new MergeJoinStep(),
                    "DBUpdate"     => new DBUpdateStep(),
                    "SetVariable"  => new SetVariableStep(),
                    "DBInput"      => new DBInputStep(),
                    "DBOutput"     => new DBOutputStep(),
                    "TableCompare" => new TableCompareStep(),
                    "Switch"       => new SwitchStep(),
                    _              => null
                };

                if (step == null) continue;

                step.Name    = stepData.Name;
                step.CanvasX = stepData.CanvasX;
                step.CanvasY = stepData.CanvasY;
                step.Settings = stepData.Settings.ToDictionary(
                    kv => kv.Key,
                    kv => (object?)kv.Value
                );

                // Id は getter-only のためリフレクションで復元
                var idField = typeof(StepBase).GetField(
                    "<Id>k__BackingField",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                idField?.SetValue(step, stepData.Id);

                pipeline.Steps.Add(step);
            }

            // 接続を復元しつつ、旧 JSON との後方互換マイグレーションを適用する:
            // SourceBranchKey が未指定 (null) の接続について、ソースステップが Filter なら "pass" を補う。
            // 旧 Filter は単一出力 (一致行のみ通過) だったため、それと等価な動作を維持する。
            var stepLookup = pipeline.Steps.ToDictionary(s => s.Id);
            foreach (var connData in pipelineData.Connections)
            {
                var branchKey = connData.SourceBranchKey;
                if (branchKey == null
                    && stepLookup.TryGetValue(connData.SourceStepId, out var src)
                    && src is BreezeFlow.Steps.FilterStep)
                {
                    branchKey = BreezeFlow.Steps.FilterStep.PassBranchKey;
                }
                pipeline.Connections.Add(new PipelineConnection
                {
                    SourceStepId = connData.SourceStepId,
                    TargetStepId = connData.TargetStepId,
                    SourceBranchKey = branchKey
                });
            }

            return pipeline;
        }
    }
}
