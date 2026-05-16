using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BreezeFlow.Engine;
using BreezeFlow.Models;
using BreezeFlow.Services;
using BreezeFlow.Tools;

namespace BreezeFlow
{
    public partial class App : Application
    {
        /// <summary>csproj の &lt;Version&gt; から取得したアプリのバージョン (Major.Minor.Build 形式)。</summary>
        public static string AppVersion { get; } =
            System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "?";

        // 親プロセスのコンソールにアタッチ（コマンドプロンプトから起動時に出力できるようにする）
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        /// <summary>
        /// 実行履歴 (runs.db) の保持ポリシー。
        /// 起動時に古いレコードを削除する (UI / CLI / スケジュール いずれの起動経路でも適用)。
        /// </summary>
        private const int RunHistoryRetentionDays = 30;
        private const int RunHistoryRetentionMaxRows = 5000;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ConnectionRegistry.Instance.Load();

            // 実行履歴ストア (SQLite) を初期化。失敗しても起動は継続する。
            try
            {
                var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runs.db");
                RunHistoryStore.InitializeDefault(dbPath);
                RunHistoryStore.Instance?.ApplyRetention(RunHistoryRetentionDays, RunHistoryRetentionMaxRows);
            }
            catch { /* 履歴記録失敗はパイプライン実行を妨げない */ }

            // CLI ヘッドレスモード: --run <pipeline.json>
            var args = e.Args;
            int runIdx = Array.IndexOf(args, "--run");
            if (runIdx >= 0 && runIdx + 1 < args.Length)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AttachConsole(-1); // 親コンソールにアタッチ（なければ無視）
                _ = RunHeadlessAsync(args[runIdx + 1]);
                return;
            }

