using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SampleELT.Tools.Ktr
{
    /// <summary>
    /// 1 つの KTR ステップ種別を SampleELT の <see cref="JsonStep"/> に変換するストラテジ。
    /// <see cref="HandledKtrType"/> を <see cref="KtrStepConverterRegistry"/> がディスパッチに使う。
    /// </summary>
    internal interface IKtrStepConverter
    {
        string HandledKtrType { get; }
        void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx);
        string SampleEltStepType { get; }
    }

    /// <summary>未対応 / フォールバック用のコンバータ。Dummy 化して原 XML を保持する。</summary>
    internal class FallbackDummyConverter : IKtrStepConverter
    {
        public string HandledKtrType => "*";
        public string SampleEltStepType => "Dummy";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            dst.Settings["OriginalKtrType"] = src.Type;
            dst.Settings["OriginalXml"] = src.Element.ToString(SaveOptions.DisableFormatting);
            ctx.Warnings.Add($"未対応のステップ種別 '{src.Type}' (name='{src.Name}') を Dummy に置換しました。");
        }
    }

    /// <summary>SetVariable 統合に失敗した RowGenerator / ScriptValueMod の保留用。</summary>
    internal class UnconvertedScriptConverter : IKtrStepConverter
    {
        private readonly string _ktrType;
        public string HandledKtrType => _ktrType;
        public string SampleEltStepType => "Dummy";
        public UnconvertedScriptConverter(string ktrType) { _ktrType = ktrType; }

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            dst.Settings["OriginalKtrType"] = src.Type;
            dst.Settings["OriginalXml"] = src.Element.ToString(SaveOptions.DisableFormatting);
            ctx.Warnings.Add($"ステップ '{src.Name}' (type={src.Type}) は SampleELT に対応する種別が無いため Dummy に置換しました。");
        }
    }

    internal class TableInputConverter : IKtrStepConverter
    {
        public string HandledKtrType => "TableInput";
        public string SampleEltStepType => "DBInput";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var connName = src.Element.Element("connection")?.Value?.Trim() ?? "";
            if (ctx.ConnectionMap.TryGetValue(connName, out var connId))
                dst.Settings["ConnectionId"] = connId.ToString();

            var sql = src.Element.Element("sql")?.Value ?? "";
            sql = KtrSqlPlaceholderNormalizer.Normalize(sql, ctx.Warnings, src.Name, ctx.SetVariableFieldOrder);
            dst.Settings["SQL"] = sql;

            var eachRow = (string?)src.Element.Element("execute_each_row") ?? "N";
            dst.Settings["ExecuteEachRow"] = string.Equals(eachRow, "Y", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

            dst.NodeWidth = 220;
            dst.NodeHeight = 80;
        }
    }

    internal class TableOutputConverter : IKtrStepConverter
    {
        public string HandledKtrType => "TableOutput";
        public string SampleEltStepType => "DBOutput";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var connName = src.Element.Element("connection")?.Value?.Trim() ?? "";
            if (ctx.ConnectionMap.TryGetValue(connName, out var connId))
                dst.Settings["ConnectionId"] = connId.ToString();

            dst.Settings["TableName"] = src.Element.Element("table")?.Value?.Trim() ?? "";
            dst.Settings["Mode"] = "INSERT";
            var commit = src.Element.Element("commit")?.Value?.Trim();
            dst.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }
    }

    internal class InsertUpdateKtrConverter : IKtrStepConverter
    {
        public string HandledKtrType => "InsertUpdate";
        public string SampleEltStepType => "InsertUpdate";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var connName = src.Element.Element("connection")?.Value?.Trim() ?? "";
            if (ctx.ConnectionMap.TryGetValue(connName, out var connId))
                dst.Settings["ConnectionId"] = connId.ToString();

            var lookup = src.Element.Element("lookup");
            dst.Settings["TableName"] = lookup?.Element("table")?.Value?.Trim() ?? "";

            var keys = lookup?.Elements("key")
                .Select(k => (k.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            dst.Settings["KeyFields"] = string.Join(",", keys);

            var values = lookup?.Elements("value")
                .Select(v => (v.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            dst.Settings["UpdateFields"] = string.Join(",", keys.Concat(values));

            var commit = src.Element.Element("commit")?.Value?.Trim();
            dst.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }
    }

    internal class UpdateConverter : IKtrStepConverter
    {
        public string HandledKtrType => "Update";
        public string SampleEltStepType => "DBUpdate";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var connName = src.Element.Element("connection")?.Value?.Trim() ?? "";
            if (ctx.ConnectionMap.TryGetValue(connName, out var connId))
                dst.Settings["ConnectionId"] = connId.ToString();

            var lookup = src.Element.Element("lookup");
            dst.Settings["TableName"] = lookup?.Element("table")?.Value?.Trim() ?? "";

            var keys = lookup?.Elements("key")
                .Select(k => (k.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            dst.Settings["KeyFields"] = string.Join(",", keys);

            var values = lookup?.Elements("value")
                .Select(v => (v.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            dst.Settings["UpdateFields"] = string.Join(",", values);

            var commit = src.Element.Element("commit")?.Value?.Trim();
            dst.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }
    }

    internal class DeleteConverter : IKtrStepConverter
    {
        public string HandledKtrType => "Delete";
        public string SampleEltStepType => "DBDelete";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var connName = src.Element.Element("connection")?.Value?.Trim() ?? "";
            if (ctx.ConnectionMap.TryGetValue(connName, out var connId))
                dst.Settings["ConnectionId"] = connId.ToString();

            var lookup = src.Element.Element("lookup");
            dst.Settings["TableName"] = lookup?.Element("table")?.Value?.Trim() ?? "";
            var keys = lookup?.Elements("key")
                .Select(k => (k.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            dst.Settings["KeyFields"] = string.Join(",", keys);

            var commit = src.Element.Element("commit")?.Value?.Trim();
            dst.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }
    }

    internal class ExecSqlConverter : IKtrStepConverter
    {
        private readonly string _ktrType;
        public string HandledKtrType => _ktrType;
        public string SampleEltStepType => "ExecSQL";
        public ExecSqlConverter(string ktrType) { _ktrType = ktrType; }

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var connName = src.Element.Element("connection")?.Value?.Trim() ?? "";
            if (ctx.ConnectionMap.TryGetValue(connName, out var connId))
                dst.Settings["ConnectionId"] = connId.ToString();
            dst.Settings["SQL"] = src.Element.Element("sql")?.Value ?? "";
            var eachRow = (string?)src.Element.Element("execute_each_row") ?? "N";
            dst.Settings["ExecuteEachRow"] = string.Equals(eachRow, "Y", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        }
    }

    internal class MergeJoinConverter : IKtrStepConverter
    {
        public string HandledKtrType => "MergeJoin";
        public string SampleEltStepType => "MergeJoin";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var joinType = (src.Element.Element("join_type")?.Value ?? "INNER").Trim().ToUpperInvariant();
            dst.Settings["JoinType"] = joinType;

            var keys1 = src.Element.Element("keys_1")?.Elements("key")
                .Select(k => k.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                ?? new List<string>();
            var keys2 = src.Element.Element("keys_2")?.Elements("key")
                .Select(k => k.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                ?? new List<string>();

            dst.Settings["KeyFields"] = string.Join(",", keys1);

            if (keys2.Count > 0 && !keys1.SequenceEqual(keys2, StringComparer.OrdinalIgnoreCase))
            {
                ctx.Warnings.Add(
                    $"MergeJoin '{src.Name}': 左右で異なるキー (left={string.Join(",", keys1)} / right={string.Join(",", keys2)}) " +
                    "が指定されています。SampleELT は単一の KeyFields のみサポートのため左側 (keys_1) を採用しました。");
            }
        }
    }

    internal class SelectValuesConverter : IKtrStepConverter
    {
        public string HandledKtrType => "SelectValues";
        public string SampleEltStepType => "SelectValues";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var lines = new List<string>();
            foreach (var f in src.Element.Element("fields")?.Elements("field") ?? Enumerable.Empty<XElement>())
            {
                var name = (f.Element("name")?.Value ?? "").Trim();
                var rename = (f.Element("rename")?.Value ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                lines.Add(string.IsNullOrEmpty(rename) || rename == name ? name : $"{name}={rename}");
            }
            dst.Settings["FieldMappings"] = string.Join("\r\n", lines);
        }
    }

    internal class CalculatorConverter : IKtrStepConverter
    {
        public string HandledKtrType => "Calculator";
        public string SampleEltStepType => "Calculation";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            // KTR Calculator は複数式を持つが、SampleELT は1式しか持てないので最初の有効計算のみ取り込む。
            var calc = src.Element.Element("calculation")?.Elements("calculation").FirstOrDefault()
                       ?? src.Element.Elements("calculation").FirstOrDefault();
            if (calc == null)
            {
                dst.Settings["OutputFieldName"] = "Result";
                dst.Settings["ExpressionType"] = "add";
                dst.Settings["Field1"] = "";
                dst.Settings["Field2"] = "";
                dst.Settings["Constant"] = "0";
                ctx.Warnings.Add($"Calculator '{src.Name}': 計算定義が読み取れませんでした。手動で設定してください。");
                return;
            }

            var fieldName = (calc.Element("field_name")?.Value ?? "Result").Trim();
            var calcType = (calc.Element("calc_type")?.Value ?? "").Trim();
            var fa = (calc.Element("field_a")?.Value ?? "").Trim();
            var fb = (calc.Element("field_b")?.Value ?? "").Trim();

            string mapped = calcType switch
            {
                "ADD"       => "add",
                "SUBTRACT"  => "subtract",
                "MULTIPLY"  => "multiply",
                "DIVIDE"    => "divide",
                "DATE_DIFF" => "dateDiffMinutes",
                _           => "add"
            };
            dst.Settings["OutputFieldName"] = fieldName;
            dst.Settings["ExpressionType"] = mapped;
            dst.Settings["Field1"] = fa;
            dst.Settings["Field2"] = fb;
            dst.Settings["Constant"] = "0";

            if (mapped == "add" && !string.Equals(calcType, "ADD", StringComparison.OrdinalIgnoreCase))
                ctx.Warnings.Add($"Calculator '{src.Name}': calc_type='{calcType}' は近似マップ ('add') で取り込みました。手動確認してください。");
        }
    }

    internal class FormulaConverter : IKtrStepConverter
    {
        public string HandledKtrType => "Formula";
        public string SampleEltStepType => "Calculation";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            var formula = src.Element.Element("formula") ?? src.Element.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "formula");
            var fieldName = (formula?.Element("field_name")?.Value ?? "Result").Trim();
            var formulaText = (formula?.Element("formula_string")?.Value ?? "").Trim();

            dst.Settings["OutputFieldName"] = fieldName;
            dst.Settings["Constant"] = "0";

            // 「([F1]-[F2])*24*60」→ dateDiffMinutes
            var diffMin = Regex.Match(formulaText, @"\(\s*\[(\w+)\]\s*-\s*\[(\w+)\]\s*\)\s*\*\s*24\s*\*\s*60");
            if (diffMin.Success)
            {
                dst.Settings["ExpressionType"] = "dateDiffMinutes";
                dst.Settings["Field1"] = diffMin.Groups[1].Value;
                dst.Settings["Field2"] = diffMin.Groups[2].Value;
                return;
            }

            // [A] op [B] の単純2項
            var simple = Regex.Match(formulaText, @"\[(\w+)\]\s*([+\-*/])\s*\[(\w+)\]");
            if (simple.Success)
            {
                var op = simple.Groups[2].Value;
                dst.Settings["ExpressionType"] = op switch
                {
                    "+" => "add",
                    "-" => "subtract",
                    "*" => "multiply",
                    "/" => "divide",
                    _   => "add"
                };
                dst.Settings["Field1"] = simple.Groups[1].Value;
                dst.Settings["Field2"] = simple.Groups[3].Value;
                return;
            }

            // 解析不能 → add で空フィールド + 警告
            dst.Settings["ExpressionType"] = "add";
            dst.Settings["Field1"] = "";
            dst.Settings["Field2"] = "";
            ctx.Warnings.Add(
                $"Formula '{src.Name}': 式 \"{formulaText}\" を Calculation の標準パターンへ自動変換できませんでした。手動で設定してください。");
        }
    }

    internal class FilterRowsConverter : IKtrStepConverter
    {
        public string HandledKtrType => "FilterRows";
        public string SampleEltStepType => "Filter";

        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx)
        {
            // KTR の <condition> はネスト可能だが、SampleELT は単純比較のみ。簡易対応。
            var cond = src.Element.Element("condition");
            var leftField = cond?.Element("leftvalue")?.Value?.Trim() ?? "";
            var op = (cond?.Element("function")?.Value ?? "=").Trim();
            var rightField = cond?.Element("rightvalue")?.Value?.Trim() ?? "";
            var constant = cond?.Element("value")?.Element("text")?.Value?.Trim() ?? "";

            dst.Settings["FieldName"] = leftField;
            dst.Settings["Operator"] = op switch
            {
                "="    => "equals",
                "<>"   => "notEquals",
                ">"    => "greaterThan",
                ">="   => "greaterOrEqual",
                "<"    => "lessThan",
                "<="   => "lessOrEqual",
                "LIKE" => "contains",
                _      => "equals"
            };
            dst.Settings["Value"] = constant;
            dst.Settings["RightField"] = rightField;

            ctx.Warnings.Add(
                $"FilterRows '{src.Name}': 条件式は単純比較として取り込みました。複合条件 (AND/OR) がある場合は手動で確認してください。");
        }
    }

    internal class DummyConverter : IKtrStepConverter
    {
        public string HandledKtrType => "Dummy";
        public string SampleEltStepType => "Dummy";
        public void Fill(KtrStep src, JsonStep dst, KtrConvertContext ctx) { /* 設定なし */ }
    }

    /// <summary>KTR ステップ種別 → コンバータの登録簿。dispatch を一箇所に集約する。</summary>
    internal static class KtrStepConverterRegistry
    {
        private static readonly Dictionary<string, IKtrStepConverter> _byType
            = new(StringComparer.Ordinal);

        private static readonly FallbackDummyConverter _fallback = new();

        static KtrStepConverterRegistry()
        {
            Register(new TableInputConverter());
            Register(new TableOutputConverter());
            Register(new InsertUpdateKtrConverter());
            Register(new UpdateConverter());
            Register(new DeleteConverter());
            Register(new ExecSqlConverter("ExecSQL"));
            Register(new ExecSqlConverter("ExecSQLRow"));
            Register(new MergeJoinConverter());
            Register(new SelectValuesConverter());
            Register(new CalculatorConverter());
            Register(new FormulaConverter());
            Register(new FilterRowsConverter());
            Register(new DummyConverter());
            Register(new UnconvertedScriptConverter("RowGenerator"));
            Register(new UnconvertedScriptConverter("ScriptValueMod"));
        }

        private static void Register(IKtrStepConverter conv) => _byType[conv.HandledKtrType] = conv;

        public static IKtrStepConverter Resolve(string ktrType)
            => _byType.TryGetValue(ktrType, out var c) ? c : _fallback;
    }
}
