using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SampleELT.Models
{
    /// <summary>
    /// SelectValues ステップのフィールドマッピング 1 行分。
    /// シリアライズは <c>SourceName|DestName|DataType|IsIncluded</c> の `|` 区切り。
    /// 後方互換性のため、`=` 形式やフィールド名のみの旧フォーマットも読める。
    /// </summary>
    public class FieldMappingItem : INotifyPropertyChanged
    {
        private string _sourceName    = "";
        private string _destName      = "";
        private string _dataType      = "";
        private bool   _isIncluded    = true;
        private string _constantValue = "";

        public string SourceName
        {
            get => _sourceName;
            set { if (_sourceName != value) { _sourceName = value; OnChanged(nameof(SourceName)); } }
        }

        public string DestName
        {
            get => _destName;
            set { if (_destName != value) { _destName = value; OnChanged(nameof(DestName)); } }
        }

        /// <summary>"" / "string" / "int" / "long" / "decimal" / "double" / "datetime" / "bool"。空文字列なら型変換なし。</summary>
        public string DataType
        {
            get => _dataType;
            set { if (_dataType != value) { _dataType = value; OnChanged(nameof(DataType)); } }
        }

        public bool IsIncluded
        {
            get => _isIncluded;
            set { if (_isIncluded != value) { _isIncluded = value; OnChanged(nameof(IsIncluded)); } }
        }

        /// <summary>
        /// 固定値。空文字列でない場合、入力データには依存せずこの値が出力される (定数モード)。
        /// 型変換 (DataType) はこの値にも適用される。
        /// </summary>
        public string ConstantValue
        {
            get => _constantValue;
            set { if (_constantValue != value) { _constantValue = value; OnChanged(nameof(ConstantValue)); OnChanged(nameof(IsConstant)); } }
        }

        public bool IsConstant => !string.IsNullOrEmpty(ConstantValue);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ==================== シリアライズ ====================

        public static List<FieldMappingItem> Parse(string raw)
        {
            var items = new List<FieldMappingItem>();
            if (string.IsNullOrWhiteSpace(raw)) return items;

            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                FieldMappingItem item;
                if (trimmed.Contains('|'))
                {
                    // 定数値列に `|` を含められるよう、最大 5 分割 (parts[4] に残り全部)
                    var parts = trimmed.Split('|', 5);
                    item = new FieldMappingItem
                    {
                        SourceName    = parts.Length > 0 ? parts[0].Trim() : "",
                        DestName      = parts.Length > 1 ? parts[1].Trim() : "",
                        DataType      = parts.Length > 2 ? parts[2].Trim() : "",
                        IsIncluded    = parts.Length > 3
                            ? !string.Equals(parts[3].Trim(), "false", StringComparison.OrdinalIgnoreCase)
                            : true,
                        ConstantValue = parts.Length > 4 ? parts[4] : ""
                    };
                }
                else if (trimmed.Contains('='))
                {
                    var eqIdx = trimmed.IndexOf('=');
                    item = new FieldMappingItem
                    {
                        SourceName = trimmed.Substring(0, eqIdx).Trim(),
                        DestName   = trimmed.Substring(eqIdx + 1).Trim()
                    };
                }
                else
                {
                    item = new FieldMappingItem { SourceName = trimmed };
                }

                // SourceName が空でも、定数モードなら追加 (DestName か ConstantValue があれば OK)
                bool hasUsefulData = !string.IsNullOrEmpty(item.SourceName)
                                  || !string.IsNullOrEmpty(item.DestName)
                                  || !string.IsNullOrEmpty(item.ConstantValue);
                if (hasUsefulData) items.Add(item);
            }
            return items;
        }

        public static string Serialize(IEnumerable<FieldMappingItem> items)
            => string.Join("\r\n", items
                .Where(i => !string.IsNullOrEmpty(i.SourceName)
                         || !string.IsNullOrEmpty(i.DestName)
                         || !string.IsNullOrEmpty(i.ConstantValue))
                .Select(i => $"{i.SourceName}|{i.DestName}|{i.DataType}|{(i.IsIncluded ? "true" : "false")}|{i.ConstantValue}"));

        /// <summary>実行時に行く先のカラム名（DestName が空なら SourceName）。</summary>
        public string EffectiveDestName
            => string.IsNullOrEmpty(DestName) ? SourceName : DestName;
    }
}
