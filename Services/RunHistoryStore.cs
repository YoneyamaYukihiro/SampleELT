using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using BreezeFlow.Models;

namespace BreezeFlow.Services
{
    /// <summary>
    /// パイプライン / ジョブ実行履歴を SQLite に永続化するストア。
    ///
    /// - DB ファイル: アプリ実行フォルダ直下の <c>runs.db</c>
    /// - 書き込みは per-call で開閉する SqliteConnection を使用 (内部プーリングが効く)
    /// - WAL モードで複数スレッドからの並行書き込みに耐える
    /// - 失敗時はログ記録自体で例外を出さない (パイプライン本体を巻き込まない)
    ///
    /// ライフサイクル: <see cref="InitializeDefault"/> を起動時に 1 度呼び、以降は <see cref="Instance"/> 経由でアクセス。
    /// 未初期化時 (テスト・CLI 補助等) は <see cref="Instance"/> が null になり、各統合ポイントは記録をスキップする。
    /// </summary>
    public class RunHistoryStore
    {
        public static RunHistoryStore? Instance { get; private set; }

        public static void InitializeDefault(string dbPath)
        {
            Instance = new RunHistoryStore(dbPath);
            Instance.EnsureSchema();
        }

        private readonly string _connectionString;
        private readonly object _writeGate = new();

        public RunHistoryStore(string dbPath)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
        }

