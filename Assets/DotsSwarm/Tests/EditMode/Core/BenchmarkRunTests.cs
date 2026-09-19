using System;
using System.Globalization;
using System.IO;
using System.Threading;
using DotsSwarm.Gameplay;
using NUnit.Framework;

namespace DotsSwarm.Tests.Gameplay
{
    public sealed class BenchmarkRunTests
    {
        [Test]
        public void Options_WithoutBenchmarkFlagDoNothing()
        {
            var args = new[] { "dots-swarm.exe", "-screen-width", "1920", "-logFile", "player.log" };
            Assert.That(BenchmarkOptions.TryParse(args, out _, out var error), Is.False);
            Assert.That(error, Is.Null);
        }

        [Test]
        public void Options_ParsePresetTimesAndOutputAfterUnrelatedArguments()
        {
            var args = new[]
            {
                "dots-swarm.exe", "-screen-fullscreen", "1", "-BENCHMARK", "20k", "-benchmark-warmup", "2.5",
                "-benchmark-duration", "45", "-benchmark-output", "D:/runs/swarm.csv"
            };

            Assert.That(BenchmarkOptions.TryParse(args, out var options, out var error), Is.True, error);
            Assert.That(options.Preset, Is.EqualTo(BenchmarkPreset.Swarm20K));
            Assert.That(options.WarmupSeconds, Is.EqualTo(2.5f));
            Assert.That(options.DurationSeconds, Is.EqualTo(45f));
            Assert.That(options.OutputPath, Is.EqualTo("D:/runs/swarm.csv"));

            Assert.That(BenchmarkOptions.TryParse(new[] { "-benchmark", "50000" }, out options, out error), Is.True);
            Assert.That(options.Preset, Is.EqualTo(BenchmarkPreset.Swarm50K));
            Assert.That(options.WarmupSeconds, Is.EqualTo(BenchmarkOptions.DefaultWarmupSeconds));
            Assert.That(options.DurationSeconds, Is.EqualTo(BenchmarkOptions.DefaultDurationSeconds));
            Assert.That(options.OutputPath, Is.Empty);
        }

        [TestCase("1k", BenchmarkPreset.Swarm1K)]
        [TestCase("10K", BenchmarkPreset.Swarm10K)]
        [TestCase("20000", BenchmarkPreset.Swarm20K)]
        [TestCase(" 50k ", BenchmarkPreset.Swarm50K)]
        public void Options_AcceptStressPresets(string value, BenchmarkPreset expected)
        {
            Assert.That(BenchmarkOptions.TryParsePreset(value, out var preset), Is.True);
            Assert.That(preset, Is.EqualTo(expected));
        }

        [TestCase("0")]
        [TestCase("5k")]
        [TestCase("-1k")]
        [TestCase("20.5k")]
        [TestCase("k")]
        [TestCase("9999999999k")]
        public void Options_RejectUnknownPresets(string value)
        {
            Assert.That(BenchmarkOptions.TryParsePreset(value, out _), Is.False);
        }

        private static readonly object[] InvalidRequests =
        {
            new object[] { new[] { "-benchmark" } },
            new object[] { new[] { "-benchmark", "survival" } },
            new object[] { new[] { "-benchmark", "20k", "-benchmark-duration", "0" } },
            new object[] { new[] { "-benchmark", "20k", "-benchmark-duration", "601" } },
            new object[] { new[] { "-benchmark", "20k", "-benchmark-warmup", "-1" } },
            new object[] { new[] { "-benchmark", "20k", "-benchmark-warmup", "1,5" } },
            new object[] { new[] { "-benchmark", "20k", "-benchmark-output" } },
            new object[] { new[] { "-benchmark", "20k", "-benchmark-frames", "10" } },
            new object[] { new[] { "-benchmark-duration", "10" } }
        };

        [TestCaseSource(nameof(InvalidRequests))]
        public void Options_ReportInvalidRequests(string[] args)
        {
            Assert.That(BenchmarkOptions.TryParse(args, out _, out var error), Is.False);
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Result_FormatsInvariantCsvRowsAndFlagsTarget()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            try
            {
                var result = CreateResult(16.6f);
                Assert.That(result.MeetsTarget, Is.True);
                Assert.That(result.ToCsvRow(), Is.EqualTo(
                    "2026-09-20T12:30:05Z,20000,20,30,1800,12.25,11.5,16.6,19.125,40,true,19998,20000,1920x1080,0,"
                    + "release,Direct3D12,\"GPU \"\"Ultra\"\", 8 GB\",CPU,6000.4.5f1"));
                Assert.That(result.ToCsvRow().Split(',').Length, Is.EqualTo(BenchmarkResult.CsvHeader.Split(',').Length + 1),
                    "Only the quoted GPU name contains an extra comma.");

                Assert.That(CreateResult(16.67f).MeetsTarget, Is.False, "p95 must fit the 60 FPS budget of 16.67 ms.");
                Assert.That(default(BenchmarkResult).MeetsTarget, Is.False, "An empty run meets nothing.");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void Result_AppendsHeaderOnceThenRows()
        {
            var directory = Path.Combine(Path.GetTempPath(), "dots-swarm-benchmark-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "nested", "results.csv");
            try
            {
                var first = CreateResult(10f);
                var second = CreateResult(20f);
                first.AppendTo(path);
                second.AppendTo(path);

                var lines = File.ReadAllLines(path);
                Assert.That(lines, Is.EqualTo(new[] { BenchmarkResult.CsvHeader, first.ToCsvRow(), second.ToCsvRow() }));
                StringAssert.Contains(",false,", lines[2]);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        private static BenchmarkResult CreateResult(float p95) => new BenchmarkResult
        {
            TimestampUtc = new DateTime(2026, 9, 20, 12, 30, 5, DateTimeKind.Utc),
            Preset = BenchmarkPreset.Swarm20K,
            WarmupSeconds = 20f,
            DurationSeconds = 30f,
            Frames = new FrameTimeSummary
            {
                Frames = 1800,
                AverageMilliseconds = 12.25f,
                P50Milliseconds = 11.5f,
                P95Milliseconds = p95,
                P99Milliseconds = 19.125f,
                MaxMilliseconds = 40f
            },
            MinEnemies = 19998,
            MaxEnemies = 20000,
            Width = 1920,
            Height = 1080,
            VSyncCount = 0,
            Build = "release",
            GraphicsApi = "Direct3D12",
            Gpu = "GPU \"Ultra\", 8 GB",
            Cpu = "CPU",
            UnityVersion = "6000.4.5f1"
        };
    }
}
