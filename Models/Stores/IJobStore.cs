using System.Collections.Generic;

namespace SampleELT.Models.Stores
{
    /// <summary>ジョブ定義の永続化インターフェイス。実体は <see cref="JobRegistry"/>。</summary>
    public interface IJobStore
    {
        IReadOnlyList<Job> Jobs { get; }
        void Load();
        void Save();

        /// <summary>アプリ全体で共有される現在のストア (テストで差し替え可)。</summary>
        public static IJobStore Default { get; set; } = JobRegistry.Instance;
    }
}
