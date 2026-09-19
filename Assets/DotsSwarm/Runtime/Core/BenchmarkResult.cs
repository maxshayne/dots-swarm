using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DotsSwarm.Gameplay
{
    // One benchmark run: frame-time percentiles plus the population and machine
    // they were measured on. Rows append to a CSV so repeated runs accumulate.
    public struct BenchmarkResult
    {
        // 60 FPS budget of the MVP performance target.
        public const float TargetMilliseconds = 1000f / 60f;
        public const string CsvHeader = "timestamp_utc,preset,warmup_s,duration_s,frames,avg_ms,p50_ms,p95_ms,"
                                        + "p99_ms,max_ms,p95_target_met,enemies_min,enemies_max,resolution,vsync,"
                                        + "build,graphics_api,gpu,cpu,unity";

        public DateTime TimestampUtc;
        public BenchmarkPreset Preset;
        public float WarmupSeconds;
        public float DurationSeconds;
        public FrameTimeSummary Frames;
        public int MinEnemies;
        public int MaxEnemies;
        public int Width;
        public int Height;
        public int VSyncCount;
        // "release", "development" or "editor": only release rows count toward the target.
        public string Build;
        public string GraphicsApi;
        public string Gpu;
        public string Cpu;
        public string UnityVersion;

        public bool MeetsTarget => Frames.Frames > 0 && Frames.P95Milliseconds <= TargetMilliseconds;

        public string ToCsvRow()
        {
            var row = new StringBuilder(256);
            row.Append(TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(',');
            row.Append((int)Preset).Append(',');
            Number(row, WarmupSeconds);
            Number(row, DurationSeconds);
            row.Append(Frames.Frames).Append(',');
            Number(row, Frames.AverageMilliseconds);
            Number(row, Frames.P50Milliseconds);
            Number(row, Frames.P95Milliseconds);
            Number(row, Frames.P99Milliseconds);
            Number(row, Frames.MaxMilliseconds);
            row.Append(MeetsTarget ? "true" : "false").Append(',');
            row.Append(MinEnemies).Append(',');
            row.Append(MaxEnemies).Append(',');
            row.Append(Width).Append('x').Append(Height).Append(',');
            row.Append(VSyncCount).Append(',');
            Text(row, Build).Append(',');
            Text(row, GraphicsApi).Append(',');
            Text(row, Gpu).Append(',');
            Text(row, Cpu).Append(',');
            Text(row, UnityVersion);
            return row.ToString();
        }

        public void AppendTo(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            using var writer = new StreamWriter(path, true, new UTF8Encoding(false));
            if (isNew) writer.WriteLine(CsvHeader);
            writer.WriteLine(ToCsvRow());
        }

        private static readonly char[] CsvSpecial = { ',', '"', '\n', '\r' };

        private static void Number(StringBuilder row, float value)
        {
            row.Append(value.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
        }

        // Device names may contain commas or quotes.
        private static StringBuilder Text(StringBuilder row, string value)
        {
            value ??= string.Empty;
            if (value.IndexOfAny(CsvSpecial) < 0)
                return row.Append(value);
            return row.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
        }
    }
}
