using System;
using System.Collections.Generic;

namespace SampleELT.Models.Stores
{
    /// <summary>
    /// 接続情報の参照系インターフェイス。
    /// Engine やステップ実装はこのインターフェイス越しに接続を解決し、
    /// テストでは差し替え可能とする (DI 軽量版)。
    /// 実体は <see cref="ConnectionRegistry"/>。
    /// </summary>
    public interface IConnectionStore
    {
        DbConnectionInfo? GetById(Guid id);
        string? GetConnectionString(Guid id);
        DbConnectionInfo? FindConnection(Dictionary<string, object?> settings);
        string ResolveConnectionString(Dictionary<string, object?> settings);

        /// <summary>
        /// アプリ全体で共有される現在のストア。デフォルトは <see cref="ConnectionRegistry.Instance"/>。
        /// テストでモックを差し込みたい場合はここに代入し、終了時に元に戻す。
        /// </summary>
        public static IConnectionStore Default { get; set; } = ConnectionRegistry.Instance;
    }
}
