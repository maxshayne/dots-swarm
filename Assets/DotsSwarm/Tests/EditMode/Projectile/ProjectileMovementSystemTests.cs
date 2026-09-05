using DotsSwarm.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Tests.Gameplay
{
    public sealed class ProjectileMovementSystemTests
    {
        private World world;
        private EntityManager entityManager;
        private EndSimulationEntityCommandBufferSystem endSimulation;
        private SystemHandle movementSystem;

        [SetUp]
        public void SetUp()
        {
            world = new World("Projectile movement test");
            entityManager = world.EntityManager;
            endSimulation = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            movementSystem = world.GetOrCreateSystem<ProjectileMovementSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            world.Dispose();
        }

        [Test]
        public void System_MovesProjectileAndCountsDownLifetime()
        {
            var projectile = CreateProjectile(2f);

            Tick(0.25f);

            var transform = entityManager.GetComponentData<LocalTransform>(projectile);
            var state = entityManager.GetComponentData<Projectile>(projectile);
            Assert.That(transform.Position, Is.EqualTo(new float3(4f, 0.5f, 6f)));
            Assert.That(transform.Scale, Is.EqualTo(0.25f));
            Assert.That(state.LifetimeRemaining, Is.EqualTo(1.75f));
        }

        [TestCase(0.25f, 0.25f)]
        [TestCase(0.25f, 10f)]
        [TestCase(0f, 0f)]
        [TestCase(-1f, 0.25f)]
        public void System_ExpiresThroughEcbWithoutMovingPastLifetime(float lifetime, float deltaTime)
        {
            var projectile = CreateProjectile(lifetime);
            world.SetTime(new TimeData(0d, deltaTime));

            movementSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Assert.That(entityManager.Exists(projectile), Is.True);
            var expectedPosition = new float3(1f, 0.5f, 2f)
                                   + new float3(12f, 0f, 16f) * math.max(0f, lifetime);
            Assert.That(entityManager.GetComponentData<LocalTransform>(projectile).Position, Is.EqualTo(expectedPosition));
            endSimulation.Update();
            Assert.That(entityManager.Exists(projectile), Is.False);
        }

        [Test]
        public void System_ZeroDeltaTimePreservesLiveProjectile()
        {
            var projectile = CreateProjectile(2f);

            Tick(0f);

            Assert.That(entityManager.GetComponentData<LocalTransform>(projectile).Position, Is.EqualTo(new float3(1f, 0.5f, 2f)));
            Assert.That(entityManager.GetComponentData<Projectile>(projectile).LifetimeRemaining, Is.EqualTo(2f));
        }

        [Test]
        public void System_DeletesTenThousandProjectilesButPreservesPrefab()
        {
            var prefab = CreateProjectile(0.25f);
            entityManager.AddComponent<Prefab>(prefab);
            using var instances = entityManager.Instantiate(prefab, 10000, Allocator.Temp);

            Tick(0.125f);
            Tick(0.125f);

            using var query = entityManager.CreateEntityQuery(typeof(Projectile));
            Assert.That(query.CalculateEntityCount(), Is.Zero);
            Assert.That(entityManager.Exists(instances[0]), Is.False);
            Assert.That(entityManager.Exists(instances[instances.Length - 1]), Is.False);
            Assert.That(entityManager.GetComponentData<Projectile>(prefab).LifetimeRemaining, Is.EqualTo(0.25f));
            Assert.That(entityManager.GetComponentData<LocalTransform>(prefab).Position, Is.EqualTo(new float3(1f, 0.5f, 2f)));
        }

        private Entity CreateProjectile(float lifetime)
        {
            var entity = entityManager.CreateEntity(typeof(Projectile), typeof(LocalTransform));
            entityManager.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                new float3(1f, 0.5f, 2f), quaternion.identity, 0.25f));
            entityManager.SetComponentData(entity, new Projectile
            {
                Velocity = new float3(12f, 0f, 16f),
                LifetimeRemaining = lifetime
            });
            return entity;
        }

        private void Tick(float deltaTime)
        {
            world.SetTime(new TimeData(world.Time.ElapsedTime + deltaTime, deltaTime));
            movementSystem.Update(world.Unmanaged);
            endSimulation.Update();
        }
    }
}
