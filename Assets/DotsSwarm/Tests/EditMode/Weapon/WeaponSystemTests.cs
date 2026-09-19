using DotsSwarm.Gameplay;
using DotsSwarm.Spatial;
using DotsSwarm.Spawning;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Tests.Gameplay
{
    public sealed class WeaponSystemTests
    {
        private World world;
        private EntityManager entityManager;
        private EndSimulationEntityCommandBufferSystem endSimulation;
        private SystemHandle gridSystem;
        private SystemHandle weaponSystem;
        private Entity player;
        private Entity prefab;
        private EntityQuery projectiles;

        [SetUp]
        public void SetUp()
        {
            world = new World("Weapon test");
            entityManager = world.EntityManager;
            endSimulation = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            gridSystem = world.GetOrCreateSystem<EnemySpatialGridSystem>();
            weaponSystem = world.GetOrCreateSystem<WeaponSystem>();
            // Baked prefabs carry LocalToWorld; firing sets it on instantiation.
            prefab = entityManager.CreateEntity(typeof(Prefab), typeof(Projectile), typeof(LocalTransform),
                typeof(LocalToWorld));
            entityManager.SetComponentData(prefab, LocalTransform.FromScale(0.25f));
            player = entityManager.CreateEntity(
                typeof(Player), typeof(Weapon), typeof(WeaponState), typeof(LocalTransform));
            entityManager.SetComponentData(player, LocalTransform.FromPosition(new float3(0f, 0.5f, 0f)));
            entityManager.SetComponentData(player, Weapon.Create(prefab, 0.5f, 20f, 30f, 2f));
            projectiles = entityManager.CreateEntityQuery(typeof(Projectile), typeof(LocalTransform));
        }

        [TearDown]
        public void TearDown()
        {
            world.Dispose();
        }

        [Test]
        public void WeaponCreate_ClampsInvalidValues()
        {
            var weapon = Weapon.Create(prefab, -1f, -2f, -3f, -4f);

            Assert.That(weapon.Cooldown, Is.EqualTo(Weapon.MinimumCooldown));
            Assert.That(weapon.Range, Is.Zero);
            Assert.That(weapon.ProjectileSpeed, Is.Zero);
            Assert.That(weapon.ProjectileLifetime, Is.Zero);
        }

        [Test]
        public void System_FiresAtNearestEnemyBeyondAdjacentCellsThroughEcb()
        {
            CreateEnemy(new float3(0f, 0.4f, 15f));
            CreateEnemy(new float3(-6f, 10f, -8f));
            world.SetTime(new TimeData(0d, 0.125f));

            gridSystem.Update(world.Unmanaged);
            weaponSystem.Update(world.Unmanaged);

            Assert.That(projectiles.CalculateEntityCount(), Is.Zero, "Spawning must wait for ECB playback.");
            endSimulation.Update();

            var projectileEntity = projectiles.GetSingletonEntity();
            var projectile = entityManager.GetComponentData<Projectile>(projectileEntity);
            var transform = entityManager.GetComponentData<LocalTransform>(projectileEntity);
            Assert.That(math.distance(projectile.Velocity, new float3(-18f, 0f, -24f)), Is.LessThan(0.0001f));
            Assert.That(projectile.LifetimeRemaining, Is.EqualTo(2f));
            Assert.That(transform.Position, Is.EqualTo(new float3(0f, 0.5f, 0f)));
            Assert.That(transform.Scale, Is.EqualTo(0.25f));
            Assert.That(entityManager.HasComponent<Prefab>(projectileEntity), Is.False);
            Assert.That(entityManager.GetComponentData<Projectile>(prefab).Velocity, Is.EqualTo(float3.zero));
        }

        [Test]
        public void System_RespectsCooldownAndDoesNotBankIdleShots()
        {
            Tick(10f);
            Assert.That(projectiles.CalculateEntityCount(), Is.Zero);
            CreateEnemy(new float3(3f, 0f, 0f));

            Tick(0.125f);
            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(1));
            Tick(0.25f);
            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(1));
            Tick(0.25f);
            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(2));
            Tick(10f);
            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(3));
        }

        [Test]
        public void System_IgnoresEnemiesOutsideCircularRangeAndEnemyPrefabs()
        {
            CreateEnemy(new float3(20.01f, 0f, 0f));
            CreateEnemy(new float3(19f, 0f, 19f));
            var enemyPrefab = CreateEnemy(new float3(1f, 0f, 0f));
            entityManager.AddComponent<Prefab>(enemyPrefab);

            Tick(1f);

            Assert.That(projectiles.CalculateEntityCount(), Is.Zero);
            Assert.That(entityManager.GetComponentData<WeaponState>(player).CooldownRemaining, Is.Zero);
        }

        [Test]
        public void System_FiresAtExactRangeBoundary()
        {
            CreateEnemy(new float3(20f, 0f, 0f));

            Tick(0.1f);

            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(1));
        }

        [Test]
        public void System_OverlappingEnemyProducesFiniteVelocity()
        {
            CreateEnemy(new float3(0f, 0.4f, 0f));

            Tick(0.1f);

            var velocity = projectiles.GetSingleton<Projectile>().Velocity;
            Assert.That(math.all(math.isfinite(velocity)), Is.True);
            Assert.That(math.length(velocity), Is.EqualTo(30f).Within(0.0001f));
        }

        [Test]
        public void FindNearestEnemy_MatchesBruteForceAcrossNegativeCellsAndTies()
        {
            var random = Random.CreateFromIndex(123u);
            var archetype = entityManager.CreateArchetype(typeof(Enemy), typeof(LocalTransform));
            using var enemies = entityManager.CreateEntity(archetype, 512, Allocator.Temp);
            for (var index = 0; index < enemies.Length; index++)
            {
                var position = random.NextFloat2(new float2(-50f), new float2(50f));
                entityManager.SetComponentData(enemies[index], LocalTransform.FromPosition(
                    new float3(position.x, 0.4f, position.y)));
            }

            gridSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            ref var grid = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(gridSystem);
            var transforms = endSimulation.GetComponentLookup<LocalTransform>(true);

            for (var sample = 0; sample < 32; sample++)
            {
                var point = random.NextFloat2(new float2(-45f), new float2(45f));
                var origin = new float3(point.x, 0.5f, point.y);
                var range = random.NextFloat(0f, 30f);
                var expected = Entity.Null;
                var bestDistance = range * range;
                foreach (var enemy in enemies)
                {
                    var distance = math.distancesq(origin.xz, transforms[enemy].Position.xz);
                    if (distance < bestDistance
                        || (distance == bestDistance && (expected == Entity.Null || enemy.Index < expected.Index)))
                    {
                        expected = enemy;
                        bestDistance = distance;
                    }
                }

                Assert.That(WeaponSystem.FindNearestEnemy(grid.Grid, transforms, origin, range), Is.EqualTo(expected));
            }

            entityManager.DestroyEntity(enemies);
            var firstTie = CreateEnemy(new float3(-1f, 0f, 0f));
            CreateEnemy(new float3(1f, 0f, 0f));
            gridSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            transforms = endSimulation.GetComponentLookup<LocalTransform>(true);
            Assert.That(WeaponSystem.FindNearestEnemy(grid.Grid, transforms, float3.zero, 1f), Is.EqualTo(firstTie));
        }

        [Test]
        public void System_RebuildWaitsForReaderAndRetargetsAfterEnemyRemoval()
        {
            var firstEnemy = CreateEnemy(new float3(-3f, 0f, 0f));
            CreateEnemy(new float3(5f, 0f, 0f));
            world.SetTime(new TimeData(0d, 1f));
            for (var frame = 0; frame < 4; frame++)
            {
                gridSystem.Update(world.Unmanaged);
                weaponSystem.Update(world.Unmanaged);
            }

            endSimulation.Update();
            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(4));
            entityManager.DestroyEntity(projectiles);
            entityManager.DestroyEntity(firstEnemy);

            Tick(1f);

            Assert.That(projectiles.GetSingleton<Projectile>().Velocity, Is.EqualTo(new float3(30f, 0f, 0f)));
        }

        [Test]
        public void SimulationGroup_FiresMovesAndExpiresAfterPlayerIsRemoved()
        {
            CreateEnemy(new float3(10f, 0f, 0f));
            var simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simulation.AddSystemToUpdateList(endSimulation);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileMovementSystem>());
            simulation.AddSystemToUpdateList(weaponSystem);
            simulation.AddSystemToUpdateList(gridSystem);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<EnemyMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<SpawnSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<TransformSystemGroup>());
            simulation.SortSystems();
            world.SetTime(new TimeData(0d, 0.25f));

            simulation.Update();
            var projectile = projectiles.GetSingletonEntity();
            Assert.That(entityManager.GetComponentData<LocalTransform>(projectile).Position,
                Is.EqualTo(new float3(0f, 0.5f, 0f)));
            entityManager.DestroyEntity(player);

            simulation.Update();
            Assert.That(entityManager.GetComponentData<LocalTransform>(projectile).Position,
                Is.EqualTo(new float3(7.5f, 0.5f, 0f)));
            world.SetTime(new TimeData(2d, 1.75f));
            simulation.Update();

            Assert.That(projectiles.CalculateEntityCount(), Is.Zero);
            Assert.That(entityManager.Exists(prefab), Is.True);
        }

        private Entity CreateEnemy(float3 position)
        {
            var enemy = entityManager.CreateEntity(typeof(Enemy), typeof(LocalTransform));
            entityManager.SetComponentData(enemy, LocalTransform.FromPosition(position));
            return enemy;
        }

        private void Tick(float deltaTime)
        {
            world.SetTime(new TimeData(world.Time.ElapsedTime + deltaTime, deltaTime));
            gridSystem.Update(world.Unmanaged);
            weaponSystem.Update(world.Unmanaged);
            endSimulation.Update();
        }
    }
}
