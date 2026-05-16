using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using BreezeFlow.Models;

namespace BreezeFlow.ViewModels
{
    public partial class StepNodeViewModel : ObservableObject
    {
        public StepBase Step { get; }

        /// <summary>
        /// このステップの出力ポート一覧 (Switch ステップで動的に変わる)。
        /// 多ポート対応の StepNodeControl が ItemsControl で描画する。
        /// </summary>
        public IReadOnlyList<OutputPort> OutputPorts => Step.OutputPorts;

        /// <summary>
        /// 指定 BranchKey のポートがノード内の縦方向どの位置 (0..1) にあるかを返す。
        /// ConnectionViewModel が Y1 を計算するときに使う。
        /// 該当するポートが無ければ中央 (0.5) を返す。
        /// </summary>
        public double GetPortRelativeY(string? branchKey)
        {
            var ports = OutputPorts;
            if (ports.Count <= 1) return 0.5;
            var key = branchKey ?? string.Empty;
            for (int i = 0; i < ports.Count; i++)
            {
                if (string.Equals(ports[i].Key, key, System.StringComparison.Ordinal))
                    return (i + 0.5) / ports.Count;
            }
            return 0.5;
        }

        /// <summary>
        /// OutputPorts が変化した場合に XAML バインディングと接続線位置を再評価させる。
        /// Switch ダイアログで Cases が編集された後に呼ぶ。
        /// </summary>
        public void NotifyOutputPortsChanged()
        {
            OnPropertyChanged(nameof(OutputPorts));
            // ConnectionViewModel.Y1 の再計算用 (NodeHeight と OutputPorts が組み合わさる)
            OnPropertyChanged(nameof(NodeHeight));
            OnPropertyChanged(nameof(NodeWidth));
        }

        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isConnectionTarget;

        [ObservableProperty]
        private bool _isConnectingMode;

        public string DisplayName => Step.Name;
        public string DisplayIcon => Step.GetDisplayIcon();

        public string TypeLabel => Step.StepType switch
        {
            StepType.DBInput      => "DB Input",
            StepType.DBOutput     => "DB Output",
            StepType.OracleInput  => "Oracle Input",
            StepType.MySQLInput   => "MySQL Input",
            StepType.ExcelInput   => "File Input",
            StepType.OracleOutput => "Oracle Output",
            StepType.MySQLOutput  => "MySQL Output",
            StepType.ExcelOutput  => "File Output",
            StepType.Filter       => "Filter",
            StepType.Calculation  => "Calculation",
            StepType.SelectValues => "Select Values",
            StepType.DBDelete     => "DB Delete",
            StepType.InsertUpdate => "Insert/Update",
            StepType.ExecSQL      => "Exec SQL",
            StepType.Dummy        => "Dummy",
            StepType.MergeJoin    => "Merge Join",
            StepType.DBUpdate     => "DB Update",
            StepType.SetVariable  => "Set Variable",
            StepType.TableCompare => "Table Compare",
            StepType.Switch       => "Switch",
            _ => Step.StepType.ToString()
        };

        /// <summary>
        /// 接続設定名 + 環境バッジ + Select 専用アイコンの表示用文字列。
        /// 例: <c>[PRD] Spirytus TRN01D 🔒</c> / <c>新しい接続</c>。
        /// ConnectionId が紐づかないステップは空文字。
        /// </summary>
        public string ConnectionLabel
        {
            get
            {
                var conn = ResolvedConnection();
                if (conn == null) return "";
                var env = conn.Environment != DbEnvironment.Development
                    ? $"[{conn.EnvironmentBadge}] "
                    : "";
                var ro = conn.IsReadOnly ? " 🔒" : "";
                return $"{env}{conn.Name}{ro}";
            }
        }

        /// <summary>
        /// ConnectionId が指す接続が解決できないとき true。
        /// ステップノードに警告バッジを出す用途。
        /// </summary>
        public bool HasUnresolvedConnection
        {
            get
            {
                if (!Step.Settings.TryGetValue("ConnectionId", out var idObj) || idObj == null)
                    return false; // そもそも ConnectionId を持たないステップ (Filter 等) は対象外
                if (!System.Guid.TryParse(idObj.ToString(), out var id))
                    return true;
                return ConnectionRegistry.Instance.GetById(id) == null;
            }
        }

        /// <summary>Production 接続を使うステップ。ノード枠を強調するため。</summary>
        public bool IsProductionConnection
            => ResolvedConnection()?.Environment == DbEnvironment.Production;

        /// <summary>Staging 接続を使うステップ。接続ラベルをオレンジ表示するため。</summary>
        public bool IsStagingConnection
            => ResolvedConnection()?.Environment == DbEnvironment.Staging;

        private DbConnectionInfo? ResolvedConnection()
        {
            if (!Step.Settings.TryGetValue("ConnectionId", out var idObj) || idObj == null)
                return null;
            if (!System.Guid.TryParse(idObj.ToString(), out var id))
                return null;
            return ConnectionRegistry.Instance.GetById(id);
        }

        [ObservableProperty]
        private double _nodeWidth;

        [ObservableProperty]
        private double _nodeHeight;

        public StepNodeViewModel(StepBase step)
        {
            Step = step;
            _x = step.CanvasX;
            _y = step.CanvasY;
            _nodeWidth = step.NodeWidth;
            _nodeHeight = step.NodeHeight;
        }

        partial void OnXChanged(double value)
        {
            Step.CanvasX = value;
        }

        partial void OnYChanged(double value)
        {
            Step.CanvasY = value;
        }

        partial void OnNodeWidthChanged(double value)
        {
            Step.NodeWidth = value;
        }

        partial void OnNodeHeightChanged(double value)
        {
            Step.NodeHeight = value;
        }

        public void NotifyNameChanged()
        {
            OnPropertyChanged(nameof(DisplayName));
        }

        public void NotifyConnectionChanged()
        {
            OnPropertyChanged(nameof(ConnectionLabel));
            OnPropertyChanged(nameof(HasUnresolvedConnection));
            OnPropertyChanged(nameof(IsProductionConnection));
            OnPropertyChanged(nameof(IsStagingConnection));
        }
    }
}
