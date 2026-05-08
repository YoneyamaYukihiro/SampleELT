using System;
using System.Windows;
using SampleELT.Dialogs;
using SampleELT.Models;
using SampleELT.ViewModels;

namespace SampleELT.Services
{
    /// <summary>
    /// 各ステップタイプの設定ダイアログ表示を一手に引き受けるサービス。
    /// MainWindow からダイアログ起動の責務を分離するために存在する。
    /// 設定値の永続化は <see cref="StepNodeViewModel.Step"/>.Settings に対して直接行う。
    /// </summary>
    public class StepDialogService
    {
        /// <summary>
        /// ステップ種別に応じた設定ダイアログをモーダル表示する。
        /// ユーザーが OK で確定した場合、Step.Settings と Step.Name を更新し ViewModel に通知する。
        /// </summary>
        public void ShowSettingsDialog(Window owner, StepNodeViewModel stepVm)
        {
            switch (stepVm.Step.StepType)
            {
                case StepType.DBInput:       OpenDBInputDialog(owner, stepVm, dbTypeFilter: null); break;
                // 旧 OracleInput / MySQLInput は同じ DBInputDialog を DbType フィルタ付きで再利用
                case StepType.OracleInput:   OpenDBInputDialog(owner, stepVm, dbTypeFilter: DbType.Oracle); break;
                case StepType.MySQLInput:    OpenDBInputDialog(owner, stepVm, dbTypeFilter: DbType.MySQL); break;
                case StepType.ExcelInput:    OpenExcelInputDialog(owner, stepVm); break;
                case StepType.DBOutput:
                case StepType.OracleOutput:
                case StepType.MySQLOutput:   OpenDBOutputDialog(owner, stepVm); break;
                case StepType.ExcelOutput:   OpenExcelOutputDialog(owner, stepVm); break;
                case StepType.Filter:        OpenFilterDialog(owner, stepVm); break;
                case StepType.Calculation:   OpenCalculationDialog(owner, stepVm); break;
                case StepType.SelectValues:  OpenSelectValuesDialog(owner, stepVm); break;
                case StepType.DBDelete:      OpenDBDeleteDialog(owner, stepVm); break;
                case StepType.InsertUpdate:  OpenInsertUpdateDialog(owner, stepVm); break;
                case StepType.ExecSQL:       OpenExecSQLDialog(owner, stepVm); break;
                case StepType.Dummy:         OpenDummyStepDialog(owner, stepVm); break;
                case StepType.MergeJoin:     OpenMergeJoinDialog(owner, stepVm); break;
                case StepType.DBUpdate:      OpenDBUpdateDialog(owner, stepVm); break;
                case StepType.SetVariable:   OpenSetVariableDialog(owner, stepVm); break;
            }
        }

        // ==================== DB Input 系 ====================

        /// <summary>
        /// すべての DB Input 系 (DBInput / OracleInput / MySQLInput) を統一ダイアログで開く。
        /// <paramref name="dbTypeFilter"/> を指定すると接続リストがその DbType に絞られる (旧個別ダイアログ互換)。
        /// </summary>
        private static void OpenDBInputDialog(
            Window owner, StepNodeViewModel stepVm, DbType? dbTypeFilter)
        {
            var step = stepVm.Step;
            var dialog = new DBInputDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                ParseConnId(step.Settings),
                GetString(step.Settings, "SQL"),
                GetBool(step.Settings, "ExecuteEachRow"),
                dbTypeFilter);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"]   = dialog.ConnectionId?.ToString();
                step.Settings["SQL"]            = dialog.SQL;
                step.Settings["ExecuteEachRow"] = dialog.ExecuteEachRow ? "true" : "false";
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== Excel Input ====================

        private static void OpenExcelInputDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExcelInputDialog { Owner = owner };

            var hhRaw = GetString(step.Settings, "HasHeader", null);
            bool hasHeader = hhRaw == null || !bool.TryParse(hhRaw, out var hhBool) ? true : hhBool;

