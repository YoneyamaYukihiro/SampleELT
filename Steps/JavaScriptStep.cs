using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using SampleELT.Models;

namespace SampleELT.Steps
{
    /// <summary>
    /// Jint を使用して JavaScript コードを実行するステップ。
    ///
    /// Settings["Script"]     : 実行する JS コード
    /// Settings["RunPerRow"]  : "true" の場合、入力の各行に対して実行 (デフォルト: true)
    ///
    /// JS 環境:
    ///   - 入力フィールドの値は変数として参照可能 (例: PROC_END, WP_ID)
    ///   - 出力フィールドは $out オブジェクトに設定: $out.NEW_FIELD = value;
    ///   - 入力フィールドを $out に設定しない場合も、入力値はそのまま引き継がれる
    /// </summary>
    public class JavaScriptStep : StepBase
    {
        public override StepType StepType => StepType.JavaScript;

        public override Task<List<Dictionary<string, object?>>> ExecuteAsync(
            List<Dictionary<string, object?>> inputData,
            IProgress<string> progress,
            CancellationToken ct)
        {
            var script = Settings.TryGetValue("Script", out var s) ? s?.ToString() ?? "" : "";
            var runPerRow = !Settings.TryGetValue("RunPerRow", out var rpr)
                || rpr?.ToString()?.Equals("false", StringComparison.OrdinalIgnoreCase) != true;

            if (string.IsNullOrWhiteSpace(script))
            {
                progress.Report("JavaScript: スクリプトが設定されていません。");
                return Task.FromResult(inputData);
            }

            var result = new List<Dictionary<string, object?>>();

            if (runPerRow && inputData.Count > 0)
            {
                foreach (var row in inputData)
                {
                    ct.ThrowIfCancellationRequested();
                    var outRow = ExecuteScript(script, row);
                    result.Add(outRow);
                }
                progress.Report($"JavaScript: {result.Count}行 実行完了");
            }
            else
            {
                // Run once with empty or first-row context
                var ctx = inputData.Count > 0 ? inputData[0] : new Dictionary<string, object?>();
                result.Add(ExecuteScript(script, ctx));
                progress.Report("JavaScript: 1回 実行完了");
            }

            return Task.FromResult(result);
        }

        private static Dictionary<string, object?> ExecuteScript(
            string script, Dictionary<string, object?> inputRow)
        {
            var engine = new Engine(cfg => cfg.AllowClr(false));

            // Expose input fields as JS variables
            foreach (var kv in inputRow)
            {
                var safeName = MakeSafeName(kv.Key);
                engine.SetValue(safeName, ToJsValue(kv.Value));
            }

            // $out object for output fields
            engine.Execute("var $out = {};");

            // Execute user script
            engine.Execute(script);

            // Collect output
            var outRow = new Dictionary<string, object?>(inputRow);

            // Read $out properties and merge into row
            var outObj = engine.GetValue("$out");
            if (outObj.IsObject())
            {
                foreach (var prop in outObj.AsObject().GetOwnProperties())
                {
                    var val = prop.Value.Value;
                    outRow[prop.Key.AsString()] = val.IsNull() || val.IsUndefined()
                        ? null
                        : val.IsNumber() ? val.AsNumber()
                        : val.IsBoolean() ? val.AsBoolean()
                        : val.ToString();
                }
            }

            return outRow;
        }

        private static object? ToJsValue(object? value)
        {
            if (value == null) return null;
            if (value is DateTime dt) return dt.ToString("yyyy/MM/dd HH:mm:ss");
            return value;
        }

        private static string MakeSafeName(string name)
            => System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");

        public override string GetDisplayIcon() => "📜";
    }
}
