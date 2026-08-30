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
            var config = SpawnConfig.Create(Entity.Null, -10f, -5f, -1, 0u);

            Assert.That(config.SpawnRadius, Is.EqualTo(0f));
            Assert.That(config.SpawnRate, Is.EqualTo(0f));
            Assert.That(config.MaxEnemies, Is.EqualTo(0));
        }

        [Test]
        public void CalculateSpawnCount_RespectsEnemyLimit()
        {
            var budget = 0.25f;

            var spawnCount = SpawnSystem.CalculateSpawnCount(
                ref budget,
                10f,
                0.5f,
                8,
                10);

            Assert.That(spawnCount, Is.EqualTo(2));
            Assert.That(budget, Is.EqualTo(0f));
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
