using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OfficeOpenXml;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// ファイル入力ステップ (Excel / CSV / TSV / TXT)。
    /// Settings["FilePath"]  : 読み込むファイルパス
    /// Settings["Format"]    : "Excel" / "CSV" / "TSV" / "TXT" (デフォルト: Excel)
    /// Settings["SheetName"] : シート名 (Excel のみ)
    /// Settings["HasHeader"] : "true" / "false" (デフォルト: true)
    /// Settings["Delimiter"] : 区切り文字 (CSV/TSV/TXT のみ)
    /// Settings["Encoding"]  : "UTF-8" / "UTF-8 BOM" / "Shift-JIS" (デフォルト: UTF-8)
    /// </summary>
    public class ExcelInputStep : StepBase
    {
        public override StepType StepType => StepType.ExcelInput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var filePath  = Settings.TryGetValue("FilePath",  out var fp)  ? fp?.ToString()  ?? "" : "";
            var format    = Settings.TryGetValue("Format",    out var fmt) ? fmt?.ToString()  ?? "Excel" : "Excel";
            var hasHeader = ParseBool(Settings.TryGetValue("HasHeader", out var hh) ? hh?.ToString() : null, true);

            if (string.IsNullOrWhiteSpace(filePath))
                throw new InvalidOperationException("入力ファイルパスが設定されていません。");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"ファイルが見つかりません: {filePath}");

            if (format == "Excel")
            {
                var sheetName = Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "" : "";
                return await ReadExcelAsync(filePath, sheetName, hasHeader, progress, ct);
            }
            else
            {
                var delimiterSetting = Settings.TryGetValue("Delimiter", out var dl) ? dl?.ToString() : null;
                var encodingName     = Settings.TryGetValue("Encoding",  out var enc) ? enc?.ToString() ?? "UTF-8" : "UTF-8";
                var delimiter = GetDelimiter(format, delimiterSetting);
                var encoding  = GetEncoding(encodingName);
                return await ReadTextAsync(filePath, delimiter, hasHeader, encoding, progress, ct);
            }
        }

        private static async Task<List<Dictionary<string, object?>>> ReadExcelAsync(
            string filePath, string sheetName, bool hasHeader,
            IProgress<string> progress, CancellationToken ct)
        {
            ExcelPackage.License.SetNonCommercialPersonal("SampleELT");
            var result = new List<Dictionary<string, object?>>();

            await Task.Run(() =>
            {
                using var package = new ExcelPackage(new FileInfo(filePath));
                ExcelWorksheet? worksheet = null;

                if (!string.IsNullOrEmpty(sheetName))
                    worksheet = package.Workbook.Worksheets[sheetName];

                if (worksheet == null && package.Workbook.Worksheets.Count > 0)
                    worksheet = package.Workbook.Worksheets[0];

                if (worksheet == null) { progress.Report("Excel: シートが見つかりません"); return; }

                int rowCount = worksheet.Dimension?.Rows ?? 0;
                int colCount = worksheet.Dimension?.Columns ?? 0;
                if (rowCount == 0) return;

                var headers = new List<string>();
                int dataStartRow = 1;

                if (hasHeader)
                {
                    for (int c = 1; c <= colCount; c++)
                        headers.Add(worksheet.Cells[1, c].Text ?? $"Column{c}");
                    dataStartRow = 2;
                }
                else
                {
                    for (int c = 1; c <= colCount; c++)
                        headers.Add($"Column{c}");
                }

                for (int r = dataStartRow; r <= rowCount; r++)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = new Dictionary<string, object?>();
                    for (int c = 1; c <= colCount; c++)
                        row[headers[c - 1]] = worksheet.Cells[r, c].Value;
                    result.Add(row);
                }

                progress.Report($"Excel Input: {result.Count}行 読み込み完了");
            }, ct);

            return result;
        }

        private static async Task<List<Dictionary<string, object?>>> ReadTextAsync(
            string filePath, string delimiter, bool hasHeader,
            Encoding encoding, IProgress<string> progress, CancellationToken ct)
        {
            var result = new List<Dictionary<string, object?>>();

            await Task.Run(() =>
            {
                using var reader = new StreamReader(filePath, encoding);
                var headers = new List<string>();
                bool firstLine = true;

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    var fields = SplitLine(line, delimiter);

                    if (firstLine)
                    {
                        firstLine = false;
                        if (hasHeader)
                        {
                            for (int i = 0; i < fields.Count; i++)
                                headers.Add(string.IsNullOrEmpty(fields[i]) ? $"Column{i + 1}" : fields[i]);
                            continue;
                        }
                        else
                        {
                            for (int i = 0; i < fields.Count; i++)
                                headers.Add($"Column{i + 1}");
                        }
                    }

                    var row = new Dictionary<string, object?>();
                    for (int i = 0; i < headers.Count; i++)
                        row[headers[i]] = i < fields.Count ? (object?)fields[i] : null;
                    result.Add(row);
                }

                progress.Report($"File Input: {result.Count}行 読み込み完了");
            }, ct);

            return result;
        }

        /// <summary>CSV/TSV の1行をフィールドに分割。ダブルクォート囲みに対応。</summary>
        private static List<string> SplitLine(string line, string delimiter)
        {
            var fields = new List<string>();

            // タブ区切りなど単純な区切り文字の場合はクォート処理不要
            if (delimiter != ",")
            {
                foreach (var f in line.Split(delimiter))
                    fields.Add(f);
                return fields;
            }

            // CSV: ダブルクォート対応
            var sb = new System.Text.StringBuilder();
            bool inQuote = false;
            int i = 0;
            while (i < line.Length)
            {
                char c = line[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                        else { inQuote = false; i++; }
                    }
                    else { sb.Append(c); i++; }
                }
                else
                {
                    if (c == '"') { inQuote = true; i++; }
                    else if (line[i..].StartsWith(delimiter))
                    {
                        fields.Add(sb.ToString()); sb.Clear();
                        i += delimiter.Length;
                    }
                    else { sb.Append(c); i++; }
                }
            }
            fields.Add(sb.ToString());
            return fields;
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

        private static bool ParseBool(string? value, bool defaultValue)
        {
            if (value == null) return defaultValue;
            if (bool.TryParse(value, out var result)) return result;
            return defaultValue;
        }

        public override string GetDisplayIcon() => "📂";
    }
}
