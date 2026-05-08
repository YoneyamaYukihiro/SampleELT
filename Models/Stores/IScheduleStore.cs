using System;
using System.Collections.Generic;

namespace SampleELT.Models.Stores
{
    /// <summary>スケジュール設定の永続化／実行時刻計算インターフェイス。実体は <see cref="ScheduleRegistry"/>。</summary>
    public interface IScheduleStore
    {
        IReadOnlyList<ScheduleEntry> Schedules { get; }
        void Load();
        void Save();
        DateTime? CalcLastDueTime(ScheduleEntry entry, DateTime now);

        /// <summary>アプリ全体で共有される現在のストア (テストで差し替え可)。</summary>
        public static IScheduleStore Default { get; set; } = ScheduleRegistry.Instance;
    }
}
