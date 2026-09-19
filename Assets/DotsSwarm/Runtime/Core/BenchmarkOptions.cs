using System;
using System.Globalization;

namespace DotsSwarm.Gameplay
{
    // Command line of an unattended benchmark run of the standalone player:
    //   dots-swarm.exe -benchmark 20k [-benchmark-warmup 20] [-benchmark-duration 30]
    //                  [-benchmark-output results.csv]
    public struct BenchmarkOptions
    {
        public const string PresetFlag = "-benchmark";
        public const string WarmupFlag = "-benchmark-warmup";
        public const string DurationFlag = "-benchmark-duration";
        public const string OutputFlag = "-benchmark-output";
        // The stress swarm spawns on a 40 m ring and needs ~18 s to close in on the player.
        public const float DefaultWarmupSeconds = 20f;
        public const float DefaultDurationSeconds = 30f;
        public const float MaximumSeconds = 600f;

        public BenchmarkPreset Preset;
        public float WarmupSeconds;
        public float DurationSeconds;
        // Empty: benchmark-results.csv in Application.persistentDataPath.
        public string OutputPath;

        public static BenchmarkOptions Create(BenchmarkPreset preset) => new BenchmarkOptions
        {
            Preset = preset,
            WarmupSeconds = DefaultWarmupSeconds,
            DurationSeconds = DefaultDurationSeconds,
            OutputPath = string.Empty
        };

        // True for a valid request; false with a null error when no benchmark flag is present.
        public static bool TryParse(string[] args, out BenchmarkOptions options, out string error)
        {
            options = Create(BenchmarkPreset.Survival);
            error = null;
            var requested = false;
            var configured = false;
            for (var index = 0; index < args.Length && error == null; index++)
            {
                var flag = args[index];
                if (!flag.StartsWith(PresetFlag, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = index + 1 < args.Length ? args[++index] : null;
                var isPreset = Is(flag, PresetFlag);
                requested |= isPreset;
                configured |= !isPreset;
                if (value == null)
                    error = $"{flag} needs a value.";
                else if (isPreset)
                {
                    if (!TryParsePreset(value, out options.Preset))
                        error = $"{flag} {value}: expected 1k, 10k, 20k or 50k.";
                }
                else if (Is(flag, WarmupFlag))
                {
                    if (!TryParseSeconds(value, out options.WarmupSeconds))
                        error = $"{flag} {value}: expected 0 to {MaximumSeconds} seconds.";
                }
                else if (Is(flag, DurationFlag))
                {
                    if (!TryParseSeconds(value, out options.DurationSeconds) || options.DurationSeconds <= 0f)
                        error = $"{flag} {value}: expected more than 0 and up to {MaximumSeconds} seconds.";
                }
                else if (Is(flag, OutputFlag))
                    options.OutputPath = value;
                else
                    error = $"Unknown option {flag}.";
            }

            if (error == null && configured && !requested)
                error = $"Benchmark options need {PresetFlag} <preset>.";
            return requested && error == null;
        }

        public static bool TryParsePreset(string value, out BenchmarkPreset preset)
        {
            preset = BenchmarkPreset.Survival;
            value = value.Trim();
            var multiplier = 1;
            if (value.EndsWith("k", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1000;
                value = value.Substring(0, value.Length - 1);
            }
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count > int.MaxValue / multiplier)
                return false;

            preset = (BenchmarkPreset)(count * multiplier);
            return preset != BenchmarkPreset.Survival && BenchmarkState.IsValid(preset);
        }

        private static bool TryParseSeconds(string value, out float seconds)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                   && seconds >= 0f && seconds <= MaximumSeconds;
        }

        private static bool Is(string flag, string name) =>
            string.Equals(flag, name, StringComparison.OrdinalIgnoreCase);
    }
}
