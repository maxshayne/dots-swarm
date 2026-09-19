using DotsSwarm.Gameplay;
using DotsSwarm.Spawning;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsSwarm.Tests.Spawning
{
    public sealed class SpawnSystemTests
    {
        [Test]
        public void SpawnConfig_CreateClampsInvalidValues()
        {
            var config = SpawnConfig.Create(Entity.Null, -10f, -5f, -1, 0u).WithRamp(-1f, -2f, -3f);

            Assert.That(config.SpawnRadius, Is.EqualTo(0f));
            Assert.That(config.InitialSpawnRate, Is.EqualTo(0f));
            Assert.That(config.PeakSpawnRate, Is.EqualTo(0f));
            Assert.That(config.RampDuration, Is.EqualTo(0f));
            Assert.That(config.RampExponent, Is.EqualTo(0f));
            Assert.That(config.MaxEnemies, Is.EqualTo(0));
        }

        [Test]
        public void SpawnRate_EasesFromInitialToPeakThenHolds()
        {
            var config = SpawnConfig.Create(Entity.Null, 40f, 3f, 20000, 1u).WithRamp(425f, 160f, 2.5f);

            Assert.That(config.CalculateSpawnRate(-1f), Is.EqualTo(3f));
            Assert.That(config.CalculateSpawnRate(0f), Is.EqualTo(3f));
            Assert.That(config.CalculateSpawnRate(80f), Is.EqualTo(3f + 422f * math.pow(0.5f, 2.5f)).Within(0.001f));
            Assert.That(config.CalculateSpawnRate(160f), Is.EqualTo(425f));
            Assert.That(config.CalculateSpawnRate(500f), Is.EqualTo(425f));
            for (var time = 1f; time <= 180f; time++)
            {
                Assert.That(config.CalculateSpawnRate(time), Is.GreaterThanOrEqualTo(config.CalculateSpawnRate(time - 1f)));
            }
        }

        [Test]
        public void ScheduledSpawns_IntegrateRateAcrossAndAfterRamp()
        {
            var config = SpawnConfig.Create(Entity.Null, 40f, 3f, 20000, 1u).WithRamp(425f, 160f, 2.5f);
            const float step = 0.01f;
            var integral = 0d;
            for (var index = 0; index < 18000; index++)
            {
                integral += config.CalculateSpawnRate((index + 0.5f) * step) * step;
            }

            Assert.That(config.CalculateScheduledSpawns(0f), Is.Zero);
            Assert.That(config.CalculateScheduledSpawns(-5f), Is.Zero);
            Assert.That(config.CalculateScheduledSpawns(160f), Is.EqualTo(3f * 160f + 422f * 160f / 3.5f).Within(0.01f));
            Assert.That(config.CalculateScheduledSpawns(180f),
                Is.EqualTo(config.CalculateScheduledSpawns(160f) + 425f * 20f).Within(0.01f));
            Assert.That(config.CalculateScheduledSpawns(180f), Is.EqualTo((float)integral).Within(1f));
        }

        [Test]
        public void ScheduledSpawns_WithoutRampKeepInitialRate()
        {
            var config = SpawnConfig.Create(Entity.Null, 40f, 7f, 20000, 1u).WithRamp(425f, 0f, 2.5f);

            Assert.That(config.CalculateSpawnRate(100f), Is.EqualTo(7f));
            Assert.That(config.CalculateScheduledSpawns(100f), Is.EqualTo(700f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        [TestCase(0f)]
        public void SpawnCount_FollowsScheduleIndependentOfFrameRate(float fixedDelta)
        {
            var config = SpawnConfig.Create(Entity.Null, 40f, 3f, int.MaxValue, 1u).WithRamp(425f, 160f, 2.5f);
            var budget = 0f;
            var elapsed = 0f;
            var total = 0;
            for (var frame = 0; elapsed < 120f; frame++)
            {
                // A zero fixed delta selects irregular frames: idle updates and long hitches.
                var delta = fixedDelta > 0f ? fixedDelta : (frame % 7 == 0 ? 0.25f : frame % 3 * 0.009f);
                var next = elapsed + delta;
                total += SpawnSystem.CalculateSpawnCount(ref budget,
                    config.CalculateScheduledSpawns(next) - config.CalculateScheduledSpawns(elapsed), total, int.MaxValue);
                elapsed = next;
            }

            Assert.That(total, Is.EqualTo((int)math.floor(config.CalculateScheduledSpawns(elapsed))).Within(1));
            Assert.That(budget, Is.InRange(0f, 1f));
        }

        [Test]
        public void CalculateSpawnCount_RespectsEnemyLimit()
        {
            var budget = 0.25f;

            var spawnCount = SpawnSystem.CalculateSpawnCount(
                ref budget,
                5f,
                8,
                10);

            Assert.That(spawnCount, Is.EqualTo(2));
            Assert.That(budget, Is.EqualTo(0f));
        }

        [Test]
        public void DefaultProfile_ReachesSwarmCapInFinalMinute()
        {
            var config = SpawnConfig.Create(Entity.Null, SpawnConfig.DefaultSpawnRadius,
                SpawnConfig.DefaultInitialSpawnRate, SpawnConfig.DefaultMaxEnemies, SpawnConfig.DefaultRandomSeed)
                .WithRamp(SpawnConfig.DefaultPeakSpawnRate, SpawnConfig.DefaultRampDuration, SpawnConfig.DefaultRampExponent);

            // Early waves stay small enough for the weapon; the cap waits for the finale
            // and leaves room to replace about a thousand kills before the deadline.
            Assert.That(config.CalculateScheduledSpawns(30f), Is.LessThan(200f));
            Assert.That(config.CalculateScheduledSpawns(GameSession.Duration - 30f), Is.LessThan(config.MaxEnemies));
            Assert.That(config.CalculateScheduledSpawns(GameSession.Duration - 10f),
                Is.GreaterThan(config.MaxEnemies + 1000));
        }

        [Test]
        public void CalculateSpawnPosition_IsDeterministicOnRadius()
        {
            var position = SpawnSystem.CalculateSpawnPosition(
                float3.zero,
                0.5f,
                10f,
                new float2(100f),
                123u,
                7u);
            var repeatedPosition = SpawnSystem.CalculateSpawnPosition(
                float3.zero,
                0.5f,
                10f,
                new float2(100f),
                123u,
                7u);

            Assert.That(math.all(position == repeatedPosition), Is.True);
            Assert.That(math.length(position.xz), Is.EqualTo(10f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(0.5f));
        }

        [TestCase(49.5f, 0f)]
        [TestCase(-50f, 20f)]
        [TestCase(49.5f, -49.5f)]
        [TestCase(-50f, 50f)]
        public void CalculateSpawnPosition_MirrorsOffArenaOffsetsToKeepDistance(float x, float z)
        {
            var center = new float3(x, 0.5f, z);
            for (var sequence = 0u; sequence < 1000u; sequence++)
            {
                var position = SpawnSystem.CalculateSpawnPosition(center, 0.5f, 40f, new float2(50f), 1u, sequence);

                Assert.That(math.distance(position.xz, center.xz), Is.EqualTo(40f).Within(0.001f));
                Assert.That(math.all(math.abs(position.xz) <= 50f), Is.True);
            }
        }

        [Test]
        public void CalculateSpawnPosition_ClampsToArenaBounds()
        {
            var position = SpawnSystem.CalculateSpawnPosition(
                new float3(4.9f, 0f, 4.9f),
                0.5f,
                10f,
                new float2(5f),
                321u,
                9u);

            Assert.That(position.x, Is.InRange(-5f, 5f));
            Assert.That(position.z, Is.InRange(-5f, 5f));
        }
    }
}
