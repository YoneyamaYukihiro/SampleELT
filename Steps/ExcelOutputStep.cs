using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OfficeOpenXml;
using SampleELT.Models;

namespace SampleELT.Steps
{
    public class ExcelOutputStep : StepBase
    {
        public override StepType StepType => StepType.ExcelOutput;

        public override async Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var filePath = Settings.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "";
            var sheetName = Settings.TryGetValue("SheetName", out var sn) ? sn?.ToString() ?? "Sheet1" : "Sheet1";
            var includeHeader = Settings.TryGetValue("IncludeHeader", out var ih) && ih is bool b ? b : true;

            await Task.Run(() =>
            {
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add(sheetName);

                if (inputData.Count == 0)
                {
                    package.SaveAs(new FileInfo(filePath));
                    progress.Report("Excel Output: データなし、空ファイルを作成");
                    return;
                }

                var columns = inputData[0].Keys.ToList();
                int startRow = 1;

                if (includeHeader)
                {
                    for (int col = 0; col < columns.Count; col++)
                    {
                        worksheet.Cells[1, col + 1].Value = columns[col];
                        worksheet.Cells[1, col + 1].Style.Font.Bold = true;
                    }
                    startRow = 2;
                }

                for (int row = 0; row < inputData.Count; row++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (int col = 0; col < columns.Count; col++)
                    {
                        worksheet.Cells[startRow + row, col + 1].Value = inputData[row][columns[col]];
                    }
                }

                var fileInfo = new FileInfo(filePath);
                package.SaveAs(fileInfo);
                progress.Report($"Excel Output: {inputData.Count}行 書き込み完了 → {filePath}");
            }, ct);

            return inputData;
        }

        public override string GetDisplayIcon() => "📗";
    }
}