            dialog.Initialize(
                step.Name,
                GetString(step.Settings, "FilePath"),
                GetString(step.Settings, "SheetName"),
                hasHeader,
                GetString(step.Settings, "Format", "Excel")!,
                GetString(step.Settings, "Delimiter", ",")!,
                GetString(step.Settings, "Encoding", "UTF-8")!);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FilePath"]  = dialog.FilePath;
                step.Settings["Format"]    = dialog.Format;
                step.Settings["SheetName"] = dialog.SheetName;
                step.Settings["HasHeader"] = dialog.HasHeader.ToString();
                step.Settings["Delimiter"] = dialog.Delimiter;
                step.Settings["Encoding"]  = dialog.Encoding;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== DB Output 系 ====================

        private static void OpenDBOutputDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new DBOutputDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                ParseConnId(step.Settings),
                GetString(step.Settings, "TableName"),
                GetString(step.Settings, "Mode", "INSERT")!,
                GetInt(step.Settings, "CommitSize", 100));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"]    = dialog.TableName;
                step.Settings["Mode"]         = dialog.Mode;
                step.Settings["CommitSize"]   = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== Excel Output ====================

        private static void OpenExcelOutputDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExcelOutputDialog { Owner = owner };

            var ihRaw = GetString(step.Settings, "IncludeHeader", null);
            bool includeHeader = ihRaw == null || !bool.TryParse(ihRaw, out var ihBool) ? true : ihBool;

            dialog.Initialize(
                step.Name,
                GetString(step.Settings, "FilePath"),
                GetString(step.Settings, "Format", "Excel")!,
                GetString(step.Settings, "SheetName", "Sheet1")!,
                GetString(step.Settings, "Delimiter", ",")!,
                GetString(step.Settings, "Encoding", "UTF-8")!,
                includeHeader);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FilePath"]      = dialog.FilePath;
                step.Settings["Format"]        = dialog.Format;
                step.Settings["SheetName"]     = dialog.SheetName;
                step.Settings["Delimiter"]     = dialog.Delimiter;
                step.Settings["Encoding"]      = dialog.Encoding;
                step.Settings["IncludeHeader"] = dialog.IncludeHeader.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== 変換系 ====================

