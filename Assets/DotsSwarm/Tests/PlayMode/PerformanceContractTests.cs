using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DotsSwarm.Gameplay;
using DotsSwarm.Spatial;
using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Graphics;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotsSwarm.Tests.Integration
{
    // Frame-cost contracts of the shipped Main scene that functional tests do not
    // observe: main-thread sync points, per-entity render data and the benchmark run.
    [Category("Integration")]
    [Timeout(600000)]
    public sealed class PerformanceContractTests
    {
        private readonly MainSceneSession main = new MainSceneSession();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return main.Load();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return main.Unload();
        }

        [UnityTest]
        public IEnumerator DefaultWorld_MovesPlayerOnMainThreadBeforeEnemyJobsTouchTransforms()
        {
            var world = main.World;
            var systems = SimulationOrder(world);
            var order = new StringBuilder();
            foreach (var system in systems)
                order.Append(SystemName(world, system)).Append(' ');

            // PlayerMovementSystem writes LocalTransform in a main-thread foreach, which
            // completes every scheduled job reading or writing LocalTransform.
            var player = IndexOf(world, systems, world.GetExistingSystem<PlayerMovementSystem>());
            Assert.That(player, Is.LessThan(IndexOf(world, systems, world.GetExistingSystem<EnemyMovementSystem>())),
                order.ToString());
            Assert.That(player, Is.LessThan(IndexOf(world, systems, world.GetExistingSystem<EnemySpatialGridSystem>())),
                order.ToString());
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwarmPrefabs_SkipPerObjectMotionVectors()
        {
            // The camera renders no motion vectors; per-object motion would still bake a
            // previous-matrix component that is copied and uploaded for every instance.
            var manager = main.Manager;
            var enemyPrefab = main.SpawnConfig.EnemyPrefab;
            var projectilePrefab = manager.GetComponentData<Weapon>(main.Player).ProjectilePrefab;
            Assert.That(manager.GetSharedComponent<RenderFilterSettings>(enemyPrefab).MotionMode,
                Is.EqualTo(MotionVectorGenerationMode.Camera));
            Assert.That(manager.GetSharedComponent<RenderFilterSettings>(projectilePrefab).MotionMode,
                Is.EqualTo(MotionVectorGenerationMode.Camera));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BenchmarkRunner_RecordsFilledStressSwarmWithoutVSyncAndAppendsCsvRow()
        {
            var path = Path.Combine(Application.temporaryCachePath, "benchmark-runner-test.csv");
            File.Delete(path);
            var vSyncCount = QualitySettings.vSyncCount;
            var targetFrameRate = Application.targetFrameRate;
            var options = BenchmarkOptions.Create(BenchmarkPreset.Swarm1K);
            options.WarmupSeconds = 0.25f;
            options.DurationSeconds = 0.5f;
            options.OutputPath = path;
            var runner = BenchmarkRunner.Launch(options);
            try
            {
                Assert.That(QualitySettings.vSyncCount, Is.Zero, "Frames are measured without VSync.");
                Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
                yield return main.RunUntil(() => runner.IsFinished, 60f, "The benchmark did not finish.");

                Assert.That(runner.ExitCode, Is.Zero);
                Assert.That(runner.OutputPath, Is.EqualTo(Path.GetFullPath(path)));
                var result = runner.Result;
                Assert.That(main.Benchmark.ActivePreset, Is.EqualTo(BenchmarkPreset.Swarm1K));
                Assert.That(result.Preset, Is.EqualTo(BenchmarkPreset.Swarm1K));
                Assert.That(result.Frames.Frames, Is.GreaterThan(0));
                Assert.That(result.Frames.P95Milliseconds, Is.GreaterThan(0f));
                Assert.That(result.Frames.MaxMilliseconds, Is.GreaterThanOrEqualTo(result.Frames.P95Milliseconds));
                // Warmup starts once the preset is populated; no enemy is in weapon range yet.
                Assert.That(result.MinEnemies, Is.EqualTo((int)BenchmarkPreset.Swarm1K));
                Assert.That(result.MaxEnemies, Is.EqualTo((int)BenchmarkPreset.Swarm1K));
                Assert.That(result.Build, Is.EqualTo("editor"));

                var lines = File.ReadAllLines(path);
                Assert.That(lines, Has.Length.EqualTo(2));
                Assert.That(lines[0], Is.EqualTo(BenchmarkResult.CsvHeader));
                Assert.That(lines[1], Is.EqualTo(result.ToCsvRow()));
            }
            finally
            {
                Object.Destroy(runner.gameObject);
                File.Delete(path);
            }

            yield return null;
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(vSyncCount), "The runner restores VSync.");
            Assert.That(Application.targetFrameRate, Is.EqualTo(targetFrameRate));
        }

        private static List<SystemHandle> SimulationOrder(World world)
        {
            var simulation = world.GetExistingSystemManaged<SimulationSystemGroup>();
            using var systems = simulation.GetAllSystems();
            var order = new List<SystemHandle>(systems.Length);
            foreach (var system in systems) order.Add(system);
            return order;
        }

        private static int IndexOf(World world, List<SystemHandle> systems, SystemHandle system)
        {
            Assert.That(system, Is.Not.EqualTo(SystemHandle.Null));
            var index = systems.IndexOf(system);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"{SystemName(world, system)} is not in SimulationSystemGroup.");
            return index;
        }

        private static string SystemName(World world, SystemHandle system) =>
            TypeManager.GetSystemName(world.Unmanaged.GetSystemTypeIndex(system)).ToString();
    }
}
