using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SampleELT.Models
{
    public class ConnectionRegistry
    {
        public static ConnectionRegistry Instance { get; } = new();

        public ObservableCollection<DbConnectionInfo> Connections { get; } = new();

        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "connections.json");

        private ConnectionRegistry() { }

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<DbConnectionInfo>>(json);
                if (list == null) return;
                Connections.Clear();
                foreach (var c in list)
                    Connections.Add(c);
            }
            catch { /* 読み込みエラーは無視 */ }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(Connections.ToList(), options);
                File.WriteAllText(FilePath, json);
            }
            catch { /* 保存エラーは無視 */ }
        }

        public DbConnectionInfo? GetById(Guid id)
            => Connections.FirstOrDefault(c => c.Id == id);

        public string? GetConnectionString(Guid id)
            => GetById(id)?.ConnectionString;

        /// <summary>
        /// ステップ Settings から ConnectionId → ConnectionString を解決する。
        /// ConnectionId が未設定の場合は直接 ConnectionString にフォールバック。
        /// </summary>
        public string ResolveConnectionString(Dictionary<string, object?> settings)
        {
            if (settings.TryGetValue("ConnectionId", out var idObj) &&
                idObj != null &&
                Guid.TryParse(idObj.ToString(), out var id))
            {
                var cs = GetConnectionString(id);
                if (!string.IsNullOrEmpty(cs)) return cs;
            }
            // 旧形式の直接指定にフォールバック（後方互換）
            return settings.TryGetValue("ConnectionString", out var direct)
                ? direct?.ToString() ?? ""
                : "";
        }
    }
}
