using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using SampleELT.Engine;
using SampleELT.Models;
using SampleELT.Models.Stores;

namespace SampleELT.Services
{
    /// <summary>
    /// アプリ内で 1 分ごとにスケジュールエントリを評価し、期限が来たジョブ／パイプラインを実行する。
    /// MainViewModel から責務を分離し、ログ出力は <see cref="IProgress{T}"/> 経由で受ける。
    /// </summary>
    public class PipelineSchedulerService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly IProgress<string> _logger;
        private bool _disposed;

        /// <summary>スケジュール実行が完了するなどして UI 表示を更新すべき時に発火。</summary>
        public event Action? StatusChanged;

        public PipelineSchedulerService(IProgress<string> logger)
        {
            _logger = logger;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop()  => _timer.Stop();

        public void Dispose()
        {
            if (_disposed) return;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _disposed = true;
        }

        private async void OnTick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            var store = IScheduleStore.Default;
            bool anyChanged = false;

            foreach (var entry in store.Schedules.Where(s => s.IsEnabled && s.Mode == ScheduleMode.InApp))
            {
                var lastDue = store.CalcLastDueTime(entry, now);
                if (lastDue == null) continue;

                // 初回観測 (一度も実行していない) は過去の予定時刻を catch-up しない:
                // LastRunTime=now を記録して、次の予定時刻まで待たせる。
                if (!entry.LastRunTime.HasValue)
                {
                    entry.LastRunTime = now;
                    anyChanged = true;
                    continue;
                }

                if (entry.LastRunTime.Value >= lastDue.Value) continue;

                await RunEntryAsync(entry);
                anyChanged = true;
            }

            if (anyChanged) store.Save();
        }

        private async Task RunEntryAsync(ScheduleEntry entry)
        {
            _logger.Report($"===== スケジュール実行開始: {entry.Name} =====");
            entry.LastRunTime = DateTime.Now;

            var cts = new CancellationTokenSource();
            try
            {
                if (entry.Target == ScheduleTarget.Job)
                {
                    if (string.IsNullOrWhiteSpace(entry.JobFilePath))
                        throw new InvalidOperationException("ジョブファイルが指定されていません");
                    if (!File.Exists(entry.JobFilePath))
                        throw new FileNotFoundException($"ジョブファイルが見つかりません: {entry.JobFilePath}");

                    var job = JobLoader.LoadFromFile(entry.JobFilePath);
                    var executor = new JobExecutor();
                    await executor.ExecuteAsync(job, _logger, cts.Token);
                }
                else
                {
                    if (!File.Exists(entry.PipelineFilePath))
                        throw new FileNotFoundException($"パイプラインファイルが見つかりません: {entry.PipelineFilePath}");

                    var pipeline = PipelineLoader.LoadFromFile(entry.PipelineFilePath);
                    var engine = new ExecutionEngine();
                    await engine.ExecuteAsync(pipeline, _logger, cts.Token);
                }

                entry.LastRunSuccess = true;
                entry.LastRunMessage = "実行完了";
                _logger.Report($"===== スケジュール実行完了: {entry.Name} =====");
            }
            catch (Exception ex)
            {
                entry.LastRunSuccess = false;
                entry.LastRunMessage = ex.Message;
                _logger.Report($"===== スケジュール実行エラー [{entry.Name}]: {ex.Message} =====");
            }
            finally
            {
                StatusChanged?.Invoke();
            }
        }
    }
}
