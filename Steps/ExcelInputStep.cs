using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OfficeOpenXml;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class ExcelInputStep : StepBase
    {
        public override StepType StepType => StepType.ExcelInput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var filePath = Settings.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "";
            var sheetName = Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "" : "";
            var hasHeader = Settings.TryGetValue("HasHeader", out var hh) && hh is bool b ? b : true;

            var result = new List<Dictionary<string, object?>>();

            await Task.Run(() =>
            {
                using var package = new ExcelPackage(new FileInfo(filePath));
                ExcelWorksheet? worksheet = null;

                if (!string.IsNullOrEmpty(sheetName))
                {
                    worksheet = package.Workbook.Worksheets[sheetName];
                }

                if (worksheet == null && package.Workbook.Worksheets.Count > 0)
                {
                    worksheet = package.Workbook.Worksheets[0];
                }

                if (worksheet == null)
                {
                    progress.Report("Excel: シートが見つかりません");
                    return;
                }

                int rowCount = worksheet.Dimension?.Rows ?? 0;
                int colCount = worksheet.Dimension?.Columns ?? 0;

                if (rowCount == 0) return;

                var headers = new List<string>();
                int dataStartRow = 1;

                if (hasHeader)
                {
                    for (int col = 1; col <= colCount; col++)
                    {
                        headers.Add(worksheet.Cells[1, col].Text ?? $"Column{col}");
                    }
                    dataStartRow = 2;
                }
                else
                {
                    for (int col = 1; col <= colCount; col++)
                    {
                        headers.Add($"Column{col}");
                    }
                }

                for (int row = dataStartRow; row <= rowCount; row++)
                {
                    ct.ThrowIfCancellationRequested();
                    var rowData = new Dictionary<string, object?>();
                    for (int col = 1; col <= colCount; col++)
                    {
                        var cell = worksheet.Cells[row, col];
                        rowData[headers[col - 1]] = cell.Value;
                    }
                    result.Add(rowData);
                }

                progress.Report($"Excel: {result.Count}行 読み込み完了");
            }, ct);

            return result;
        }

        public override string GetDisplayIcon() => "📗";
    }
}