        private static void OpenFilterDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new FilterDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                GetString(step.Settings, "FieldName"),
                GetString(step.Settings, "Operator", "equals")!,
                GetString(step.Settings, "Value"),
                GetString(step.Settings, "RightField"));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FieldName"]  = dialog.FieldName;
                step.Settings["Operator"]   = dialog.Operator;
                step.Settings["Value"]      = dialog.Value;
                step.Settings["RightField"] = dialog.RightField;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenCalculationDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new CalculationDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                GetString(step.Settings, "OutputFieldName", "Result")!,
                GetString(step.Settings, "ExpressionType", "add")!,
                GetString(step.Settings, "Field1"),
                GetString(step.Settings, "Field2"),
                GetString(step.Settings, "Constant", "0")!);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["OutputFieldName"] = dialog.OutputFieldName;
                step.Settings["ExpressionType"]  = dialog.ExpressionType;
                step.Settings["Field1"]          = dialog.Field1;
                step.Settings["Field2"]          = dialog.Field2;
                step.Settings["Constant"]        = dialog.Constant;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenSelectValuesDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new SelectValuesDialog { Owner = owner };

            dialog.Initialize(step.Name, GetString(step.Settings, "FieldMappings"));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["FieldMappings"] = dialog.FieldMappings;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== DB 書き込み・実行系 ====================

        private static void OpenDBDeleteDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new DBDeleteDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                ParseConnId(step.Settings),
                GetString(step.Settings, "TableName"),
                GetString(step.Settings, "KeyFields"),
                GetInt(step.Settings, "CommitSize", 100));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"]    = dialog.TableName;
                step.Settings["KeyFields"]    = dialog.KeyFields;
                step.Settings["CommitSize"]   = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenInsertUpdateDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new InsertUpdateDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                ParseConnId(step.Settings),
                GetString(step.Settings, "TableName"),
                GetString(step.Settings, "KeyFields"),
                GetString(step.Settings, "UpdateFields"),
                GetInt(step.Settings, "CommitSize", 100));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"]    = dialog.TableName;
                step.Settings["KeyFields"]    = dialog.KeyFields;
                step.Settings["UpdateFields"] = dialog.UpdateFields;
                step.Settings["CommitSize"]   = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenExecSQLDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new ExecSQLDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                ParseConnId(step.Settings),
                GetString(step.Settings, "SQL"),
                GetBool(step.Settings, "ExecuteEachRow"));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"]   = dialog.ConnectionId?.ToString();
                step.Settings["SQL"]            = dialog.SQL;
                step.Settings["ExecuteEachRow"] = dialog.ExecuteEachRow ? "true" : "false";
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenDBUpdateDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new DBUpdateDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                ParseConnId(step.Settings),
                GetString(step.Settings, "TableName"),
                GetString(step.Settings, "KeyFields"),
                GetString(step.Settings, "UpdateFields"),
                GetInt(step.Settings, "CommitSize", 100));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["ConnectionId"] = dialog.ConnectionId?.ToString();
                step.Settings["TableName"]    = dialog.TableName;
                step.Settings["KeyFields"]    = dialog.KeyFields;
                step.Settings["UpdateFields"] = dialog.UpdateFields;
                step.Settings["CommitSize"]   = dialog.CommitSize.ToString();
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== 補助 ====================

        private static void OpenDummyStepDialog(Window owner, StepNodeViewModel stepVm)
        {
            var dialog = new DummyStepDialog { Owner = owner };
            dialog.Initialize(stepVm.Step.Name);
            if (dialog.ShowDialog() == true)
            {
                stepVm.Step.Name = dialog.StepName;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenMergeJoinDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new MergeJoinDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                GetString(step.Settings, "JoinType", "INNER")!,
                GetString(step.Settings, "KeyFields"));

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["JoinType"]  = dialog.JoinType;
                step.Settings["KeyFields"] = dialog.KeyFields;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        private static void OpenSetVariableDialog(Window owner, StepNodeViewModel stepVm)
        {
            var step = stepVm.Step;
            var dialog = new SetVariableDialog { Owner = owner };

            dialog.Initialize(
                step.Name,
                GetString(step.Settings, "Fields"),
                GetString(step.Settings, "DateFormat", "yyyy/MM/dd")!);

            if (dialog.ShowDialog() == true)
            {
                step.Name = dialog.StepName;
                step.Settings["Fields"]     = dialog.Fields;
                step.Settings["DateFormat"] = dialog.DateFormat;
                stepVm.NotifyNameChanged();
                stepVm.NotifyConnectionChanged();
            }
        }

        // ==================== Settings 読み出しヘルパ ====================

        private static Guid? ParseConnId(System.Collections.Generic.Dictionary<string, object?> settings)
            => settings.TryGetValue("ConnectionId", out var v)
               && v != null
               && Guid.TryParse(v.ToString(), out var g)
                ? g : (Guid?)null;

        private static string GetString(
            System.Collections.Generic.Dictionary<string, object?> settings,
            string key,
            string? fallback = "")
            => settings.TryGetValue(key, out var v) ? v?.ToString() ?? fallback ?? "" : fallback ?? "";

        private static bool GetBool(
            System.Collections.Generic.Dictionary<string, object?> settings,
            string key)
            => settings.TryGetValue(key, out var v)
               && v?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        private static int GetInt(
            System.Collections.Generic.Dictionary<string, object?> settings,
            string key,
            int fallback)
            => settings.TryGetValue(key, out var v)
               && int.TryParse(v?.ToString(), out var n) ? n : fallback;
    }
}
