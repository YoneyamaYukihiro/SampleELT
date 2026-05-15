using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BreezeFlow.Models;
using BreezeFlow.Steps;
using BreezeFlow.Models.Stores;
using BreezeFlow.Tools;

namespace BreezeFlow.Dialogs
{
    public partial class SelectValuesDialog : Window
    {
        public string StepName { get; private set; } = "";
        public string FieldMappings { get; private set; } = "";

        private readonly ObservableCollection<FieldMappingItem> _items = new();
        private Pipeline? _pipeline;
        private Guid _currentStepId;

        private static readonly string[] DataTypeOptions =
            { "", "string", "int", "long", "decimal", "double", "datetime", "bool" };

        public SelectValuesDialog()
        {
            InitializeComponent();
            MappingGrid.ItemsSource = _items;
            DataTypeColumn.ItemsSource = DataTypeOptions;
            _items.CollectionChanged += (_, _) => RefreshStatus();
        }

        public void Initialize(string stepName, string fieldMappings)
            => Initialize(stepName, fieldMappings, null, Guid.Empty);

        /// <summary>
        /// <param name="pipeline">前段フィールド取得に使用 (null なら「前段から取得」を無効化)</param>
        /// <param name="currentStepId">現在のステップ ID。前段ステップを辿るのに使用</param>
        /// </summary>
        public void Initialize(string stepName, string fieldMappings, Pipeline? pipeline, Guid currentStepId)
        {
            StepNameBox.Text = stepName;
            _items.Clear();
            foreach (var item in FieldMappingItem.Parse(fieldMappings))
                _items.Add(item);

            _pipeline = pipeline;
            _currentStepId = currentStepId;
            FetchFromUpstreamButton.IsEnabled = _pipeline != null && _currentStepId != Guid.Empty;

            RefreshStatus();
        }

        // ==================== 行操作 ====================

        private void AddRow_Click(object sender, RoutedEventArgs e)
        {
            var item = new FieldMappingItem { IsIncluded = true };
            _items.Add(item);
            MappingGrid.SelectedItem = item;
            MappingGrid.ScrollIntoView(item);
        }

        private void AddConstRow_Click(object sender, RoutedEventArgs e)
        {
            // 元名は空、新名と定数値はユーザに入力してもらう
            var item = new FieldMappingItem { IsIncluded = true, ConstantValue = " " };
            _items.Add(item);
            MappingGrid.SelectedItem = item;
            MappingGrid.ScrollIntoView(item);
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            if (MappingGrid.SelectedItem is FieldMappingItem item)
                _items.Remove(item);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = MappingGrid.SelectedIndex;
            if (idx <= 0) return;
            _items.Move(idx, idx - 1);
            MappingGrid.SelectedIndex = idx - 1;
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = MappingGrid.SelectedIndex;
            if (idx < 0 || idx >= _items.Count - 1) return;
            _items.Move(idx, idx + 1);
            MappingGrid.SelectedIndex = idx + 1;
        }

        // ==================== 前段から取得 ====================

