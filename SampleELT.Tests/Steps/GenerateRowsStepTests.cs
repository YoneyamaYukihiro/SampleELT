using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Steps
{
    public class GenerateRowsStepTests
    {
        private static async Task<List<Dictionary<string, object?>>> Run(
            string fields, string rowCount = "1")
        {
            var step = new GenerateRowsStep();
            step.Settings["Fields"]   = fields;
            step.Settings["RowCount"] = rowCount;
            return await step.ExecuteAsync(
                new List<Dictionary<string, object?>>(),
                new SyncProgress(),
                CancellationToken.None);
        }

        [Fact]
        public async Task DefaultRowCount_GeneratesOneRow()
        {
            var result = await Run("A=1");
            Assert.Single(result);
        }

        [Fact]
        public async Task RowCount_GeneratesCorrectNumber()
        {
            var result = await Run("A=1", "5");
            Assert.Equal(5, result.Count);
        }

        [Fact]
        public async Task FieldValue_IsSet()
        {
            var result = await Run("STATUS=active");
            Assert.Equal("active", result[0]["STATUS"]);
        }

        [Fact]
        public async Task MultipleFields_AllPresent()
        {
            var result = await Run("A=1\nB=2\nC=3");
            Assert.Equal(3, result[0].Count);
            Assert.Equal("1", result[0]["A"]);
            Assert.Equal("2", result[0]["B"]);
            Assert.Equal("3", result[0]["C"]);
        }

        [Fact]
        public async Task AllRows_HaveSameFields()
        {
            var result = await Run("X=hello", "3");
            foreach (var row in result)
                Assert.Equal("hello", row["X"]);
        }

        [Fact]
        public async Task InvalidRowCount_DefaultsToOne()
        {
            var result = await Run("A=1", "abc");
            Assert.Single(result);
        }
    }
}
