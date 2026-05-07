using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SampleELT.Models;

namespace SampleELT.Tools
{
    public class KtrConvertResult
    {
        public string PipelineName { get; set; } = "";
        public string PipelineJson { get; set; } = "";
        public List<string> Warnings { get; } = new();
        public List<DbConnectionInfo> NewConnections { get; } = new();
        public List<DbConnectionInfo> MatchedConnections { get; } = new();
    }

    /// <summary>
    /// Pentaho Kettle Transformation (.ktr / XML) を SampleELT のパイプライン JSON に変換する。
    /// 対応ステップ: TableInput, TableOutput, InsertUpdate, Update, Delete, ExecSQL,
    ///   MergeJoin, SelectValues, Calculator, Formula, FilterRows, Dummy。
    /// 特殊扱い:
    ///   RowGenerator + ScriptValueMod の連結が「start_date / end_date を昨日・今日にセット」する
    ///   定型パターンであれば SetVariable に圧縮する。
    /// </summary>
    public static class KtrToJsonConverter
    {
        public static KtrConvertResult Convert(string ktrFilePath)
        {
            var xml = XDocument.Load(ktrFilePath);
            var root = xml.Root ?? throw new InvalidOperationException("KTR ルート要素が見つかりません。");
            return ConvertInternal(root);
        }

        public static KtrConvertResult ConvertFromString(string ktrXml)
        {
            var xml = XDocument.Parse(ktrXml);
            var root = xml.Root ?? throw new InvalidOperationException("KTR ルート要素が見つかりません。");
            return ConvertInternal(root);
        }

        // -------- 内部表現 --------

        private class KtrStep
        {
            public string Name = "";
            public string Type = "";
            public XElement Element = new("step");
            public int X;
            public int Y;
        }

        private class JsonStep
        {
            public Guid Id = Guid.NewGuid();
            public string Name = "";
            public string StepType = "";
            public int CanvasX;
            public int CanvasY;
            public int NodeWidth = 150;
            public int NodeHeight = 75;
            public Dictionary<string, object?> Settings = new();
        }

        private class JsonHop
        {
            public Guid Id = Guid.NewGuid();
            public Guid SourceStepId;
            public Guid TargetStepId;
        }

        // -------- 変換本体 --------

        private static KtrConvertResult ConvertInternal(XElement root)
        {
            var result = new KtrConvertResult
            {
                PipelineName = root.Element("info")?.Element("name")?.Value?.Trim() ?? "Pipeline"
            };

            // 接続名 → ConnectionId
            var connNameToId = ResolveConnections(root, result);

            // KTR ステップ収集
            var ktrSteps = root.Elements("step")
                .Select(ParseKtrStep)
                .ToList();

            // hop 収集（順序付き）
            var ktrHops = (root.Element("order")?.Elements("hop") ?? Enumerable.Empty<XElement>())
                .Where(h => string.Equals((string?)h.Element("enabled") ?? "Y", "Y", StringComparison.OrdinalIgnoreCase))
                .Select(h => (From: (string)h.Element("from")! ?? "", To: (string)h.Element("to")! ?? ""))
                .ToList();

            // RowGenerator + ScriptValueMod (+ SelectValues) の合成パターン検知
            var (replacementMap, removedNames, syntheticSteps, setVarFieldOrder) =
                DetectSetVariablePatterns(ktrSteps, ktrHops, result.Warnings);

            // 各 KTR ステップ → JsonStep に変換（除外対象を除く）
            var nameToJson = new Dictionary<string, JsonStep>(StringComparer.Ordinal);

            // 合成ステップを先に追加
            foreach (var syn in syntheticSteps)
                nameToJson[syn.Name] = syn;

            foreach (var step in ktrSteps)
            {
                if (removedNames.Contains(step.Name)) continue;
                if (nameToJson.ContainsKey(step.Name)) continue; // 合成済み

                var converted = ConvertStep(step, connNameToId, result.Warnings, setVarFieldOrder);
                if (converted != null)
                    nameToJson[step.Name] = converted;
            }

            // hop 変換（除外ステップ・合成ステップを考慮）
            var jsonHops = new List<JsonHop>();
            var seenPairs = new HashSet<(Guid, Guid)>();
            foreach (var hop in ktrHops)
            {
                var fromName = ResolveName(hop.From, replacementMap);
                var toName = ResolveName(hop.To, replacementMap);
                if (fromName == null || toName == null) continue;
                if (fromName == toName) continue; // 自己ループ除外
                if (!nameToJson.TryGetValue(fromName, out var fromStep)) continue;
                if (!nameToJson.TryGetValue(toName, out var toStep)) continue;
                if (!seenPairs.Add((fromStep.Id, toStep.Id))) continue;

                jsonHops.Add(new JsonHop
                {
                    SourceStepId = fromStep.Id,
                    TargetStepId = toStep.Id
                });
            }

            // JSON シリアライズ
            result.PipelineJson = SerializePipeline(result.PipelineName, nameToJson.Values, jsonHops);
            return result;
        }

