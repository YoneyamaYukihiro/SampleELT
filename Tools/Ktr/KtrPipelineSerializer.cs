using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BreezeFlow.Tools.Ktr
{
    /// <summary>変換結果のステップとホップを BreezeFlow のパイプライン JSON にシリアライズする。</summary>
    internal static class KtrPipelineSerializer
    {
        public static string Serialize(string name, IEnumerable<JsonStep> steps, IEnumerable<JsonHop> hops)
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
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
