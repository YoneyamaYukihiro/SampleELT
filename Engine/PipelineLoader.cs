using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SampleELT.Models;
using SampleELT.Steps;
using SampleELT.ViewModels;

namespace SampleELT.Engine
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

            var pipeline = new Pipeline { Name = pipelineData.Name };

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
                    "GenerateRows" => new GenerateRowsStep(),
                    "MergeJoin"    => new MergeJoinStep(),
                    "DBUpdate"     => new DBUpdateStep(),
                    "SetVariable"  => new SetVariableStep(),
                    "DBInput"      => new DBInputStep(),
                    "DBOutput"     => new DBOutputStep(),
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

            foreach (var connData in pipelineData.Connections)
            {
                pipeline.Connections.Add(new PipelineConnection
                {
                    SourceStepId = connData.SourceStepId,
                    TargetStepId = connData.TargetStepId
                });
            }

            return pipeline;
        }
    }
}
