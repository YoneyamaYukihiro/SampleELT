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
    }
}
