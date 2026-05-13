using System;
using System.Text.RegularExpressions;

namespace SampleELT.Tools
{
    /// <summary>
    /// Oracle の DataSource 文字列を host / port / service に分解する。
    /// EZ Connect (<c>host:port/service</c>) と TNS Descriptor
    /// (<c>(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=...)(PORT=...))(CONNECT_DATA=(SERVICE_NAME=...)))</c>)
    /// の両方をサポートする。
    /// OracleConnectionStringBuilder は保存時に EZ Connect を TNS Descriptor 形式へ
    /// 正規化することがあるため、再ロード時には両方を解釈する必要がある。
    /// </summary>
    public static class OracleDataSourceParser
    {
        public static void Parse(
            string dataSource, out string host, out string port, out string service)
        {
            host = "localhost"; port = "1521"; service = "ORCL";
            if (string.IsNullOrWhiteSpace(dataSource)) return;

            var trimmed = dataSource.Trim('"', ' ', '\t');

            if (trimmed.StartsWith("(", StringComparison.Ordinal)
                && trimmed.IndexOf("DESCRIPTION", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var h = MatchTns(trimmed, "HOST");
                var p = MatchTns(trimmed, "PORT");
                var s = MatchTns(trimmed, "SERVICE_NAME");
                if (string.IsNullOrEmpty(s)) s = MatchTns(trimmed, "SID");
                if (!string.IsNullOrEmpty(h)) host = h;
                if (!string.IsNullOrEmpty(p)) port = p;
                if (!string.IsNullOrEmpty(s)) service = s;
                return;
            }

            var colonIdx = trimmed.IndexOf(':');
            var slashIdx = trimmed.IndexOf('/');

            if (colonIdx > 0 && slashIdx > colonIdx)
            {
                host    = trimmed[..colonIdx];
                port    = trimmed[(colonIdx + 1)..slashIdx];
                service = trimmed[(slashIdx + 1)..];
            }
            else if (slashIdx > 0)
            {
                host    = trimmed[..slashIdx];
                service = trimmed[(slashIdx + 1)..];
            }
            else
            {
                host = trimmed; // TNS 名などそのまま使用
            }
        }

        private static string MatchTns(string descriptor, string key)
        {
            var pattern = $@"\(\s*{Regex.Escape(key)}\s*=\s*([^)\s]+)\s*\)";
            var match = Regex.Match(descriptor, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : "";
        }
    }
}
