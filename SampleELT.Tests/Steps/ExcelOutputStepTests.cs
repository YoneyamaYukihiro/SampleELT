using System;
using SampleELT.Steps;
using Xunit;

namespace SampleELT.Tests.Steps
{
    public class ExcelOutputStepTests
    {
        private static readonly DateTime FixedDate = new DateTime(2026, 4, 3, 14, 30, 22);

        [Fact]
        public void ResolveDateTokens_NoTokens_ReturnsUnchanged()
        {
            var result = ExcelOutputStep.ResolveDateTokens("output.xlsx", FixedDate);
            Assert.Equal("output.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_yyyyMMdd_ReplacesCorrectly()
        {
            var result = ExcelOutputStep.ResolveDateTokens("output_{yyyyMMdd}.xlsx", FixedDate);
            Assert.Equal("output_20260403.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_DashDate_ReplacesCorrectly()
        {
            var result = ExcelOutputStep.ResolveDateTokens("report_{yyyy-MM-dd}.csv", FixedDate);
            Assert.Equal("report_2026-04-03.csv", result);
        }

        [Fact]
        public void ResolveDateTokens_HHmmss_ReplacesCorrectly()
        {
            var result = ExcelOutputStep.ResolveDateTokens("log_{HHmmss}.txt", FixedDate);
            Assert.Equal("log_143022.txt", result);
        }

        [Fact]
        public void ResolveDateTokens_MultipleTokens_AllReplaced()
        {
            var result = ExcelOutputStep.ResolveDateTokens("data_{yyyyMMdd}_{HHmmss}.xlsx", FixedDate);
            Assert.Equal("data_20260403_143022.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_YearOnly_ReplacesCorrectly()
        {
            var result = ExcelOutputStep.ResolveDateTokens("{yyyy}.xlsx", FixedDate);
            Assert.Equal("2026.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_MonthOnly_ReplacesCorrectly()
        {
            var result = ExcelOutputStep.ResolveDateTokens("{MM}.xlsx", FixedDate);
            Assert.Equal("04.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_DayOnly_ReplacesCorrectly()
        {
            var result = ExcelOutputStep.ResolveDateTokens("{dd}.xlsx", FixedDate);
            Assert.Equal("03.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_EmptyPath_ReturnsEmpty()
        {
            var result = ExcelOutputStep.ResolveDateTokens("", FixedDate);
            Assert.Equal("", result);
        }

        [Fact]
        public void ResolveDateTokens_NoAtParam_UsesCurrentTime()
        {
            // Without a fixed DateTime, uses DateTime.Now — just verify it doesn't throw
            var result = ExcelOutputStep.ResolveDateTokens("output_{yyyyMMdd}.xlsx");
            Assert.Matches(@"output_\d{8}\.xlsx", result);
        }

        [Fact]
        public void ResolveDateTokens_PathWithDirectory_PreservesDirectory()
        {
            var result = ExcelOutputStep.ResolveDateTokens(@"C:\reports\data_{yyyyMMdd}.xlsx", FixedDate);
            Assert.Equal(@"C:\reports\data_20260403.xlsx", result);
        }
    }
}