        // -------- 接続解決 --------

        private static Dictionary<string, Guid> ResolveConnections(XElement root, KtrConvertResult result)
        {
            var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var connEl in root.Elements("connection"))
            {
                var name = connEl.Element("name")?.Value?.Trim() ?? "";
                if (string.IsNullOrEmpty(name)) continue;
                if (map.ContainsKey(name)) continue;

                var existing = ConnectionRegistry.Instance.Connections
                    .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    map[name] = existing.Id;
                    result.MatchedConnections.Add(existing);
                }
                else
                {
                    var info = BuildConnectionFromKtr(connEl, name);
                    map[name] = info.Id;
                    result.NewConnections.Add(info);
                    result.Warnings.Add(
                        $"接続 '{name}' は Connection Manager に未登録です。新規 GUID を発行しました。" +
                        " 取り込み後に Connection Manager で接続情報（パスワード等）を完成させてください。");
                }
            }

            return map;
        }

        private static DbConnectionInfo BuildConnectionFromKtr(XElement connEl, string name)
        {
            var typeStr = (connEl.Element("type")?.Value ?? "").Trim().ToUpperInvariant();
            var server = (connEl.Element("server")?.Value ?? "").Trim();
            var port = (connEl.Element("port")?.Value ?? "").Trim();
            var database = (connEl.Element("database")?.Value ?? "").Trim();
            var user = (connEl.Element("username")?.Value ?? "").Trim();

            DbType dbType = typeStr switch
            {
                "ORACLE"     => DbType.Oracle,
                "MYSQL"      => DbType.MySQL,
                "MARIADB"    => DbType.MariaDB,
                "POSTGRESQL" => DbType.PostgreSQL,
                "MSSQL"      => DbType.SqlServer,
                "MSSQLSERVER"=> DbType.SqlServer,
                "SQLSERVER"  => DbType.SqlServer,
                "SQLITE"     => DbType.Sqlite,
                _            => DbType.MySQL
            };

            // パスワードは Pentaho 暗号化のため復元不能。空のまま。
            string connStr = dbType switch
            {
                DbType.Oracle =>
                    $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={server})(PORT={(string.IsNullOrEmpty(port) ? "1521" : port)}))(CONNECT_DATA=(SERVICE_NAME={database})));User Id={user};Password=;",
                DbType.PostgreSQL =>
                    $"Host={server};Port={(string.IsNullOrEmpty(port) ? "5432" : port)};Database={database};Username={user};Password=;",
                DbType.SqlServer =>
                    $"Server={server},{(string.IsNullOrEmpty(port) ? "1433" : port)};Database={database};User Id={user};Password=;TrustServerCertificate=True;",
                DbType.Sqlite =>
                    $"Data Source={database};",
                _ =>
                    $"Server={server};Port={(string.IsNullOrEmpty(port) ? "3306" : port)};Database={database};Uid={user};Pwd=;"
            };

            return new DbConnectionInfo
            {
                Id = Guid.NewGuid(),
                Name = name,
                DbType = dbType,
                ConnectionString = connStr
            };
        }

        // -------- KTR ステップ パース --------

        private static KtrStep ParseKtrStep(XElement el)
        {
            var step = new KtrStep
            {
                Name = el.Element("name")?.Value?.Trim() ?? "",
                Type = el.Element("type")?.Value?.Trim() ?? "",
                Element = el
            };

            var gui = el.Element("GUI");
            int.TryParse(gui?.Element("xloc")?.Value, out step.X);
            int.TryParse(gui?.Element("yloc")?.Value, out step.Y);
            return step;
        }

        // -------- SetVariable 統合パターン検知 --------

        private static (Dictionary<string, string> Replacement, HashSet<string> Removed, List<JsonStep> Synthetic,
                        List<string> SetVarFieldOrder)
            DetectSetVariablePatterns(List<KtrStep> steps, List<(string From, string To)> hops, List<string> warnings)
        {
            var replacement = new Dictionary<string, string>(StringComparer.Ordinal);
            var removed = new HashSet<string>(StringComparer.Ordinal);
            var synthetic = new List<JsonStep>();
            var setVarFieldOrder = new List<string>();

            var byName = steps.ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);

            // RowGenerator → ScriptValueMod の hop を探す
            foreach (var hop in hops)
            {
                if (!byName.TryGetValue(hop.From, out var src)) continue;
                if (!byName.TryGetValue(hop.To, out var dst)) continue;
                if (!string.Equals(src.Type, "RowGenerator", StringComparison.Ordinal)) continue;
                if (!string.Equals(dst.Type, "ScriptValueMod", StringComparison.Ordinal)) continue;
                if (replacement.ContainsKey(src.Name)) continue; // すでに統合済み

                var setVarFields = TryParseDateScript(dst.Element);
                if (setVarFields == null)
                {
                    warnings.Add(
                        $"ScriptValueMod '{dst.Name}' は日付セットの定型パターンに合致しないため SetVariable へ統合できません。" +
                        " 元のステップは Dummy として残します。");
                    continue;
                }

                // ScriptValueMod の直後の SelectValues も同名フィールドのみなら統合（不要なフィルタなので除外）
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
                synthetic.Add(syn);

                // 後続 TableInput の `?` 置換用に最初に検出したフィールド順を記憶
                if (setVarFieldOrder.Count == 0)
                    setVarFieldOrder.AddRange(setVarFields.Keys);

                // 元の RowGenerator / ScriptValueMod を削除し、両方とも合成ステップにリダイレクト
                removed.Add(src.Name);
                removed.Add(dst.Name);
                replacement[src.Name] = syn.Name;
                replacement[dst.Name] = syn.Name;

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
                        removed.Add(child.Name);
                        replacement[child.Name] = syn.Name;
                    }
                }
            }

            return (replacement, removed, synthetic, setVarFieldOrder);
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

            // 例: start_date.setValue(s); end_date.setValue(e);
            var setValueMatches = Regex.Matches(script, @"(\w+)\s*\.\s*setValue\s*\(\s*(\w+)\s*\)");
            if (setValueMatches.Count == 0) return null;

            // 変数 s, e がそれぞれ「昨日(yesterday)」「今日(today)」由来かを判定
            bool ScriptUsesYesterday(string varName)
            {
                // var s = startYear + '/' + startMonth + ... の右辺を見る
                var rhsMatch = Regex.Match(script,
                    @"var\s+" + Regex.Escape(varName) + @"\s*=\s*([^;]+);",
                    RegexOptions.Multiline);
                if (!rhsMatch.Success) return false;
                var rhs = rhsMatch.Groups[1].Value;
                // 'startYear' や 'yesterday' が出ていれば昨日由来とみなす
                return Regex.IsMatch(rhs, @"\b(start(Year|Month|Day|Yaer)|yesterday)\b", RegexOptions.IgnoreCase);
            }

            bool ScriptUsesToday(string varName)
            {
                var rhsMatch = Regex.Match(script,
                    @"var\s+" + Regex.Escape(varName) + @"\s*=\s*([^;]+);",
                    RegexOptions.Multiline);
                if (!rhsMatch.Success) return false;
                var rhs = rhsMatch.Groups[1].Value;
                return Regex.IsMatch(rhs, @"\b(end(Year|Month|Day|Yaer)|today)\b", RegexOptions.IgnoreCase);
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in setValueMatches)
            {
                var fieldName = m.Groups[1].Value;
                var sourceVar = m.Groups[2].Value;
                if (ScriptUsesYesterday(sourceVar))
                    fields[fieldName] = "YESTERDAY";
                else if (ScriptUsesToday(sourceVar))
                    fields[fieldName] = "TODAY";
                else
                    return null; // 未対応パターン
            }

            return fields.Count > 0 ? fields : null;
        }

        private static string? ResolveName(string original, Dictionary<string, string> replacement)
            => replacement.TryGetValue(original, out var mapped) ? mapped : original;

        // -------- ステップ変換 --------

        private static JsonStep? ConvertStep(KtrStep step, Dictionary<string, Guid> connMap, List<string> warnings,
                                              IReadOnlyList<string>? defaultPlaceholderNames)
        {
            var json = new JsonStep
            {
                Name = step.Name,
                CanvasX = step.X,
                CanvasY = step.Y,
                NodeWidth = 170,
                NodeHeight = 75
            };

            switch (step.Type)
            {
                case "TableInput":
                    json.StepType = "DBInput";
                    FillTableInput(step, json, connMap, warnings, defaultPlaceholderNames);
                    return json;

                case "TableOutput":
                    json.StepType = "DBOutput";
                    FillTableOutput(step, json, connMap);
                    return json;

                case "InsertUpdate":
                    json.StepType = "InsertUpdate";
                    FillInsertUpdate(step, json, connMap);
                    return json;

                case "Update":
                    json.StepType = "DBUpdate";
                    FillUpdate(step, json, connMap);
                    return json;

                case "Delete":
                    json.StepType = "DBDelete";
                    FillDelete(step, json, connMap);
                    return json;

                case "ExecSQL":
                case "ExecSQLRow":
                    json.StepType = "ExecSQL";
                    FillExecSql(step, json, connMap);
                    return json;

                case "MergeJoin":
                    json.StepType = "MergeJoin";
                    FillMergeJoin(step, json, warnings);
                    return json;

                case "SelectValues":
                    json.StepType = "SelectValues";
                    FillSelectValues(step, json);
                    return json;

                case "Calculator":
                    json.StepType = "Calculation";
                    FillCalculator(step, json, warnings);
                    return json;

                case "Formula":
                    json.StepType = "Calculation";
                    FillFormula(step, json, warnings);
                    return json;

                case "FilterRows":
                    json.StepType = "Filter";
                    FillFilterRows(step, json, warnings);
                    return json;

                case "Dummy":
                    json.StepType = "Dummy";
                    return json;

                case "RowGenerator":
                case "ScriptValueMod":
                    // SetVariable 統合に失敗したケース: Dummy 化して原 XML を保持
                    json.StepType = "Dummy";
                    json.Settings["OriginalKtrType"] = step.Type;
                    json.Settings["OriginalXml"] = step.Element.ToString(SaveOptions.DisableFormatting);
                    warnings.Add($"ステップ '{step.Name}' (type={step.Type}) は SampleELT に対応する種別が無いため Dummy に置換しました。");
                    return json;

                default:
                    json.StepType = "Dummy";
                    json.Settings["OriginalKtrType"] = step.Type;
                    json.Settings["OriginalXml"] = step.Element.ToString(SaveOptions.DisableFormatting);
                    warnings.Add($"未対応のステップ種別 '{step.Type}' (name='{step.Name}') を Dummy に置換しました。");
                    return json;
            }
        }

        // ---- 個別ステップ詳細 ----

        private static void FillTableInput(KtrStep step, JsonStep json, Dictionary<string, Guid> connMap,
                                            List<string> warnings, IReadOnlyList<string>? defaultPlaceholderNames)
        {
            var connName = step.Element.Element("connection")?.Value?.Trim() ?? "";
            if (connMap.TryGetValue(connName, out var connId))
                json.Settings["ConnectionId"] = connId.ToString();

            var sql = step.Element.Element("sql")?.Value ?? "";
            sql = NormalizeSqlPlaceholders(sql, warnings, step.Name, defaultPlaceholderNames);
            json.Settings["SQL"] = sql;

            var eachRow = (string?)step.Element.Element("execute_each_row") ?? "N";
            json.Settings["ExecuteEachRow"] = string.Equals(eachRow, "Y", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

            json.NodeWidth = 220;
            json.NodeHeight = 80;
        }

        private static void FillTableOutput(KtrStep step, JsonStep json, Dictionary<string, Guid> connMap)
        {
            var connName = step.Element.Element("connection")?.Value?.Trim() ?? "";
            if (connMap.TryGetValue(connName, out var connId))
                json.Settings["ConnectionId"] = connId.ToString();

            json.Settings["TableName"] = step.Element.Element("table")?.Value?.Trim() ?? "";
            json.Settings["Mode"] = "INSERT";
            var commit = step.Element.Element("commit")?.Value?.Trim();
            json.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }

        private static void FillInsertUpdate(KtrStep step, JsonStep json, Dictionary<string, Guid> connMap)
        {
            var connName = step.Element.Element("connection")?.Value?.Trim() ?? "";
            if (connMap.TryGetValue(connName, out var connId))
                json.Settings["ConnectionId"] = connId.ToString();

            var lookup = step.Element.Element("lookup");
            json.Settings["TableName"] = lookup?.Element("table")?.Value?.Trim() ?? "";

            var keys = lookup?.Elements("key")
                .Select(k => (k.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            json.Settings["KeyFields"] = string.Join(",", keys);

            var values = lookup?.Elements("value")
                .Select(v => (v.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            json.Settings["UpdateFields"] = string.Join(",", keys.Concat(values));

            var commit = step.Element.Element("commit")?.Value?.Trim();
            json.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }

        private static void FillUpdate(KtrStep step, JsonStep json, Dictionary<string, Guid> connMap)
        {
            var connName = step.Element.Element("connection")?.Value?.Trim() ?? "";
            if (connMap.TryGetValue(connName, out var connId))
                json.Settings["ConnectionId"] = connId.ToString();

            var lookup = step.Element.Element("lookup");
            json.Settings["TableName"] = lookup?.Element("table")?.Value?.Trim() ?? "";

            var keys = lookup?.Elements("key")
                .Select(k => (k.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            json.Settings["KeyFields"] = string.Join(",", keys);

            var values = lookup?.Elements("value")
                .Select(v => (v.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            json.Settings["UpdateFields"] = string.Join(",", values);

            var commit = step.Element.Element("commit")?.Value?.Trim();
            json.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }

        private static void FillDelete(KtrStep step, JsonStep json, Dictionary<string, Guid> connMap)
        {
            var connName = step.Element.Element("connection")?.Value?.Trim() ?? "";
            if (connMap.TryGetValue(connName, out var connId))
                json.Settings["ConnectionId"] = connId.ToString();

            var lookup = step.Element.Element("lookup");
            json.Settings["TableName"] = lookup?.Element("table")?.Value?.Trim() ?? "";
            var keys = lookup?.Elements("key")
                .Select(k => (k.Element("name")?.Value ?? "").Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            json.Settings["KeyFields"] = string.Join(",", keys);

            var commit = step.Element.Element("commit")?.Value?.Trim();
            json.Settings["CommitSize"] = string.IsNullOrEmpty(commit) ? "100" : commit;
        }

        private static void FillExecSql(KtrStep step, JsonStep json, Dictionary<string, Guid> connMap)
        {
            var connName = step.Element.Element("connection")?.Value?.Trim() ?? "";
            if (connMap.TryGetValue(connName, out var connId))
                json.Settings["ConnectionId"] = connId.ToString();
            json.Settings["SQL"] = step.Element.Element("sql")?.Value ?? "";
            var eachRow = (string?)step.Element.Element("execute_each_row") ?? "N";
            json.Settings["ExecuteEachRow"] = string.Equals(eachRow, "Y", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        }

        private static void FillMergeJoin(KtrStep step, JsonStep json, List<string> warnings)
        {
            var joinType = (step.Element.Element("join_type")?.Value ?? "INNER").Trim().ToUpperInvariant();
            json.Settings["JoinType"] = joinType;

            var keys1 = step.Element.Element("keys_1")?.Elements("key")
                .Select(k => k.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                ?? new List<string>();
            var keys2 = step.Element.Element("keys_2")?.Elements("key")
                .Select(k => k.Value.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                ?? new List<string>();

            json.Settings["KeyFields"] = string.Join(",", keys1);

            if (keys2.Count > 0 && !keys1.SequenceEqual(keys2, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add(
                    $"MergeJoin '{step.Name}': 左右で異なるキー (left={string.Join(",", keys1)} / right={string.Join(",", keys2)}) " +
                    "が指定されています。SampleELT は単一の KeyFields のみサポートのため左側 (keys_1) を採用しました。");
            }
        }

        private static void FillSelectValues(KtrStep step, JsonStep json)
        {
            var lines = new List<string>();
            foreach (var f in step.Element.Element("fields")?.Elements("field") ?? Enumerable.Empty<XElement>())
            {
                var name = (f.Element("name")?.Value ?? "").Trim();
                var rename = (f.Element("rename")?.Value ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                lines.Add(string.IsNullOrEmpty(rename) || rename == name ? name : $"{name}={rename}");
            }
            json.Settings["FieldMappings"] = string.Join("\r\n", lines);
        }

        private static void FillCalculator(KtrStep step, JsonStep json, List<string> warnings)
        {
            // KTR Calculator は複数式を持つが、SampleELT は1式しか持てないので最初の有効計算のみ取り込む。
            var calc = step.Element.Element("calculation")?.Elements("calculation").FirstOrDefault()
                       ?? step.Element.Elements("calculation").FirstOrDefault();
            if (calc == null)
            {
                json.Settings["OutputFieldName"] = "Result";
                json.Settings["ExpressionType"] = "add";
                json.Settings["Field1"] = "";
                json.Settings["Field2"] = "";
                json.Settings["Constant"] = "0";
                warnings.Add($"Calculator '{step.Name}': 計算定義が読み取れませんでした。手動で設定してください。");
                return;
            }

            var fieldName = (calc.Element("field_name")?.Value ?? "Result").Trim();
            var calcType = (calc.Element("calc_type")?.Value ?? "").Trim();
            var fa = (calc.Element("field_a")?.Value ?? "").Trim();
            var fb = (calc.Element("field_b")?.Value ?? "").Trim();

            string mapped = calcType switch
            {
                "ADD"      => "add",
                "SUBTRACT" => "subtract",
                "MULTIPLY" => "multiply",
                "DIVIDE"   => "divide",
                "DATE_DIFF"=> "dateDiffMinutes",
                _          => "add"
            };
            json.Settings["OutputFieldName"] = fieldName;
            json.Settings["ExpressionType"] = mapped;
            json.Settings["Field1"] = fa;
            json.Settings["Field2"] = fb;
            json.Settings["Constant"] = "0";

            if (mapped == "add" && !string.Equals(calcType, "ADD", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Calculator '{step.Name}': calc_type='{calcType}' は近似マップ ('add') で取り込みました。手動確認してください。");
        }

        private static void FillFormula(KtrStep step, JsonStep json, List<string> warnings)
        {
            var formula = step.Element.Element("formula") ?? step.Element.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "formula");
            var fieldName = (formula?.Element("field_name")?.Value ?? "Result").Trim();
            var formulaText = (formula?.Element("formula_string")?.Value ?? "").Trim();

            json.Settings["OutputFieldName"] = fieldName;
            json.Settings["Constant"] = "0";

            // 「([F1]-[F2])*24*60」→ dateDiffMinutes
            var diffMin = Regex.Match(formulaText, @"\(\s*\[(\w+)\]\s*-\s*\[(\w+)\]\s*\)\s*\*\s*24\s*\*\s*60");
            if (diffMin.Success)
            {
                json.Settings["ExpressionType"] = "dateDiffMinutes";
                json.Settings["Field1"] = diffMin.Groups[1].Value;
                json.Settings["Field2"] = diffMin.Groups[2].Value;
                return;
            }

            // [A] op [B] の単純2項
            var simple = Regex.Match(formulaText, @"\[(\w+)\]\s*([+\-*/])\s*\[(\w+)\]");
            if (simple.Success)
            {
                var op = simple.Groups[2].Value;
                json.Settings["ExpressionType"] = op switch
                {
                    "+" => "add",
                    "-" => "subtract",
                    "*" => "multiply",
                    "/" => "divide",
                    _   => "add"
                };
                json.Settings["Field1"] = simple.Groups[1].Value;
                json.Settings["Field2"] = simple.Groups[3].Value;
                return;
            }

            // 解析不能 → add で空フィールド + 警告
            json.Settings["ExpressionType"] = "add";
            json.Settings["Field1"] = "";
            json.Settings["Field2"] = "";
            warnings.Add(
                $"Formula '{step.Name}': 式 \"{formulaText}\" を Calculation の標準パターンへ自動変換できませんでした。手動で設定してください。");
        }

        private static void FillFilterRows(KtrStep step, JsonStep json, List<string> warnings)
        {
            // KTR の <condition> はネスト可能だが、SampleELT は単純比較のみ。簡易対応。
            var cond = step.Element.Element("condition");
            var leftField = cond?.Element("leftvalue")?.Value?.Trim() ?? "";
            var op = (cond?.Element("function")?.Value ?? "=").Trim();
            var rightField = cond?.Element("rightvalue")?.Value?.Trim() ?? "";
            var constant = cond?.Element("value")?.Element("text")?.Value?.Trim() ?? "";

            json.Settings["FieldName"] = leftField;
            json.Settings["Operator"] = op switch
            {
                "="           => "equals",
                "<>"          => "notEquals",
                ">"           => "greaterThan",
                ">="          => "greaterOrEqual",
                "<"           => "lessThan",
                "<="          => "lessOrEqual",
                "LIKE"        => "contains",
                _             => "equals"
            };
            json.Settings["Value"] = constant;
            json.Settings["RightField"] = rightField;

            warnings.Add(
                $"FilterRows '{step.Name}': 条件式は単純比較として取り込みました。複合条件 (AND/OR) がある場合は手動で確認してください。");
        }

        // -------- SQL プレースホルダ正規化 --------

        /// <summary>
        /// KTR の `?` プレースホルダを SampleELT の `:{name}` 形式に置換する。
        /// 前段 SetVariable のフィールド名が判明していれば出現順に当てはめ、
        /// 不明な分は :{p1}, :{p2}, ... のフォールバックを使い警告を出す。
        /// </summary>
        private static string NormalizeSqlPlaceholders(string sql, List<string> warnings, string stepName,
                                                       IReadOnlyList<string>? defaultPlaceholderNames)
        {
            int count = 0;
            int unmatched = 0;
            // 文字列リテラル中の '?' は置換しない
            var sb = new System.Text.StringBuilder(sql.Length + 32);
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                if (c == '\'' && !inDoubleQuote) inSingleQuote = !inSingleQuote;
                else if (c == '"' && !inSingleQuote) inDoubleQuote = !inDoubleQuote;

                if (c == '?' && !inSingleQuote && !inDoubleQuote)
                {
                    count++;
                    string name;
                    if (defaultPlaceholderNames != null && count - 1 < defaultPlaceholderNames.Count)
                    {
                        name = defaultPlaceholderNames[count - 1];
                    }
                    else
                    {
                        name = "p" + count;
                        unmatched++;
                    }
                    sb.Append(":{").Append(name).Append('}');
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (count > 0 && unmatched > 0)
            {
                warnings.Add(
                    $"DBInput '{stepName}': SQL 内の `?` のうち {unmatched} 件は前段 SetVariable のフィールド名で解決できず " +
                    ":{p<n>} 形式にフォールバックしました。手動で書き換えてください。");
            }
            return sb.ToString();
        }

        // -------- JSON シリアライズ --------

        private static string SerializePipeline(string name, IEnumerable<JsonStep> steps, IEnumerable<JsonHop> hops)
        {
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("Name", name);

                writer.WritePropertyName("Steps");
                writer.WriteStartArray();
                foreach (var s in steps)
                {
                    writer.WriteStartObject();
                    writer.WriteString("Id", s.Id.ToString());
                    writer.WriteString("Name", s.Name);
                    writer.WriteString("StepType", s.StepType);
                    writer.WriteNumber("CanvasX", s.CanvasX);
                    writer.WriteNumber("CanvasY", s.CanvasY);
                    writer.WriteNumber("NodeWidth", s.NodeWidth);
                    writer.WriteNumber("NodeHeight", s.NodeHeight);

                    writer.WritePropertyName("Settings");
                    writer.WriteStartObject();
                    foreach (var kv in s.Settings)
                        writer.WriteString(kv.Key, kv.Value?.ToString() ?? "");
                    writer.WriteEndObject();

                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WritePropertyName("Connections");
                writer.WriteStartArray();
                foreach (var h in hops)
                {
                    writer.WriteStartObject();
                    writer.WriteString("Id", h.Id.ToString());
                    writer.WriteString("SourceStepId", h.SourceStepId.ToString());
                    writer.WriteString("TargetStepId", h.TargetStepId.ToString());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
