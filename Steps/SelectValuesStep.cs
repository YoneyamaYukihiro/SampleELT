using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BreezeFlow.Models;

namespace BreezeFlow.Steps
{
    /// <summary>
    /// フィールドの選択・リネーム・型変換を行うステップ。
    /// Settings["FieldMappings"] には 1 行 1 マッピング:
    ///   - 新フォーマット: <c>元名|新名|型|残す</c>
    ///   - 旧フォーマット: <c>元名=新名</c> または <c>元名</c>
    /// IsIncluded=false の行は出力から除外する。
    /// </summary>
    public class SelectValuesStep : StepBase
    {
        public override StepType StepType => StepType.SelectValues;

        public override async IAsyncEnumerable<Dictionary<string, object?>> ExecuteStreamingAsync(
            IAsyncEnumerable<Dictionary<string, object?>> input,
            IProgress<string> progress,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var raw   = Settings.TryGetValue("FieldMappings", out var m) ? m?.ToString() ?? "" : "";
            var items = FieldMappingItem.Parse(raw)
                .Where(i => i.IsIncluded
                            && (!string.IsNullOrEmpty(i.SourceName) || i.IsConstant))
                .ToList();

            if (items.Count == 0)
            {
                // マッピング未定義 → そのまま通過
                int passCount = 0;
                await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
                {
                    passCount++;
                    yield return row;
                }
                progress.Report($"Select Values: マッピング未定義 - {passCount}行 そのまま通過");
                yield break;
            }

            int total = 0;
            bool anyInput = false;
            await foreach (var row in input.WithCancellation(ct).ConfigureAwait(false))
            {
                anyInput = true;
                var newRow = new Dictionary<string, object?>();
                foreach (var item in items)
                {
                    if (item.IsConstant)
                        newRow[item.EffectiveDestName] = ConvertValue(item.ConstantValue, item.DataType);
                    else if (row.TryGetValue(item.SourceName, out var val))
                        newRow[item.EffectiveDestName] = ConvertValue(val, item.DataType);
                }
                total++;
                yield return newRow;
            }

            // 入力が 0 行でも定数のみで 1 行出力する (旧仕様互換)
            if (!anyInput && items.All(i => i.IsConstant))
            {
                var constRow = new Dictionary<string, object?>();
                foreach (var item in items)
                    constRow[item.EffectiveDestName] = ConvertValue(item.ConstantValue, item.DataType);
                progress.Report("Select Values: 定数のみで 1 行出力");
                yield return constRow;
                yield break;
            }

            progress.Report($"Select Values: {total}行 変換完了");
        }

        /// <summary>
        /// 型変換。失敗時 (パース不能) は null を返す。型指定が空のときは元の値をそのまま返す。
        /// </summary>
        private static object? ConvertValue(object? val, string type)
        {
            if (string.IsNullOrEmpty(type)) return val;
            if (val == null) return null;

            var s = val.ToString() ?? "";
            return type.ToLowerInvariant() switch
            {
                "string"   => s,
                "int"      => int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? (object?)i : null,
                "long"     => long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? (object?)l : null,
                "decimal"  => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? (object?)d : null,
                "double"   => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dd) ? (object?)dd : null,
                "datetime" => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                              || DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt)
                              ? (object?)dt : null,
                "bool"     => bool.TryParse(s, out var b) ? (object?)b : null,
                _          => val
            };
        }

        public override string GetDisplayIcon() => "📋";
    }
}
