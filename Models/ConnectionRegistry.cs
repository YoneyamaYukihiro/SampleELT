using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using SampleELT.Models.Stores;

namespace SampleELT.Models
{
    public class ConnectionRegistry : IConnectionStore
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
        /// ステップ Settings から DbConnectionInfo を取得する。
        /// </summary>
        public DbConnectionInfo? FindConnection(Dictionary<string, object?> settings)
        {
            if (settings.TryGetValue("ConnectionId", out var idObj) &&
                idObj != null &&
                Guid.TryParse(idObj.ToString(), out var id))
            {
                return GetById(id);
            }
            return null;
        }

        /// <summary>
        /// ステップ Settings から ConnectionId → ConnectionString を解決する。
        ///
        /// <para>
        /// 解決ルール (誤った DB への書き込み・削除を防ぐため厳格):
        /// 1. <c>Settings["ConnectionId"]</c> が指定されている場合:
        ///    - レジストリで解決できれば、その接続文字列を返す
        ///    - 解決できない (削除済み / 別環境からのコピーで Guid 不一致 等) ときは
        ///      <see cref="ConnectionResolutionException"/> をスロー。
        ///      旧形式 <c>Settings["ConnectionString"]</c> へのサイレントフォールバックは行わない。
        /// 2. <c>ConnectionId</c> が未指定の場合のみ、旧形式 <c>Settings["ConnectionString"]</c>
        ///    にフォールバック (後方互換)。これは ConnectionId を持たない古いパイプライン用。
        /// </para>
        /// </summary>
        public string ResolveConnectionString(Dictionary<string, object?> settings)
        {
            if (settings.TryGetValue("ConnectionId", out var idObj) && idObj != null)
            {
                var raw = idObj.ToString() ?? "";
                if (!Guid.TryParse(raw, out var id))
                    throw new ConnectionResolutionException(
                        $"ConnectionId '{raw}' は Guid として解釈できません。" +
                        "ステップの接続設定を再選択してください。");

                var cs = GetConnectionString(id);
                if (string.IsNullOrEmpty(cs))
                    throw new ConnectionResolutionException(
                        $"ConnectionId '{id}' に対応する接続設定が見つかりません。" +
                        "接続マネージャで該当の接続を選び直してください。" +
                        "(誤った DB への書き込みを防ぐため、旧 ConnectionString へのフォールバックは無効化されています)");

                return cs;
            }
            // ConnectionId 未設定 → 旧形式の直接指定にフォールバック (後方互換)
            return settings.TryGetValue("ConnectionString", out var direct)
                ? direct?.ToString() ?? ""
                : "";
        }
    }
}
