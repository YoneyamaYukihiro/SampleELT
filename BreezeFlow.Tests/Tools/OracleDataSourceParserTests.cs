using SampleELT.Tools;
using Xunit;

namespace SampleELT.Tests.Tools
{
    public class OracleDataSourceParserTests
    {
        [Fact]
        public void EmptyString_ReturnsDefaults()
        {
            OracleDataSourceParser.Parse("", out var h, out var p, out var s);
            Assert.Equal("localhost", h);
            Assert.Equal("1521", p);
            Assert.Equal("ORCL", s);
        }

        [Fact]
        public void EzConnect_HostPortService_Parsed()
        {
            OracleDataSourceParser.Parse("163.141.231.40:1521/MES02", out var h, out var p, out var s);
            Assert.Equal("163.141.231.40", h);
            Assert.Equal("1521", p);
            Assert.Equal("MES02", s);
        }

        [Fact]
        public void EzConnect_HostService_Only_PortDefaultsTo1521()
        {
            OracleDataSourceParser.Parse("db01/PROD", out var h, out var p, out var s);
            Assert.Equal("db01", h);
            Assert.Equal("1521", p);
            Assert.Equal("PROD", s);
        }

        [Fact]
        public void TnsDescriptor_ServiceName_Parsed()
        {
            var tns = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=163.141.231.40)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=MES02)))";
            OracleDataSourceParser.Parse(tns, out var h, out var p, out var s);
            Assert.Equal("163.141.231.40", h);
            Assert.Equal("1521", p);
            Assert.Equal("MES02", s);
        }

        [Fact]
        public void TnsDescriptor_SID_Fallback()
        {
            var tns = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db01)(PORT=1521))(CONNECT_DATA=(SID=XE)))";
            OracleDataSourceParser.Parse(tns, out var h, out var p, out var s);
            Assert.Equal("db01", h);
            Assert.Equal("1521", p);
            Assert.Equal("XE", s);
        }

        [Fact]
        public void TnsDescriptor_QuotedAndPadded_Parsed()
        {
            var tns = "\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db.example.com)(PORT=1530))(CONNECT_DATA=(SERVICE_NAME=PROD)))\"";
            OracleDataSourceParser.Parse(tns, out var h, out var p, out var s);
            Assert.Equal("db.example.com", h);
            Assert.Equal("1530", p);
            Assert.Equal("PROD", s);
        }

        [Fact]
        public void TnsDescriptor_LowerCaseKeys_Parsed()
        {
            var tns = "(description=(address=(protocol=tcp)(host=db01)(port=1521))(connect_data=(service_name=ORCL)))";
            OracleDataSourceParser.Parse(tns, out var h, out var p, out var s);
            Assert.Equal("db01", h);
            Assert.Equal("1521", p);
            Assert.Equal("ORCL", s);
        }
    }
}
