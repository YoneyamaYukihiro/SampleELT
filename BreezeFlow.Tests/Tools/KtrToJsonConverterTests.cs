using System.Linq;
using System.Text.Json;
using BreezeFlow.Tools;
using Xunit;

namespace BreezeFlow.Tests.Tools
{
    /// <summary>
    /// Strategy 化リファクタ後の KtrToJsonConverter が代表的な KTR ステップを
    /// 正しい BreezeFlow パイプライン JSON に変換することを確認する。
    /// </summary>
    public class KtrToJsonConverterTests
    {
        private static readonly string MinimalKtrXml = @"<?xml version='1.0' encoding='UTF-8'?>
<transformation>
  <info>
    <name>SampleConversion</name>
  </info>
  <step>
    <name>Read Source</name>
    <type>TableInput</type>
    <connection>local-mysql</connection>
    <sql>SELECT id, name FROM users</sql>
    <execute_each_row>N</execute_each_row>
    <GUI><xloc>100</xloc><yloc>200</yloc></GUI>
  </step>
  <step>
    <name>Filter Active</name>
    <type>FilterRows</type>
    <condition>
      <leftvalue>status</leftvalue>
      <function>=</function>
      <value><text>active</text></value>
    </condition>
    <GUI><xloc>250</xloc><yloc>200</yloc></GUI>
  </step>
  <step>
    <name>Write Target</name>
    <type>TableOutput</type>
    <connection>local-mysql</connection>
    <table>users_archive</table>
    <commit>50</commit>
    <GUI><xloc>400</xloc><yloc>200</yloc></GUI>
  </step>
  <order>
    <hop><from>Read Source</from><to>Filter Active</to><enabled>Y</enabled></hop>
    <hop><from>Filter Active</from><to>Write Target</to><enabled>Y</enabled></hop>
  </order>
  <connection>
    <name>local-mysql</name>
    <type>MYSQL</type>
    <server>localhost</server>
    <database>testdb</database>
    <username>tester</username>
  </connection>
</transformation>";

        [Fact]
        public void Convert_BuildsPipelineWithExpectedSteps()
        {
            var result = KtrToJsonConverter.ConvertFromString(MinimalKtrXml);

            Assert.Equal("SampleConversion", result.PipelineName);

            using var doc = JsonDocument.Parse(result.PipelineJson);
            var root = doc.RootElement;

            Assert.Equal("SampleConversion", root.GetProperty("Name").GetString());
            var steps = root.GetProperty("Steps");
            Assert.Equal(3, steps.GetArrayLength());

            var stepTypes = steps.EnumerateArray()
                .Select(s => s.GetProperty("StepType").GetString())
                .ToList();
            Assert.Contains("DBInput", stepTypes);
            Assert.Contains("Filter", stepTypes);
            Assert.Contains("DBOutput", stepTypes);

            // FilterRows = → equals に変換されているか
            var filter = steps.EnumerateArray().First(s => s.GetProperty("StepType").GetString() == "Filter");
            Assert.Equal("equals",
                filter.GetProperty("Settings").GetProperty("Operator").GetString());
            Assert.Equal("status",
                filter.GetProperty("Settings").GetProperty("FieldName").GetString());
            Assert.Equal("active",
                filter.GetProperty("Settings").GetProperty("Value").GetString());

            // TableOutput の commit が CommitSize に
            var output = steps.EnumerateArray().First(s => s.GetProperty("StepType").GetString() == "DBOutput");
            Assert.Equal("users_archive",
                output.GetProperty("Settings").GetProperty("TableName").GetString());
            Assert.Equal("50",
                output.GetProperty("Settings").GetProperty("CommitSize").GetString());

            // ホップが 2 本作成されている
            Assert.Equal(2, root.GetProperty("Connections").GetArrayLength());
        }

        [Fact]
        public void Convert_UnknownStepType_FallbackToDummyWithWarning()
        {
            var ktr = @"<?xml version='1.0' encoding='UTF-8'?>
<transformation>
  <info><name>P</name></info>
  <step>
    <name>Mystery</name>
    <type>SomeNewKtrTypeIDoNotKnow</type>
    <GUI><xloc>0</xloc><yloc>0</yloc></GUI>
  </step>
</transformation>";
            var result = KtrToJsonConverter.ConvertFromString(ktr);

            using var doc = JsonDocument.Parse(result.PipelineJson);
            var step = doc.RootElement.GetProperty("Steps").EnumerateArray().First();
            Assert.Equal("Dummy", step.GetProperty("StepType").GetString());
            Assert.Equal("SomeNewKtrTypeIDoNotKnow",
                step.GetProperty("Settings").GetProperty("OriginalKtrType").GetString());
            Assert.Contains(result.Warnings, w => w.Contains("未対応のステップ種別"));
        }

        [Fact]
        public void Convert_ExecSql_BothExecSQLAndExecSQLRowMapToExecSQLStepType()
        {
            var ktr = @"<?xml version='1.0' encoding='UTF-8'?>
<transformation>
  <info><name>P</name></info>
  <step>
    <name>Sql1</name>
    <type>ExecSQL</type>
    <connection>conn</connection>
    <sql>DELETE FROM x</sql>
    <GUI><xloc>0</xloc><yloc>0</yloc></GUI>
  </step>
  <step>
    <name>Sql2</name>
    <type>ExecSQLRow</type>
    <connection>conn</connection>
    <sql>UPDATE y SET z=1</sql>
    <execute_each_row>Y</execute_each_row>
    <GUI><xloc>100</xloc><yloc>0</yloc></GUI>
  </step>
  <connection>
    <name>conn</name>
    <type>MYSQL</type>
    <server>localhost</server>
    <database>db</database>
    <username>u</username>
  </connection>
</transformation>";
            var result = KtrToJsonConverter.ConvertFromString(ktr);

            using var doc = JsonDocument.Parse(result.PipelineJson);
            var stepTypes = doc.RootElement.GetProperty("Steps").EnumerateArray()
                .Select(s => s.GetProperty("StepType").GetString())
                .ToList();
            Assert.Equal(2, stepTypes.Count);
            Assert.All(stepTypes, t => Assert.Equal("ExecSQL", t));

            // ExecuteEachRow フラグの保存を確認
            var execSqls = doc.RootElement.GetProperty("Steps").EnumerateArray().ToList();
            var sql2 = execSqls.First(s => s.GetProperty("Name").GetString() == "Sql2");
            Assert.Equal("true", sql2.GetProperty("Settings").GetProperty("ExecuteEachRow").GetString());
            var sql1 = execSqls.First(s => s.GetProperty("Name").GetString() == "Sql1");
            Assert.Equal("false", sql1.GetProperty("Settings").GetProperty("ExecuteEachRow").GetString());
        }
    }
}