        private async void FetchFromUpstream_Click(object sender, RoutedEventArgs e)
        {
            if (_pipeline == null || _currentStepId == Guid.Empty) return;

            FetchFromUpstreamButton.IsEnabled = false;
            try
            {
                StatusText.Text = "前段ステップを実行プレビュー中...";
                var fields = await GetUpstreamFieldsAsync();
                if (fields.Count == 0)
                {
                    StatusText.Text = "前段から取得できるフィールド名がありませんでした。";
                    return;
                }

                // 既存マッピングと重複しないものだけ追加
                var existing = new HashSet<string>(_items.Select(i => i.SourceName),
                    StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (var f in fields)
                {
                    if (existing.Contains(f)) continue;
                    _items.Add(new FieldMappingItem { SourceName = f, IsIncluded = true });
                    added++;
                }

                RefreshStatus();
                StatusText.Text = added > 0
                    ? $"{added} 件のフィールドを追加しました (前段の出力: {fields.Count} 件)"
                    : $"重複のため新規追加なし (前段の出力: {fields.Count} 件)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"取得失敗: {ex.Message}";
            }
            finally
            {
                FetchFromUpstreamButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// 直前 (incoming) のステップを 1 件だけ実行プレビューしてフィールド名一覧を返す。
        /// 複数前段がある場合は最初の 1 件のみ。
        /// </summary>
        private async Task<List<string>> GetUpstreamFieldsAsync()
        {
            if (_pipeline == null) return new();

            // 自ステップの直前ステップを Connections から探す
            var incoming = _pipeline.Connections
                .Where(c => c.TargetStepId == _currentStepId)
                .Select(c => c.SourceStepId)
                .ToList();
            if (incoming.Count == 0) return new();

            var prevStep = _pipeline.Steps.FirstOrDefault(s => s.Id == incoming[0]);
            if (prevStep == null) return new();

            // 静的に解決できるケース (SetVariable / SelectValues)
            var staticFields = TryGetStaticFields(prevStep);
            if (staticFields.Count > 0) return staticFields;

            // 実行プレビューを試みる (DBInput / FileInput など)
            var rows = await TryExecutePreviewAsync(prevStep);
            if (rows.Count > 0) return rows[0].Keys.ToList();

            return new();
        }

        /// <summary>ステップ実行なしでフィールド名が分かるケースの抽出。</summary>
        private static List<string> TryGetStaticFields(StepBase step)
        {
            switch (step.StepType)
            {
                case StepType.SetVariable:
                {
                    var raw = step.Settings.TryGetValue("Fields", out var v) ? v?.ToString() ?? "" : "";
                    return raw.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l) && l.Contains('='))
                        .Select(l => l.Substring(0, l.IndexOf('=')).Trim())
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                }
                case StepType.SelectValues:
                {
                    var raw = step.Settings.TryGetValue("FieldMappings", out var v) ? v?.ToString() ?? "" : "";
                    return FieldMappingItem.Parse(raw)
                        .Where(i => i.IsIncluded)
                        .Select(i => i.EffectiveDestName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                }
                case StepType.DBInput:
                case StepType.OracleInput:
                case StepType.MySQLInput:
                {
                    // SQL を構文解析して SELECT 句のカラム名を抽出 (実行せず、? パラメータがあっても OK)
                    var sql = step.Settings.TryGetValue("SQL", out var v) ? v?.ToString() ?? "" : "";
                    return SqlColumnExtractor.Extract(sql);
                }
                default:
                    return new();
            }
        }

        /// <summary>1 行プレビュー実行で出力カラムを得る (DBInput / ExcelInput 向け)。</summary>
        private static async Task<List<Dictionary<string, object?>>> TryExecutePreviewAsync(StepBase step)
        {
            try
            {
                // ExcelInputStep は MaxRows をサポート
                if (step is ExcelInputStep)
                {
                    var save = step.Settings.TryGetValue("MaxRows", out var prev) ? prev : null;
                    step.Settings["MaxRows"] = "1";
                    try
                    {
                        return await step.ExecuteAsync(
                            new List<Dictionary<string, object?>>(),
                            new Progress<string>(), CancellationToken.None);
                    }
                    finally
                    {
                        if (prev != null) step.Settings["MaxRows"] = prev;
                        else step.Settings.Remove("MaxRows");
                    }
                }

                // DBInput / 他は通常実行 (大きいデータだと遅い)
                return await step.ExecuteAsync(
                    new List<Dictionary<string, object?>>(),
                    new Progress<string>(), CancellationToken.None);
            }
            catch
            {
                return new();
            }
        }

        // ==================== 状態表示 ====================

        private void RefreshStatus()
        {
            var active = _items
                .Where(i => i.IsIncluded && (!string.IsNullOrEmpty(i.SourceName) || i.IsConstant))
                .ToList();
            var warnings = new List<string>();

            // 新名の重複検出 (定数行と入力行をまとめてチェック)
            var dests = active
                .GroupBy(i => i.EffectiveDestName, StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (dests.Count > 0)
                warnings.Add($"新名が重複: {string.Join(", ", dests)}");

            // 元名の重複検出 (入力モードのみ)
            var srcs = active
                .Where(i => !i.IsConstant)
                .GroupBy(i => i.SourceName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (srcs.Count > 0)
                warnings.Add($"元名が重複: {string.Join(", ", srcs)}");

            // 定数モードで新名 (出力先カラム名) 未設定の警告
            var constNoDest = active.Where(i => i.IsConstant && string.IsNullOrEmpty(i.DestName)).Count();
            if (constNoDest > 0)
                warnings.Add($"定数行に新名未設定: {constNoDest} 件");

            var constCount = active.Count(i => i.IsConstant);
            var summary = constCount > 0
                ? $"{active.Count} 件のフィールドを出力 (うち定数 {constCount} 件)"
                : $"{active.Count} 件のフィールドを出力";

            StatusText.Text = warnings.Count > 0
                ? "⚠ " + string.Join(" / ", warnings)
                : summary;
        }

        // ==================== OK/キャンセル ====================

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(StepNameBox.Text))
            {
                MessageBox.Show("ステップ名を入力してください。", "入力エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StepName = StepNameBox.Text.Trim();
            FieldMappings = FieldMappingItem.Serialize(_items);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
