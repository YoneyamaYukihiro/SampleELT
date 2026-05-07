using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SampleELT.Engine;
using SampleELT.Models;
using SampleELT.Tools;

namespace SampleELT
{
    public partial class App : Application
    {
        // 親プロセスのコンソールにアタッチ（コマンドプロンプトから起動時に出力できるようにする）
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ConnectionRegistry.Instance.Load();

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
            JobRegistry.Instance.Load();
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private async Task RunHeadlessAsync(string pipelineFile)
        {
            int exitCode = 0;
            var logs = new List<string>();

            void Log(string msg)
            {
                var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                logs.Add(line);
                Console.WriteLine(line);
            }

            string? logFile = null;
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
                var progress = new Progress<string>(Log);
                var engine = new ExecutionEngine();

                await engine.ExecuteAsync(pipeline, progress, CancellationToken.None);
                Log("===== 実行完了 =====");
            }
            catch (Exception ex)
            {
                Log($"===== エラー: {ex.Message} =====");
                exitCode = 1;
            }
            finally
            {
                if (logFile != null)
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
