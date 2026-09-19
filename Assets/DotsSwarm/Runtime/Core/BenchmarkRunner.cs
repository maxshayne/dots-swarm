using System;
using System.IO;
using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Gameplay
{
    // Unattended benchmark of the running game: selects a stress preset, waits for
    // the swarm to fill and warm up, records every real frame with VSync and the
    // frame cap off, appends a CSV row and quits the player. Launched from the
    // command line (see BenchmarkOptions); in the editor it only records.
    [DisallowMultipleComponent]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        public const string DefaultOutputFile = "benchmark-results.csv";
        // Real seconds for the default world to stream in a playable session.
        private const double StartupTimeoutSeconds = 120.0;
        // Frames above this rate still count; percentiles then cover the latest frames.
        private const int MaximumSampledFramesPerSecond = 1000;

        private enum Phase
        {
            WaitingForSession,
            Filling,
            WarmingUp,
            Recording,
            Finished
        }

        private BenchmarkOptions options;
        private FrameTimeHistory frames;
        private Phase phase;
        private double startupDeadline;
        private double phaseSeconds;
        private int minEnemies;
        private int maxEnemies;
        private int restoreVSyncCount;
        private int restoreTargetFrameRate;
        private World world;
        private EntityQuery sessions;
        private EntityQuery enemies;

        public bool IsFinished => phase == Phase.Finished;
        // 0: recorded and written; 1: invalid arguments; 2: no session; 3: write failed.
        public int ExitCode { get; private set; }
        public string OutputPath { get; private set; }
        public BenchmarkResult Result { get; private set; }

        public static BenchmarkRunner Launch(BenchmarkOptions options)
        {
            var host = new GameObject("Benchmark Runner");
            DontDestroyOnLoad(host);
            var runner = host.AddComponent<BenchmarkRunner>();
            runner.Begin(options);
            return runner;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LaunchFromCommandLine()
        {
            if (BenchmarkOptions.TryParse(Environment.GetCommandLineArgs(), out var options, out var error))
            {
                Launch(options);
            }
            else if (error != null)
            {
                Debug.LogError($"Benchmark: {error}");
                if (!Application.isEditor) Application.Quit(1);
            }
        }

        private void Begin(BenchmarkOptions requested)
        {
            options = requested;
            OutputPath = Path.GetFullPath(string.IsNullOrEmpty(options.OutputPath)
                ? Path.Combine(Application.persistentDataPath, DefaultOutputFile)
                : options.OutputPath);
            var capacity = (int)Math.Ceiling(options.DurationSeconds * MaximumSampledFramesPerSecond);
            frames = new FrameTimeHistory(Math.Max(1, capacity));
            startupDeadline = Time.realtimeSinceStartupAsDouble + StartupTimeoutSeconds;
            restoreVSyncCount = QualitySettings.vSyncCount;
            restoreTargetFrameRate = Application.targetFrameRate;
            // Measure the frame cost itself, not the display refresh interval.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Debug.Log($"Benchmark: preset {(int)options.Preset}, warmup {options.WarmupSeconds} s, "
                      + $"duration {options.DurationSeconds} s, output {OutputPath}");
        }

        private void Update()
        {
            // Only Launch configures a run.
            if (frames == null) return;
            var deltaTime = Time.unscaledDeltaTime;
            switch (phase)
            {
                case Phase.WaitingForSession:
                    if (TryGetSession(out var session) && session.Initialized)
                    {
                        RequestPreset();
                        phase = Phase.Filling;
                    }
                    else if (Time.realtimeSinceStartupAsDouble > startupDeadline)
                    {
                        Finish(2, "no playable session loaded");
                    }
                    break;

                case Phase.Filling:
                    if (!TryGetSession(out _))
                    {
                        Finish(2, "the session was unloaded");
                        break;
                    }
                    // The request is consumed by the reset update; warmup starts once the
                    // next update has filled the preset, so neither frame is measured.
                    var benchmark = sessions.GetSingleton<BenchmarkState>();
                    if (!benchmark.ChangeRequested && benchmark.ActivePreset == options.Preset
                        && enemies.CalculateEntityCount() >= (int)options.Preset)
                    {
                        phase = Phase.WarmingUp;
                        phaseSeconds = 0d;
                    }
                    break;

                case Phase.WarmingUp:
                    phaseSeconds += deltaTime;
                    if (phaseSeconds >= options.WarmupSeconds)
                    {
                        phase = Phase.Recording;
                        phaseSeconds = 0d;
                        frames.Clear();
                        minEnemies = int.MaxValue;
                        maxEnemies = 0;
                    }
                    break;

                case Phase.Recording:
                    if (!TryGetSession(out _))
                    {
                        Finish(2, "the session was unloaded");
                        break;
                    }
                    // unscaledDeltaTime of this update is the previous full frame.
                    frames.Add(deltaTime);
                    phaseSeconds += deltaTime;
                    var count = enemies.CalculateEntityCount();
                    minEnemies = Math.Min(minEnemies, count);
                    maxEnemies = Math.Max(maxEnemies, count);
                    if (phaseSeconds >= options.DurationSeconds) Complete();
                    break;
            }
        }

        private void Complete()
        {
            Result = new BenchmarkResult
            {
                TimestampUtc = DateTime.UtcNow,
                Preset = options.Preset,
                WarmupSeconds = options.WarmupSeconds,
                DurationSeconds = options.DurationSeconds,
                Frames = frames.Summarize(),
                MinEnemies = minEnemies,
                MaxEnemies = maxEnemies,
                Width = Screen.width,
                Height = Screen.height,
                VSyncCount = QualitySettings.vSyncCount,
                Build = Application.isEditor ? "editor" : Debug.isDebugBuild ? "development" : "release",
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                Gpu = SystemInfo.graphicsDeviceName,
                Cpu = SystemInfo.processorType,
                UnityVersion = Application.unityVersion
            };

            try
            {
                Result.AppendTo(OutputPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(3, $"could not write {OutputPath}");
                return;
            }

            var summary = Result.Frames;
            Finish(0, $"{summary.Frames} frames, avg {summary.AverageMilliseconds:0.00} ms, "
                      + $"p95 {summary.P95Milliseconds:0.00} ms, p99 {summary.P99Milliseconds:0.00} ms, "
                      + $"max {summary.MaxMilliseconds:0.00} ms, enemies {minEnemies}-{maxEnemies}, "
                      + $"p95 target {(Result.MeetsTarget ? "met" : "missed")}");
        }

        private void Finish(int exitCode, string message)
        {
            ExitCode = exitCode;
            phase = Phase.Finished;
            if (exitCode == 0) Debug.Log($"Benchmark: {message}");
            else Debug.LogError($"Benchmark: {message}");
            if (!Application.isEditor) Application.Quit(exitCode);
        }

        private void RequestPreset()
        {
            var benchmark = sessions.GetSingleton<BenchmarkState>();
            benchmark.RequestedPreset = options.Preset;
            benchmark.ChangeRequested = true;
            sessions.SetSingleton(benchmark);
        }

        private bool TryGetSession(out GameSession session)
        {
            session = default;
            var current = World.DefaultGameObjectInjectionWorld;
            if (current == null || !current.IsCreated) return false;
            if (world != current)
            {
                ReleaseQueries();
                world = current;
                sessions = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadWrite<GameSession>(), ComponentType.ReadWrite<BenchmarkState>());
                enemies = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
            }
            if (sessions.CalculateEntityCount() != 1) return false;
            // The session is written from a job; query singletons only check safety.
            sessions.CompleteDependency();
            session = sessions.GetSingleton<GameSession>();
            return true;
        }

        private void OnDestroy()
        {
            ReleaseQueries();
            if (frames == null) return;
            QualitySettings.vSyncCount = restoreVSyncCount;
            Application.targetFrameRate = restoreTargetFrameRate;
        }

        private void ReleaseQueries()
        {
            if (world != null && world.IsCreated)
            {
                sessions.Dispose();
                enemies.Dispose();
            }
            world = null;
        }
    }
}
