using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Steps
{
    public class FilterStepTests
    {
        private static List<Dictionary<string, object?>> MakeRows(params (string key, object? val)[] values)
        {
            var row = new Dictionary<string, object?>();
            foreach (var (k, v) in values) row[k] = v;
            return new List<Dictionary<string, object?>> { row };
        }

        private static async Task<List<Dictionary<string, object?>>> Run(
            List<Dictionary<string, object?>> input,
            string fieldName, string op, string value = "", string rightField = "")
        {
            var step = new FilterStep();
            step.Settings["FieldName"] = fieldName;
            step.Settings["Operator"] = op;
            step.Settings["Value"] = value;
            if (!string.IsNullOrEmpty(rightField))
                step.Settings["RightField"] = rightField;
            return await step.ExecuteAsync(input, new SyncProgress(), CancellationToken.None);
        }

        [Fact]
        public async Task Equals_Match_ReturnsRow()
        {
            var input = MakeRows(("Status", "active"));
            var result = await Run(input, "Status", "equals", "active");
            Assert.Single(result);
        }

        [Fact]
        public async Task Equals_NoMatch_ReturnsEmpty()
        {
            var input = MakeRows(("Status", "inactive"));
            var result = await Run(input, "Status", "equals", "active");
            Assert.Empty(result);
        }

        [Fact]
        public async Task Equals_CaseInsensitive_Match()
        {
            var input = MakeRows(("Status", "ACTIVE"));
            var result = await Run(input, "Status", "equals", "active");
            Assert.Single(result);
        }

        [Fact]
        public async Task NotEquals_Match_ReturnsRow()
        {
            var input = MakeRows(("Status", "inactive"));
            var result = await Run(input, "Status", "notEquals", "active");
            Assert.Single(result);
        }

        [Fact]
        public async Task Contains_Match_ReturnsRow()
        {
            var input = MakeRows(("Name", "John Doe"));
            var result = await Run(input, "Name", "contains", "John");
            Assert.Single(result);
        }

        [Fact]
        public async Task Contains_NoMatch_ReturnsEmpty()
        {
            var input = MakeRows(("Name", "Jane Doe"));
            var result = await Run(input, "Name", "contains", "John");
            Assert.Empty(result);
        }

        [Fact]
        public async Task GreaterThan_Numeric_Match()
        {
            var input = MakeRows(("Score", "90"));
            var result = await Run(input, "Score", "greaterThan", "80");
            Assert.Single(result);
        }

        [Fact]
        public async Task GreaterThan_Numeric_NoMatch()
        {
            var input = MakeRows(("Score", "70"));
            var result = await Run(input, "Score", "greaterThan", "80");
            Assert.Empty(result);
        }

        [Fact]
        public async Task LessThan_Numeric_Match()
        {
            var input = MakeRows(("Score", "70"));
            var result = await Run(input, "Score", "lessThan", "80");
            Assert.Single(result);
        }

        [Fact]
        public async Task IsNull_NullValue_ReturnsRow()
        {
            var input = MakeRows(("Value", null));
            var result = await Run(input, "Value", "isNull");
            Assert.Single(result);
        }

        [Fact]
        public async Task IsNull_NonNullValue_ReturnsEmpty()
        {
            var input = MakeRows(("Value", "something"));
            var result = await Run(input, "Value", "isNull");
            Assert.Empty(result);
        }

        [Fact]
        public async Task IsNotNull_NonNullValue_ReturnsRow()
        {
            var input = MakeRows(("Value", "something"));
            var result = await Run(input, "Value", "isNotNull");
            Assert.Single(result);
        }

        [Fact]
        public async Task IsNotNull_NullValue_ReturnsEmpty()
        {
            var input = MakeRows(("Value", null));
            var result = await Run(input, "Value", "isNotNull");
            Assert.Empty(result);
        }

        [Fact]
        public async Task RightField_FieldComparison_Match()
        {
            var row = new Dictionary<string, object?> { ["A"] = "hello", ["B"] = "hello" };
            var input = new List<Dictionary<string, object?>> { row };
            var result = await Run(input, "A", "equals", "", "B");
            Assert.Single(result);
        }

        [Fact]
        public async Task RightField_FieldComparison_NoMatch()
        {
            var row = new Dictionary<string, object?> { ["A"] = "hello", ["B"] = "world" };
            var input = new List<Dictionary<string, object?>> { row };
            var result = await Run(input, "A", "equals", "", "B");
            Assert.Empty(result);
        }

        [Fact]
        public async Task MissingField_ReturnsEmpty()
        {
            var input = MakeRows(("OtherField", "value"));
            var result = await Run(input, "MissingField", "equals", "value");
            Assert.Empty(result);
        }

        [Fact]
        public async Task MultipleRows_FiltersCorrectly()
        {
            var input = new List<Dictionary<string, object?>>
            {
                new() { ["Score"] = "90" },
                new() { ["Score"] = "60" },
                new() { ["Score"] = "85" },
            };
            var result = await Run(input, "Score", "greaterThan", "80");
            Assert.Equal(2, result.Count);
        }
    }
}
