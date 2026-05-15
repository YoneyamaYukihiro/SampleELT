using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Steps
{
    public class CalculationStepTests
    {
        private static async Task<List<Dictionary<string, object?>>> Run(
            Dictionary<string, object?> rowValues,
            string expressionType,
            string field1 = "",
            string field2 = "",
            string constant = "0",
            string outputField = "Result")
        {
            var step = new CalculationStep();
            step.Settings["OutputFieldName"] = outputField;
            step.Settings["ExpressionType"] = expressionType;
            step.Settings["Field1"] = field1;
            step.Settings["Field2"] = field2;
            step.Settings["Constant"] = constant;

            var input = new List<Dictionary<string, object?>> { rowValues };
            return await step.ExecuteAsync(input, new SyncProgress(), CancellationToken.None);
        }

        [Fact]
        public async Task Add_TwoFields_ReturnsSum()
        {
            var row = new Dictionary<string, object?> { ["A"] = "10", ["B"] = "5" };
            var result = await Run(row, "add", "A", "B");
            Assert.Equal(15.0, result[0]["Result"]);
        }

        [Fact]
        public async Task Subtract_TwoFields_ReturnsDifference()
        {
            var row = new Dictionary<string, object?> { ["A"] = "10", ["B"] = "3" };
            var result = await Run(row, "subtract", "A", "B");
            Assert.Equal(7.0, result[0]["Result"]);
        }

        [Fact]
        public async Task Multiply_TwoFields_ReturnsProduct()
        {
            var row = new Dictionary<string, object?> { ["A"] = "4", ["B"] = "5" };
            var result = await Run(row, "multiply", "A", "B");
            Assert.Equal(20.0, result[0]["Result"]);
        }

        [Fact]
        public async Task Divide_TwoFields_ReturnsQuotient()
        {
            var row = new Dictionary<string, object?> { ["A"] = "10", ["B"] = "4" };
            var result = await Run(row, "divide", "A", "B");
            Assert.Equal(2.5, result[0]["Result"]);
        }

        [Fact]
        public async Task Divide_ByZero_ReturnsNull()
        {
            var row = new Dictionary<string, object?> { ["A"] = "10", ["B"] = "0" };
            var result = await Run(row, "divide", "A", "B");
            Assert.Null(result[0]["Result"]);
        }

        [Fact]
        public async Task Concat_TwoFields_ReturnsConcatenated()
        {
            var row = new Dictionary<string, object?> { ["First"] = "Hello", ["Last"] = "World" };
            var result = await Run(row, "concat", "First", "Last");
            Assert.Equal("HelloWorld", result[0]["Result"]);
        }

        [Fact]
        public async Task Constant_NumericValue_ReturnsDouble()
        {
            var row = new Dictionary<string, object?>();
            var result = await Run(row, "constant", constant: "42");
            Assert.Equal(42.0, result[0]["Result"]);
        }

        [Fact]
        public async Task Constant_StringValue_ReturnsString()
        {
            var row = new Dictionary<string, object?>();
            var result = await Run(row, "constant", constant: "N/A");
            Assert.Equal("N/A", result[0]["Result"]);
        }

        [Fact]
        public async Task Add_NullField_TreatedAsZero()
        {
            var row = new Dictionary<string, object?> { ["A"] = null, ["B"] = "5" };
            var result = await Run(row, "add", "A", "B");
            Assert.Equal(5.0, result[0]["Result"]);
        }

        [Fact]
        public async Task Add_NonNumericField_TreatedAsZero()
        {
            var row = new Dictionary<string, object?> { ["A"] = "abc", ["B"] = "5" };
            var result = await Run(row, "add", "A", "B");
            Assert.Equal(5.0, result[0]["Result"]);
        }

        [Fact]
        public async Task OutputFieldName_CustomName_UsedCorrectly()
        {
            var row = new Dictionary<string, object?> { ["A"] = "3", ["B"] = "4" };
            var result = await Run(row, "add", "A", "B", outputField: "Total");
            Assert.True(result[0].ContainsKey("Total"));
            Assert.Equal(7.0, result[0]["Total"]);
        }

        [Fact]
        public async Task PreservesOriginalFields()
        {
            var row = new Dictionary<string, object?> { ["A"] = "3", ["B"] = "4" };
            var result = await Run(row, "add", "A", "B");
            Assert.Equal("3", result[0]["A"]);
            Assert.Equal("4", result[0]["B"]);
        }

        [Fact]
        public async Task DateDiffMinutes_DateTimeValues_ReturnsMinutes()
        {
            var dt1 = new DateTime(2026, 1, 1, 10, 30, 0);
            var dt2 = new DateTime(2026, 1, 1, 9, 0, 0);
            var row = new Dictionary<string, object?> { ["Start"] = dt2, ["End"] = dt1 };
            var result = await Run(row, "dateDiffMinutes", "End", "Start");
            Assert.Equal(90.0, result[0]["Result"]);
        }

        [Fact]
        public async Task MultipleRows_AllProcessed()
        {
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["A"] = "1", ["B"] = "2" },
                new() { ["A"] = "3", ["B"] = "4" },
                new() { ["A"] = "5", ["B"] = "6" },
            };
            var step = new CalculationStep();
            step.Settings["OutputFieldName"] = "Result";
            step.Settings["ExpressionType"] = "add";
            step.Settings["Field1"] = "A";
            step.Settings["Field2"] = "B";
            step.Settings["Constant"] = "0";
            var result = await step.ExecuteAsync(input, new SyncProgress(), CancellationToken.None);
            Assert.Equal(3, result.Count);
            Assert.Equal(3.0, result[0]["Result"]);
            Assert.Equal(7.0, result[1]["Result"]);
            Assert.Equal(11.0, result[2]["Result"]);
        }
    }
}
