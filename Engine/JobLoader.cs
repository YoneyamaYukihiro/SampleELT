using System.IO;
using System.Text.Json;
using SampleELT.Models;

namespace SampleELT.Engine
{
    public static class JobLoader
    {
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        public static Job LoadFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var job = JsonSerializer.Deserialize<Job>(json, ReadOptions)
                      ?? throw new InvalidDataException("ジョブファイルの形式が正しくありません");
            job.FilePath = filePath;
            return job;
        }

        public static void SaveToFile(Job job, string filePath)
        {
            var json = JsonSerializer.Serialize(job, WriteOptions);
            File.WriteAllText(filePath, json);
            job.FilePath = filePath;
        }
    }
}