            // CLI ヘッドレスモード: --run-job <job.json> (タスクスケジューラ用)
            int runJobIdx = Array.IndexOf(args, "--run-job");
            if (runJobIdx >= 0 && runJobIdx + 1 < args.Length)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AttachConsole(-1);
                _ = RunHeadlessJobAsync(args[runJobIdx + 1]);
                return;
            }

            // CLI 変換モード: --convert-ktr <input.ktr> [<output.json>]
            int convIdx = Array.IndexOf(args, "--convert-ktr");
            if (convIdx >= 0 && convIdx + 1 < args.Length)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                AttachConsole(-1);
                var input = args[convIdx + 1];
                var output = convIdx + 2 < args.Length && !args[convIdx + 2].StartsWith("--")
                    ? args[convIdx + 2]
                    : null;
                int code = ConvertKtrCli(input, output);
                Shutdown(code);
                return;
            }

            // 通常 UI モード
            ScheduleRegistry.Instance.Load();
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private async Task RunHeadlessAsync(string pipelineFile)
        {
            int exitCode = 0;
            var logs = new List<string>();
            var logMode = LogMode.OnError;

            void Log(string msg)
            {
                var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                logs.Add(line);
                Console.WriteLine(line);
            }

            string? logFile = null;
            long runId = -1;
            RunHistoryProgressWriter? historyWriter = null;
            RunStatus finalStatus = RunStatus.Failed;
            string? finalError = null;

            try
            {
                pipelineFile = Path.GetFullPath(pipelineFile);
                logFile = Path.Combine(
                    Path.GetDirectoryName(pipelineFile) ?? ".",
                    Path.GetFileNameWithoutExtension(pipelineFile)
                        + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");

                Log("===== パイプライン実行開始 =====");
                Log($"ファイル: {pipelineFile}");

                if (!File.Exists(pipelineFile))
                    throw new FileNotFoundException($"パイプラインファイルが見つかりません: {pipelineFile}");

                var pipeline = PipelineLoader.LoadFromFile(pipelineFile);
                logMode = pipeline.LogMode;

                var rawProgress = new Progress<string>(Log);
                IProgress<string> progress = rawProgress;

                if (RunHistoryStore.Instance != null)
                {
                    runId = RunHistoryStore.Instance.BeginRun(new RunRecord
                    {
                        PipelinePath = pipelineFile,
                        PipelineName = pipeline.Name,
                        Trigger = "cli",
                        StartedAt = DateTime.Now
                    });
                    historyWriter = new RunHistoryProgressWriter(RunHistoryStore.Instance, runId, rawProgress);
                    progress = historyWriter;
                }

                var engine = new ExecutionEngine();
                await engine.ExecuteAsync(pipeline, progress, CancellationToken.None);
                finalStatus = RunStatus.Success;
                Log("===== 実行完了 =====");
            }
            catch (Exception ex)
            {
                Log($"===== エラー: {ex.Message} =====");
                exitCode = 1;
                finalStatus = RunStatus.Failed;
                finalError = ex.Message;
            }
            finally
            {
                historyWriter?.FinishUnclosedSteps(finalStatus, finalError);
                if (runId > 0 && RunHistoryStore.Instance != null)
                    RunHistoryStore.Instance.EndRun(runId, finalStatus, finalError,
                        historyWriter?.LastReportedRowCount);

                if (ShouldWriteLog(logMode, exitCode != 0) && logFile != null)
                {
                    try { File.WriteAllLines(logFile, logs); }
                    catch { }
                }
            }

            Shutdown(exitCode);
        }

        /// <summary>LogMode と実行結果からログファイルを書くべきかを判定する。</summary>
        private static bool ShouldWriteLog(LogMode mode, bool errored) => mode switch
        {
            LogMode.Always  => true,
            LogMode.OnError => errored,
            LogMode.Never   => false,
            _               => errored
        };

        /// <summary>
        /// CLI からジョブを実行する (タスクスケジューラから呼び出される想定)。
        /// Job ファイルを読み込み、含まれる全パイプラインを順次実行する。
        /// 結果はジョブファイルと同じディレクトリに <c>{name}_{timestamp}.log</c> として保存される。
        /// </summary>
        private async Task RunHeadlessJobAsync(string jobFile)
        {
            int exitCode = 0;
            var logs = new List<string>();
            var logMode = LogMode.OnError;

            void Log(string msg)
            {
                var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                logs.Add(line);
                Console.WriteLine(line);
            }

            string? logFile = null;
            try
            {
                jobFile = Path.GetFullPath(jobFile);
                logFile = Path.Combine(
                    Path.GetDirectoryName(jobFile) ?? ".",
                    Path.GetFileNameWithoutExtension(jobFile)
                        + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");

                Log("===== ジョブ実行開始 =====");
                Log($"ファイル: {jobFile}");

                if (!File.Exists(jobFile))
                    throw new FileNotFoundException($"ジョブファイルが見つかりません: {jobFile}");

                var job = JobLoader.LoadFromFile(jobFile);
                logMode = job.LogMode;
                var progress = new Progress<string>(Log);
                var executor = new JobExecutor();

                await executor.ExecuteAsync(job, progress, CancellationToken.None, trigger: "cli");
                Log("===== ジョブ実行完了 =====");
            }
            catch (Exception ex)
            {
                Log($"===== エラー: {ex.Message} =====");
                exitCode = 1;
            }
            finally
            {
                if (ShouldWriteLog(logMode, exitCode != 0) && logFile != null)
                {
                    try { File.WriteAllLines(logFile, logs); }
                    catch { }
                }
            }

            Shutdown(exitCode);
        }

        private static int ConvertKtrCli(string inputPath, string? outputPath)
        {
            try
            {
                inputPath = Path.GetFullPath(inputPath);
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine($"KTR ファイルが見つかりません: {inputPath}");
                    return 1;
                }

                var result = KtrToJsonConverter.Convert(inputPath);

                if (result.NewConnections.Count > 0)
                {
                    foreach (var c in result.NewConnections)
                        ConnectionRegistry.Instance.Connections.Add(c);
                    ConnectionRegistry.Instance.Save();
                }

                outputPath ??= Path.Combine(
                    Path.GetDirectoryName(inputPath) ?? ".",
                    Path.GetFileNameWithoutExtension(inputPath) + ".json");
                File.WriteAllText(outputPath, result.PipelineJson);

                Console.WriteLine($"変換完了: {outputPath}");
                Console.WriteLine($"  パイプライン名: {result.PipelineName}");
                if (result.MatchedConnections.Count > 0)
                {
                    Console.WriteLine("  既存接続を流用:");
                    foreach (var c in result.MatchedConnections)
                        Console.WriteLine($"    - {c.Name} ({c.DbType})");
                }
                if (result.NewConnections.Count > 0)
                {
                    Console.WriteLine("  新規接続を Connection Manager に登録 (パスワード未設定):");
                    foreach (var c in result.NewConnections)
                        Console.WriteLine($"    - {c.Name} ({c.DbType})");
                }
                if (result.Warnings.Count > 0)
                {
                    Console.WriteLine("  警告:");
                    foreach (var w in result.Warnings)
                        Console.WriteLine($"    - {w}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"変換失敗: {ex.Message}");
                return 1;
            }
        }
    }
}