        private SqliteConnection Open()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            // WAL + 適度な同期 (耐障害 ≒ NORMAL)
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON;";
                cmd.ExecuteNonQuery();
            }
            return conn;
        }

        public void EnsureSchema()
        {
            lock (_writeGate)
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS runs (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    run_guid      TEXT    NOT NULL,
    pipeline_path TEXT,
    pipeline_name TEXT,
    job_path      TEXT,
    job_name      TEXT,
    trigger       TEXT    NOT NULL,
    started_at    TEXT    NOT NULL,
    ended_at      TEXT,
    duration_ms   INTEGER,
    status        TEXT    NOT NULL,
    error_message TEXT,
    total_rows    INTEGER
);
CREATE INDEX IF NOT EXISTS idx_runs_started  ON runs(started_at DESC);
CREATE INDEX IF NOT EXISTS idx_runs_pipeline ON runs(pipeline_path, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_runs_status   ON runs(status, started_at DESC);

CREATE TABLE IF NOT EXISTS run_steps (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id        INTEGER NOT NULL REFERENCES runs(id) ON DELETE CASCADE,
    step_order    INTEGER NOT NULL,
    step_name     TEXT    NOT NULL,
    step_type     TEXT    NOT NULL,
    started_at    TEXT    NOT NULL,
    ended_at      TEXT,
    duration_ms   INTEGER,
    row_count     INTEGER,
    status        TEXT    NOT NULL,
    error_message TEXT
);
CREATE INDEX IF NOT EXISTS idx_run_steps_run ON run_steps(run_id);

CREATE TABLE IF NOT EXISTS run_logs (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id    INTEGER NOT NULL REFERENCES runs(id) ON DELETE CASCADE,
    timestamp TEXT    NOT NULL,
    level     TEXT    NOT NULL DEFAULT 'INFO',
    message   TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_run_logs_run ON run_logs(run_id, id);
";
                cmd.ExecuteNonQuery();
            }
        }

        // ==================== 書き込み API ====================

        public long BeginRun(RunRecord record)
        {
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO runs (run_guid, pipeline_path, pipeline_name, job_path, job_name, trigger, started_at, status)
VALUES ($guid, $pp, $pn, $jp, $jn, $tr, $start, $status);
SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$guid",   record.RunGuid.ToString());
                    cmd.Parameters.AddWithValue("$pp",     (object?)record.PipelinePath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$pn",     (object?)record.PipelineName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$jp",     (object?)record.JobPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$jn",     (object?)record.JobName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$tr",     record.Trigger);
                    cmd.Parameters.AddWithValue("$start",  FormatIso(record.StartedAt));
                    cmd.Parameters.AddWithValue("$status", record.Status.ToString());
                    var id = (long)cmd.ExecuteScalar()!;
                    record.Id = id;
                    return id;
                }
                catch
                {
                    return -1; // 履歴記録失敗はパイプライン本体を巻き込まない
                }
            }
        }

        public void EndRun(long runId, RunStatus status, string? errorMessage, int? totalRows)
        {
            if (runId <= 0) return;
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
UPDATE runs
   SET ended_at     = $end,
       duration_ms  = CAST((julianday($end) - julianday(started_at)) * 86400000 AS INTEGER),
       status       = $status,
       error_message = $err,
       total_rows   = $rows
 WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$end",    FormatIso(DateTime.Now));
                    cmd.Parameters.AddWithValue("$status", status.ToString());
                    cmd.Parameters.AddWithValue("$err",    (object?)errorMessage ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$rows",   (object?)totalRows ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$id",     runId);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public long RecordStepStart(long runId, int order, string name, string type)
        {
            if (runId <= 0) return -1;
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO run_steps (run_id, step_order, step_name, step_type, started_at, status)
VALUES ($rid, $ord, $name, $type, $start, 'Running');
SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$rid",   runId);
                    cmd.Parameters.AddWithValue("$ord",   order);
                    cmd.Parameters.AddWithValue("$name",  name);
                    cmd.Parameters.AddWithValue("$type",  type);
                    cmd.Parameters.AddWithValue("$start", FormatIso(DateTime.Now));
                    return (long)cmd.ExecuteScalar()!;
                }
                catch
                {
                    return -1;
                }
            }
        }

        public void RecordStepEnd(long stepId, RunStatus status, int? rowCount, string? errorMessage)
        {
            if (stepId <= 0) return;
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
UPDATE run_steps
   SET ended_at     = $end,
       duration_ms  = CAST((julianday($end) - julianday(started_at)) * 86400000 AS INTEGER),
       row_count    = $rows,
       status       = $status,
       error_message = $err
 WHERE id = $id;";
                    cmd.Parameters.AddWithValue("$end",    FormatIso(DateTime.Now));
                    cmd.Parameters.AddWithValue("$rows",   (object?)rowCount ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$status", status.ToString());
                    cmd.Parameters.AddWithValue("$err",    (object?)errorMessage ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$id",     stepId);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        public void AppendLog(long runId, string level, string message)
        {
            if (runId <= 0) return;
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO run_logs (run_id, timestamp, level, message)
VALUES ($rid, $ts, $lvl, $msg);";
                    cmd.Parameters.AddWithValue("$rid", runId);
                    cmd.Parameters.AddWithValue("$ts",  FormatIso(DateTime.Now));
                    cmd.Parameters.AddWithValue("$lvl", level);
                    cmd.Parameters.AddWithValue("$msg", message ?? "");
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        // ==================== 読み出し API ====================

        public IList<RunRecord> Search(RunSearchFilter filter)
        {
            var list = new List<RunRecord>();
            var sb = new StringBuilder(@"
SELECT id, run_guid, pipeline_path, pipeline_name, job_path, job_name, trigger,
       started_at, ended_at, duration_ms, status, error_message, total_rows
  FROM runs
 WHERE 1=1");
            var parms = new List<(string Name, object Val)>();
            if (filter.From.HasValue)
            {
                sb.Append(" AND started_at >= $from");
                parms.Add(("$from", FormatIso(filter.From.Value)));
            }
            if (filter.To.HasValue)
            {
                sb.Append(" AND started_at <= $to");
                parms.Add(("$to", FormatIso(filter.To.Value)));
            }
            if (!string.IsNullOrEmpty(filter.PipelineNameLike))
            {
                sb.Append(" AND (pipeline_name LIKE $pl OR pipeline_path LIKE $pl)");
                parms.Add(("$pl", "%" + filter.PipelineNameLike + "%"));
            }
            if (!string.IsNullOrEmpty(filter.JobNameLike))
            {
                sb.Append(" AND (job_name LIKE $jl OR job_path LIKE $jl)");
                parms.Add(("$jl", "%" + filter.JobNameLike + "%"));
            }
            if (filter.Statuses != null && filter.Statuses.Count > 0)
            {
                var inList = new List<string>();
                int i = 0;
                foreach (var s in filter.Statuses)
                {
                    var p = "$s" + (i++);
                    inList.Add(p);
                    parms.Add((p, s.ToString()));
                }
                sb.Append(" AND status IN (" + string.Join(",", inList) + ")");
            }
            if (filter.Triggers != null && filter.Triggers.Count > 0)
            {
                var inList = new List<string>();
                int i = 0;
                foreach (var t in filter.Triggers)
                {
                    var p = "$t" + (i++);
                    inList.Add(p);
                    parms.Add((p, t));
                }
                sb.Append(" AND trigger IN (" + string.Join(",", inList) + ")");
            }
            sb.Append(" ORDER BY started_at DESC LIMIT $lim");
            parms.Add(("$lim", Math.Max(1, filter.Limit)));

            try
            {
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sb.ToString();
                foreach (var p in parms) cmd.Parameters.AddWithValue(p.Name, p.Val);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(ReadRun(r));
            }
            catch
            {
                // 検索失敗は空リストで返す (UI 表示が空になるが致命傷ではない)
            }
            return list;
        }

        public RunDetail? GetDetail(long runId)
        {
            try
            {
                using var conn = Open();
                RunRecord? header = null;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT id, run_guid, pipeline_path, pipeline_name, job_path, job_name, trigger,
       started_at, ended_at, duration_ms, status, error_message, total_rows
  FROM runs WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", runId);
                    using var r = cmd.ExecuteReader();
                    if (r.Read()) header = ReadRun(r);
                }
                if (header == null) return null;

                var detail = new RunDetail { Run = header };

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT id, run_id, step_order, step_name, step_type,
       started_at, ended_at, duration_ms, row_count, status, error_message
  FROM run_steps WHERE run_id = $id ORDER BY step_order, id";
                    cmd.Parameters.AddWithValue("$id", runId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) detail.Steps.Add(ReadStep(r));
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
SELECT id, run_id, timestamp, level, message
  FROM run_logs WHERE run_id = $id ORDER BY id";
                    cmd.Parameters.AddWithValue("$id", runId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) detail.Logs.Add(ReadLog(r));
                }

                return detail;
            }
            catch
            {
                return null;
            }
        }

        public void DeleteRun(long runId)
        {
            if (runId <= 0) return;
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM runs WHERE id = $id";
                    cmd.Parameters.AddWithValue("$id", runId);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        /// <summary>古い実行履歴を期間と件数で削除する。両方の条件 OR で削除対象とする。</summary>
        public int ApplyRetention(int? maxAgeDays, int? maxRows)
        {
            int deleted = 0;
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    if (maxAgeDays.HasValue && maxAgeDays.Value > 0)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "DELETE FROM runs WHERE started_at < $cutoff";
                        cmd.Parameters.AddWithValue("$cutoff",
                            FormatIso(DateTime.Now.AddDays(-maxAgeDays.Value)));
                        deleted += cmd.ExecuteNonQuery();
                    }
                    if (maxRows.HasValue && maxRows.Value > 0)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = @"
DELETE FROM runs
 WHERE id NOT IN (
   SELECT id FROM runs ORDER BY started_at DESC LIMIT $lim
 );";
                        cmd.Parameters.AddWithValue("$lim", maxRows.Value);
                        deleted += cmd.ExecuteNonQuery();
                    }
                }
                catch { }
            }
            return deleted;
        }

        public void Vacuum()
        {
            lock (_writeGate)
            {
                try
                {
                    using var conn = Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "VACUUM;";
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        // ==================== ヘルパ ====================

        private static string FormatIso(DateTime dt)
            => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        private static DateTime? ParseIso(object? v)
        {
            if (v == null || v == DBNull.Value) return null;
            var s = v.ToString();
            if (string.IsNullOrEmpty(s)) return null;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
                ? dt : (DateTime?)null;
        }

        private static RunStatus ParseStatus(object? v)
        {
            var s = v?.ToString();
            return Enum.TryParse<RunStatus>(s, ignoreCase: true, out var r) ? r : RunStatus.Failed;
        }

        private static RunRecord ReadRun(IDataReader r) => new RunRecord
        {
            Id = r.GetInt64(0),
            RunGuid = Guid.TryParse(r.GetString(1), out var g) ? g : Guid.Empty,
            PipelinePath = r.IsDBNull(2) ? null : r.GetString(2),
            PipelineName = r.IsDBNull(3) ? null : r.GetString(3),
            JobPath = r.IsDBNull(4) ? null : r.GetString(4),
            JobName = r.IsDBNull(5) ? null : r.GetString(5),
            Trigger = r.GetString(6),
            StartedAt = ParseIso(r.GetValue(7)) ?? DateTime.MinValue,
            EndedAt = ParseIso(r.GetValue(8)),
            DurationMs = r.IsDBNull(9) ? null : r.GetInt64(9),
            Status = ParseStatus(r.GetValue(10)),
            ErrorMessage = r.IsDBNull(11) ? null : r.GetString(11),
            TotalRows = r.IsDBNull(12) ? null : r.GetInt32(12)
        };

        private static RunStepRecord ReadStep(IDataReader r) => new RunStepRecord
        {
            Id = r.GetInt64(0),
            RunId = r.GetInt64(1),
            StepOrder = r.GetInt32(2),
            StepName = r.GetString(3),
            StepType = r.GetString(4),
            StartedAt = ParseIso(r.GetValue(5)) ?? DateTime.MinValue,
            EndedAt = ParseIso(r.GetValue(6)),
            DurationMs = r.IsDBNull(7) ? null : r.GetInt64(7),
            RowCount = r.IsDBNull(8) ? null : r.GetInt32(8),
            Status = ParseStatus(r.GetValue(9)),
            ErrorMessage = r.IsDBNull(10) ? null : r.GetString(10)
        };

        private static RunLogEntry ReadLog(IDataReader r) => new RunLogEntry
        {
            Id = r.GetInt64(0),
            RunId = r.GetInt64(1),
            Timestamp = ParseIso(r.GetValue(2)) ?? DateTime.MinValue,
            Level = r.GetString(3),
            Message = r.GetString(4)
        };
    }
}
