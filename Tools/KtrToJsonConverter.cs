using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BreezeFlow.Models;
using BreezeFlow.Tools.Ktr;

namespace BreezeFlow.Tools
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
    /// Pentaho Kettle Transformation (.ktr / XML) を BreezeFlow のパイプライン JSON に変換する。
    /// 各 KTR ステップ種別の変換ロジックは <see cref="Ktr.IKtrStepConverter"/> 実装に Strategy パターンで分離されている。
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

        private static KtrConvertResult ConvertInternal(XElement root)
        {
            var result = new KtrConvertResult
            {
                PipelineName = root.Element("info")?.Element("name")?.Value?.Trim() ?? "Pipeline"
            };
            var ctx = new KtrConvertContext();

            // 接続を解決 (警告は ctx.Warnings に集約され、最後に result.Warnings へ転送される)
            KtrConnectionResolver.Resolve(root, ctx, result);

            // KTR ステップ・ホップを収集
            var ktrSteps = root.Elements("step").Select(KtrStep.Parse).ToList();
            var ktrHops = (root.Element("order")?.Elements("hop") ?? Enumerable.Empty<XElement>())
                .Where(h => string.Equals((string?)h.Element("enabled") ?? "Y", "Y", StringComparison.OrdinalIgnoreCase))
                .Select(h => (From: (string)h.Element("from")! ?? "", To: (string)h.Element("to")! ?? ""))
                .ToList();

            // SetVariable 統合パターン検知
            var pattern = KtrSetVariablePatternDetector.Detect(ktrSteps, ktrHops, ctx.Warnings);
            ctx.SetVariableFieldOrder = pattern.SetVarFieldOrder;

            // ステップ変換
            var nameToJson = new Dictionary<string, JsonStep>(StringComparer.Ordinal);
            foreach (var syn in pattern.Synthetic)
                nameToJson[syn.Name] = syn;

            foreach (var step in ktrSteps)
            {
                if (pattern.Removed.Contains(step.Name)) continue;
                if (nameToJson.ContainsKey(step.Name)) continue;

                var converter = KtrStepConverterRegistry.Resolve(step.Type);
                var json = new JsonStep
                {
                    Name = step.Name,
                    StepType = converter.BreezeFlowStepType,
                    CanvasX = step.X,
                    CanvasY = step.Y,
                    NodeWidth = 170,
                    NodeHeight = 75
                };
                converter.Fill(step, json, ctx);
                nameToJson[step.Name] = json;
            }

            // ホップ変換
            var jsonHops = new List<JsonHop>();
            var seenPairs = new HashSet<(Guid, Guid)>();
            foreach (var hop in ktrHops)
            {
                var fromName = ResolveName(hop.From, pattern.Replacement);
                var toName = ResolveName(hop.To, pattern.Replacement);
                if (fromName == null || toName == null) continue;
                if (fromName == toName) continue;
                if (!nameToJson.TryGetValue(fromName, out var fromStep)) continue;
                if (!nameToJson.TryGetValue(toName, out var toStep)) continue;
                if (!seenPairs.Add((fromStep.Id, toStep.Id))) continue;

                jsonHops.Add(new JsonHop
                {
                    SourceStepId = fromStep.Id,
                    TargetStepId = toStep.Id
                });
            }

            // SetVariable + DBInput を含むパイプラインへのヒント
            if (pattern.SetVarFieldOrder.Count > 0 && nameToJson.Values.Any(s => s.StepType == "DBInput"))
            {
                var fieldList = string.Join(", ", pattern.SetVarFieldOrder.Select(f => $":{{{f}}} AS {f.ToUpper()}_VAR"));
                ctx.Warnings.Add(
                    "ヒント: 期間内の行のみを Update したい場合は、各 DBInput の SELECT に " +
                    $"`{fieldList}` を追加し、Update 直前に Filter ステップ" +
                    $"（{pattern.SetVarFieldOrder.First().ToUpper()}_VAR / {pattern.SetVarFieldOrder.Last().ToUpper()}_VAR を使った範囲条件）" +
                    "を挟んでください。Filter は greaterThan / lessOrEqual 演算子と RightField を使うと表現できます。");
            }

            // 警告を結果に転送
            result.Warnings.AddRange(ctx.Warnings);

            // JSON シリアライズ
            result.PipelineJson = KtrPipelineSerializer.Serialize(result.PipelineName, nameToJson.Values, jsonHops);
            return result;
        }

        private static string? ResolveName(string original, Dictionary<string, string> replacement)
            => replacement.TryGetValue(original, out var mapped) ? mapped : original;
    }
}
