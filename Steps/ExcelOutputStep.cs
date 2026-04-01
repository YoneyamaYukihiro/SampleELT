using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OfficeOpenXml;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// ファイル出力ステップ (Excel / CSV / TSV / TXT)。
    /// Settings["FilePath"]     : 出力先ファイルパス
    /// Settings["Format"]       : "Excel" / "CSV" / "TSV" / "TXT" (デフォルト: Excel)
    /// Settings["SheetName"]    : シート名 (Excel のみ、デフォルト: Sheet1)
    /// Settings["IncludeHeader"]: "true" / "false" (デフォルト: true)
    /// Settings["Delimiter"]    : 区切り文字 (CSV/TSV/TXT のみ、デフォルト: , or \t)
    /// Settings["Encoding"]     : "UTF-8" / "UTF-8 BOM" / "Shift-JIS" (デフォルト: UTF-8)
    /// </summary>
    public class ExcelOutputStep : StepBase
    {
        public override StepType StepType => StepType.ExcelOutput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var filePath = Settings.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "";
            var format = Settings.TryGetValue("Format", out var fmt) ? fmt?.ToString() ?? "Excel" : "Excel";
            var sheetName = Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "Sheet1" : "Sheet1";
            var includeHeader = ParseBool(Settings.TryGetValue("IncludeHeader", out var ih) ? ih?.ToString() : null, true);
            var encodingName = Settings.TryGetValue("Encoding", out var enc) ? enc?.ToString() ?? "UTF-8" : "UTF-8";
            var delimiterSetting = Settings.TryGetValue("Delimiter", out var dl) ? dl?.ToString() : null;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("出力ファイルパスが設定されていません。");

            // ディレクトリが存在しなければ作成
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (format == "Excel")
            {
                await WriteExcelAsync(filePath, sheetName, includeHeader, inputData, progress, ct);
            }
            else
            {
                var delimiter = GetDelimiter(format, delimiterSetting);
                var encoding = GetEncoding(encodingName);
                await WriteTextAsync(filePath, delimiter, includeHeader, encoding, inputData, progress, ct);
            }

            return inputData;
        }

        private static async Task WriteExcelAsync(
            string filePath, string sheetName, bool includeHeader,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress, CancellationToken ct)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            await Task.Run(() =>
            {
                using var package = new ExcelPackage();
                var ws = package.Workbook.Worksheets.Add(sheetName);

                if (inputData.Count == 0)
                {
                    package.SaveAs(new FileInfo(filePath));
                    progress.Report($"Excel Output: データなし、空ファイルを作成 → {filePath}");
                    return;
                }

                var columns = inputData[0].Keys.ToList();
                int startRow = 1;

                if (includeHeader)
                {
                    for (int c = 0; c < columns.Count; c++)
                    {
                        ws.Cells[1, c + 1].Value = columns[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }
                    startRow = 2;
                }

                for (int r = 0; r < inputData.Count; r++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int c = 0; c < columns.Count; c++)
                        ws.Cells[startRow + r, c + 1].Value = inputData[r][columns[c]];
                }

                package.SaveAs(new FileInfo(filePath));
                progress.Report($"Excel Output: {inputData.Count}行 書き込み完了 → {filePath}");
            }, ct);
        }

        private static async Task WriteTextAsync(
            string filePath, string delimiter, bool includeHeader,
            Encoding encoding,
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress, CancellationToken ct)
        {
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(filePath, append: false, encoding: encoding);

                if (inputData.Count == 0)
                {
                    progress.Report($"File Output: データなし、空ファイルを作成 → {filePath}");
                    return;
                }

                var columns = inputData[0].Keys.ToList();

                if (includeHeader)
                    writer.WriteLine(string.Join(delimiter, columns.Select(c => EscapeField(c, delimiter))));

                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    writer.WriteLine(string.Join(delimiter,
                        columns.Select(c => EscapeField(row[c]?.ToString() ?? "", delimiter))));
                }

                progress.Report($"File Output: {inputData.Count}行 書き込み完了 → {filePath}");
            }, ct);
        }

        /// <summary>フィールドに区切り文字や改行が含まれる場合はダブルクォートで囲む (CSV 標準)</summary>
        private static string EscapeField(string value, string delimiter)
        {
            if (delimiter == "\t") return value; // TSV はクォート不要
            if (value.Contains(delimiter) || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string GetDelimiter(string format, string? setting)
        {
            if (!string.IsNullOrEmpty(setting)) return setting!;
            return format == "TSV" ? "\t" : ",";
        }

        private static Encoding GetEncoding(string name) => name switch
        {
            "UTF-8 BOM" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "Shift-JIS" => Encoding.GetEncoding("shift_jis"),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        /// <summary>"true"/"false" 文字列と bool 両方に対応したパース</summary>
        private static bool ParseBool(string? value, bool defaultValue)
        {
            if (value == null) return defaultValue;
            if (bool.TryParse(value, out var result)) return result;
            return defaultValue;
        }

        public override string GetDisplayIcon() => "📄";
    }
}
