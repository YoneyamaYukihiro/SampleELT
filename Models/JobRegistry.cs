using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SampleELT.Models
{
    public class JobRegistry
    {
        private static readonly Lazy<JobRegistry> _lazy = new(() => new JobRegistry());
        public static JobRegistry Instance => _lazy.Value;

        private static readonly string FilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "jobs.json");

        public List<Job> Jobs { get; private set; } = new();

        private JobRegistry() { }

        public void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                var json = File.ReadAllText(FilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                Jobs = JsonSerializer.Deserialize<List<Job>>(json, options) ?? new();
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Jobs, options));
            }
            catch { }
        }
    }
}
