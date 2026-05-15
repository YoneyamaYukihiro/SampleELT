using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BreezeFlow.Tools.Ktr
{
    /// <summary>
    /// 「RowGenerator → ScriptValueMod (→ SelectValues)」が
    /// 「start_date / end_date を昨日・今日にセット」する定型パターンと一致するかを判定し、
    /// 一致した場合は単一の SetVariable ステップに圧縮する。
    /// </summary>
    internal static class KtrSetVariablePatternDetector
    {
        internal class Result
        {
            public Dictionary<string, string> Replacement { get; } = new(StringComparer.Ordinal);
            public HashSet<string> Removed { get; } = new(StringComparer.Ordinal);
            public List<JsonStep> Synthetic { get; } = new();
            public List<string> SetVarFieldOrder { get; } = new();
        }

        public static Result Detect(
            List<KtrStep> steps,
            List<(string From, string To)> hops,
            List<string> warnings)
        {
            var result = new Result();
            var byName = steps.ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);

            foreach (var hop in hops)
            {
                if (!byName.TryGetValue(hop.From, out var src)) continue;
                if (!byName.TryGetValue(hop.To, out var dst)) continue;
                if (!string.Equals(src.Type, "RowGenerator", StringComparison.Ordinal)) continue;
                if (!string.Equals(dst.Type, "ScriptValueMod", StringComparison.Ordinal)) continue;
                if (result.Replacement.ContainsKey(src.Name)) continue;

                var setVarFields = TryParseDateScript(dst.Element);
                if (setVarFields == null)
                {
                    warnings.Add(
                        $"ScriptValueMod '{dst.Name}' は日付セットの定型パターンに合致しないため SetVariable へ統合できません。" +
                        " 元のステップは Dummy として残します。");
                    continue;
                }

                var dstChildren = hops.Where(h => string.Equals(h.From, dst.Name, StringComparison.Ordinal))
                                      .Select(h => h.To).ToList();

                var syn = new JsonStep
                {
                    StepType = "SetVariable",
                    Name = "Set Variable",
                    CanvasX = src.X,
                    CanvasY = src.Y,
                    NodeWidth = 150,
                    NodeHeight = 70,
                    Settings =
                    {
                        ["Fields"] = string.Join("\r\n", setVarFields.Select(kv => $"{kv.Key} = {kv.Value}")),
                        ["DateFormat"] = "yyyy/MM/dd"
                    }
                };
                result.Synthetic.Add(syn);

                if (result.SetVarFieldOrder.Count == 0)
                    result.SetVarFieldOrder.AddRange(setVarFields.Keys);

                result.Removed.Add(src.Name);
                result.Removed.Add(dst.Name);
                result.Replacement[src.Name] = syn.Name;
                result.Replacement[dst.Name] = syn.Name;

                // 直後の SelectValues がフィールドを絞るだけなら除外
                foreach (var childName in dstChildren)
                {
                    if (!byName.TryGetValue(childName, out var child)) continue;
                    if (!string.Equals(child.Type, "SelectValues", StringComparison.Ordinal)) continue;

                    var fieldsInSelect = child.Element.Element("fields")?.Elements("field")
                        .Select(f => (f.Element("name")?.Value ?? "").Trim())
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList() ?? new List<string>();

                    bool sameSet = fieldsInSelect.Count == setVarFields.Count
                        && fieldsInSelect.All(setVarFields.ContainsKey);
                    if (sameSet)
                    {
                        result.Removed.Add(child.Name);
                        result.Replacement[child.Name] = syn.Name;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// ScriptValueMod の JS スクリプトを解析し、各 setValue 呼び出しを SetVariable の式に変換する。
        /// 対応パターン: 「昨日 → start_date, 今日 → end_date」のような定型のみ。
        /// </summary>
        private static Dictionary<string, string>? TryParseDateScript(XElement scriptStepEl)
        {
            var script = scriptStepEl
                .Element("jsScripts")?
                .Element("jsScript")?
                .Element("jsScript_script")?.Value ?? "";

            if (string.IsNullOrWhiteSpace(script)) return null;

            var setValueMatches = Regex.Matches(script, @"(\w+)\s*\.\s*setValue\s*\(\s*(\w+)\s*\)");
            if (setValueMatches.Count == 0) return null;

            bool VarUsesYesterday(string varName) => RhsMatches(script, varName,
                @"\b(start(Year|Month|Day|Yaer)|yesterday)\b");
            bool VarUsesToday(string varName) => RhsMatches(script, varName,
                @"\b(end(Year|Month|Day|Yaer)|today)\b");

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in setValueMatches)
            {
                var fieldName = m.Groups[1].Value;
                var sourceVar = m.Groups[2].Value;
                if (VarUsesYesterday(sourceVar)) fields[fieldName] = "YESTERDAY";
                else if (VarUsesToday(sourceVar)) fields[fieldName] = "TODAY";
                else return null;
            }

            return fields.Count > 0 ? fields : null;
        }

        private static bool RhsMatches(string script, string varName, string pattern)
        {
            var rhsMatch = Regex.Match(script,
                @"var\s+" + Regex.Escape(varName) + @"\s*=\s*([^;]+);",
                RegexOptions.Multiline);
            return rhsMatch.Success && Regex.IsMatch(rhsMatch.Groups[1].Value, pattern, RegexOptions.IgnoreCase);
        }
    }
}
