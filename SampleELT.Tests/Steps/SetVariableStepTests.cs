using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Steps
{
    public class SetVariableStepTests
    {
        private static async Task<List<Dictionary<string, object?>>> Run(
            string fields, string dateFormat = "yyyy/MM/dd")
        {
            var step = new SetVariableStep();
            step.Settings["Fields"]     = fields;
            step.Settings["DateFormat"] = dateFormat;
            return await step.ExecuteAsync(
                new List<Dictionary<string, object?>>(),
                new Progress<string>(),
                CancellationToken.None);
        }

        [Fact]
        public async Task OutputsExactlyOneRow()
        {
            var result = await Run("X=TODAY");
            Assert.Single(result);
        }

        [Fact]
        public async Task Today_FormatsWithDateFormat()
        {
            var result = await Run("D=TODAY", "yyyy/MM/dd");
            Assert.Equal(DateTime.Today.ToString("yyyy/MM/dd"), result[0]["D"]);
        }

        [Fact]
        public async Task Today_AlternativeDateFormat()
        {
            var result = await Run("D=TODAY", "yyyy-MM-dd");
            Assert.Equal(DateTime.Today.ToString("yyyy-MM-dd"), result[0]["D"]);
        }

        [Fact]
        public async Task Yesterday_IsOneDayBefore()
        {
            var result = await Run("D=YESTERDAY");
            Assert.Equal(DateTime.Today.AddDays(-1).ToString("yyyy/MM/dd"), result[0]["D"]);
        }

        [Fact]
        public async Task TodayMinusN_SubtractsDays()
        {
            var result = await Run("D=TODAY-7");
            Assert.Equal(DateTime.Today.AddDays(-7).ToString("yyyy/MM/dd"), result[0]["D"]);
        }

        [Fact]
        public async Task TodayPlusN_AddsDays()
        {
            var result = await Run("D=TODAY+3");
            Assert.Equal(DateTime.Today.AddDays(3).ToString("yyyy/MM/dd"), result[0]["D"]);
        }

        [Fact]
        public async Task MonthStart_IsFirstDayOfMonth()
        {
            var result = await Run("D=MONTH_START");
            var expected = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                .ToString("yyyy/MM/dd");
            Assert.Equal(expected, result[0]["D"]);
        }

        [Fact]
        public async Task MonthEnd_IsLastDayOfMonth()
        {
            var result = await Run("D=MONTH_END");
            var today = DateTime.Today;
            var expected = new DateTime(today.Year, today.Month,
                DateTime.DaysInMonth(today.Year, today.Month)).ToString("yyyy/MM/dd");
            Assert.Equal(expected, result[0]["D"]);
        }

        [Fact]
        public async Task YearStart_IsJanFirst()
        {
            var result = await Run("D=YEAR_START");
            Assert.Equal(new DateTime(DateTime.Today.Year, 1, 1).ToString("yyyy/MM/dd"), result[0]["D"]);
        }

        [Fact]
        public async Task YearEnd_IsDec31()
        {
            var result = await Run("D=YEAR_END");
            Assert.Equal(new DateTime(DateTime.Today.Year, 12, 31).ToString("yyyy/MM/dd"), result[0]["D"]);
        }

        [Fact]
        public async Task Literal_PassesThroughAsIs()
        {
            var result = await Run("LABEL=FIXED_VALUE");
            Assert.Equal("FIXED_VALUE", result[0]["LABEL"]);
        }

        [Fact]
        public async Task MultipleFields_AllPresent()
        {
            var result = await Run("FROM_DATE=TODAY-30\nTO_DATE=TODAY\nDEPT=01");
            Assert.Equal(3, result[0].Count);
            Assert.Equal(DateTime.Today.AddDays(-30).ToString("yyyy/MM/dd"), result[0]["FROM_DATE"]);
            Assert.Equal(DateTime.Today.ToString("yyyy/MM/dd"),              result[0]["TO_DATE"]);
            Assert.Equal("01",                                                result[0]["DEPT"]);
        }

        [Fact]
        public async Task EmptyLines_AreSkipped()
        {
            var result = await Run("A=1\n\n\nB=2");
            Assert.Equal(2, result[0].Count);
        }

        [Fact]
        public async Task CaseInsensitive_Today()
        {
            var result = await Run("D=today");
            Assert.Equal(DateTime.Today.ToString("yyyy/MM/dd"), result[0]["D"]);
        }
    }
}
