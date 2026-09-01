using DotsSwarm.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Transforms;

namespace DotsSwarm.Tests.Gameplay
{
    public sealed class EnemyMovementSystemTests
    {
        private const int EnemyCount = 10000;

        [Test]
        public void EnemyCreate_ClampsNegativeMovementSpeed()
        {
            var enemy = Enemy.Create(-1f);

            Assert.That(enemy.MovementSpeed, Is.EqualTo(0f));
        }

        [Test]
        public void CalculatePosition_MovesTowardPlayerOnGroundPlane()
        {
            var position = EnemyMovementSystem.CalculatePosition(
                new float3(0f, 0.4f, 0f),
                new float3(3f, 1f, 4f),
                2f,
                1f);

            Assert.That(position.x, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(position.y, Is.EqualTo(0.4f));
            Assert.That(position.z, Is.EqualTo(1.6f).Within(0.0001f));
        }

        [Test]
        public void CalculatePosition_DoesNotOvershootPlayer()
        {
            var position = EnemyMovementSystem.CalculatePosition(
                new float3(-1f, 0.4f, 0f),
                new float3(1f, 0.5f, 0f),
                10f,
                1f);

            Assert.That(position.x, Is.EqualTo(1f));
            Assert.That(position.y, Is.EqualTo(0.4f));
            Assert.That(position.z, Is.EqualTo(0f));
        }

        [Test]
        public void System_MovesTenThousandEnemiesWithoutGcAllocations()
        {
            using var world = new World("Enemy movement test");
            var entityManager = world.EntityManager;
            var player = entityManager.CreateEntity(typeof(Player), typeof(LocalTransform));
            entityManager.SetComponentData(player, new Player());
            entityManager.SetComponentData(
                player,
                LocalTransform.FromPosition(new float3(10f, 0.5f, 0f)));

            var enemyArchetype = entityManager.CreateArchetype(
                typeof(Enemy),
                typeof(LocalTransform));
            using var enemies = entityManager.CreateEntity(
                enemyArchetype,
                EnemyCount,
                Allocator.Temp);

            for (var index = 0; index < enemies.Length; index++)
            {
                entityManager.SetComponentData(enemies[index], Enemy.Create(3f));
                entityManager.SetComponentData(
                    enemies[index],
                    LocalTransform.FromPosition(new float3(0f, 0.4f, index % 2)));
            }

            var system = world.GetOrCreateSystem<EnemyMovementSystem>();
            world.SetTime(new TimeData(0d, 1f / 60f));
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            using var gcRecorder = new ProfilerRecorder(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.WrapAroundWhenCapacityReached
                | ProfilerRecorderOptions.SumAllSamplesInFrame
                | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            Assert.That(gcRecorder.Valid, Is.True);

            gcRecorder.Start();
            system.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            gcRecorder.Stop();

            var allocationCount = gcRecorder.Count > 0
                ? gcRecorder.GetSample(0).Count
                : 0;
            var firstTransform = entityManager.GetComponentData<LocalTransform>(enemies[0]);
            var lastTransform = entityManager.GetComponentData<LocalTransform>(
                enemies[enemies.Length - 1]);

            Assert.That(allocationCount, Is.EqualTo(0));
            Assert.That(firstTransform.Position.x, Is.GreaterThan(0f));
            Assert.That(lastTransform.Position.x, Is.GreaterThan(0f));
        }
    }
}
