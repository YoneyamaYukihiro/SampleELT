using System;
using System.Collections.Generic;
using System.IO;
using SampleELT.Engine;
using SampleELT.Models;
using Xunit;

namespace SampleELT.Tests.Engine
{
    public class JobLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public JobLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "JobLoaderTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private string TempFile(string name) => Path.Combine(_tempDir, name);

        [Fact]
        public void SaveAndLoad_BasicJob_RoundTrips()
        {
            var job = new Job { Name = "Test Job" };
            var path = TempFile("job1.json");

            JobLoader.SaveToFile(job, path);
            var loaded = JobLoader.LoadFromFile(path);

            Assert.Equal("Test Job", loaded.Name);
            Assert.Equal(job.Id, loaded.Id);
        }

        [Fact]
        public void SaveToFile_SetsFilePath()
        {
            var job = new Job { Name = "Job A" };
            var path = TempFile("jobA.json");

            JobLoader.SaveToFile(job, path);

            Assert.Equal(path, job.FilePath);
        }

        [Fact]
        public void LoadFromFile_SetsFilePath()
        {
            var job = new Job { Name = "Job B" };
            var path = TempFile("jobB.json");
            JobLoader.SaveToFile(job, path);

            var loaded = JobLoader.LoadFromFile(path);

            Assert.Equal(path, loaded.FilePath);
        }

        [Fact]
        public void SaveAndLoad_JobWithSteps_RoundTrips()
        {
            var job = new Job
            {
                Name = "Multi-step Job",
                Steps = new List<JobStep>
                {
                    new() { Order = 1, Name = "Step One", PipelineFilePath = @"C:\pipelines\p1.json" },
                    new() { Order = 2, Name = "Step Two", PipelineFilePath = @"C:\pipelines\p2.json", ContinueOnError = true },
                }
            };
            var path = TempFile("job_steps.json");

            JobLoader.SaveToFile(job, path);
            var loaded = JobLoader.LoadFromFile(path);

            Assert.Equal(2, loaded.Steps.Count);
            Assert.Equal("Step One", loaded.Steps[0].Name);
            Assert.Equal(1, loaded.Steps[0].Order);
            Assert.Equal("Step Two", loaded.Steps[1].Name);
            Assert.True(loaded.Steps[1].ContinueOnError);
        }

        [Fact]
        public void SaveAndLoad_IsEnabled_RoundTrips()
        {
            var job = new Job { Name = "Disabled Job", IsEnabled = false };
            var path = TempFile("job_disabled.json");

            JobLoader.SaveToFile(job, path);
            var loaded = JobLoader.LoadFromFile(path);

            Assert.False(loaded.IsEnabled);
        }

        [Fact]
        public void FilePathNotSerialized_NotInJson()
        {
            var job = new Job { Name = "FilePath Test" };
            var path = TempFile("job_filepath.json");
            job.FilePath = "should_not_appear";

            JobLoader.SaveToFile(job, path);
            var json = File.ReadAllText(path);

            Assert.DoesNotContain("should_not_appear", json);
        }

        [Fact]
        public void LoadFromFile_InvalidFile_ThrowsException()
        {
            var path = TempFile("invalid.json");
            File.WriteAllText(path, "not valid json {{{");

            Assert.Throws<System.Text.Json.JsonException>(() => JobLoader.LoadFromFile(path));
        }

        [Fact]
        public void LoadFromFile_MissingFile_ThrowsException()
        {
            var path = TempFile("nonexistent.json");
            Assert.Throws<FileNotFoundException>(() => JobLoader.LoadFromFile(path));
        }

        [Fact]
        public void SaveToFile_CreatesFile()
        {
            var job = new Job { Name = "Create Test" };
            var path = TempFile("created.json");

            Assert.False(File.Exists(path));
            JobLoader.SaveToFile(job, path);
            Assert.True(File.Exists(path));
        }
    }
}
