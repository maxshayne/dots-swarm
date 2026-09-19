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
    public sealed class CombatSystemTests
    {
        private World world;
        private EntityManager entityManager;
        private SimulationSystemGroup simulation;
        private EndSimulationEntityCommandBufferSystem endSimulation;
        private SystemHandle gridSystem;
        private SystemHandle hitSystem;
        private SystemHandle movementSystem;
        private SystemHandle damageSystem;
        private SystemHandle deathSystem;

        [SetUp]
        public void SetUp()
        {
            world = new World("Combat test");
            entityManager = world.EntityManager;
            simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            endSimulation = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            gridSystem = world.GetOrCreateSystem<EnemySpatialGridSystem>();
            hitSystem = world.GetOrCreateSystem<ProjectileHitSystem>();
            movementSystem = world.GetOrCreateSystem<ProjectileMovementSystem>();
            damageSystem = world.GetOrCreateSystem<DamageSystem>();
            deathSystem = world.GetOrCreateSystem<DeathSystem>();

            // Deliberately register out of order to exercise production attributes.
            simulation.AddSystemToUpdateList(endSimulation);
            simulation.AddSystemToUpdateList(deathSystem);
            simulation.AddSystemToUpdateList(damageSystem);
            simulation.AddSystemToUpdateList(movementSystem);
            simulation.AddSystemToUpdateList(hitSystem);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<WeaponSystem>());
            simulation.AddSystemToUpdateList(gridSystem);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<EnemyMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<SpawnSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<TransformSystemGroup>());
            simulation.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            world.Dispose();
        }

        [Test]
        public void CombatSettings_ClampInvalidValues()
        {
            var combat = ProjectileCombat.Create(-1, -2f);
            Assert.That(combat.Damage, Is.Zero);
            Assert.That(combat.Radius, Is.Zero);
            Assert.That(combat.HitEntity, Is.EqualTo(Entity.Null));
            Assert.That(Health.Create(-1).Current, Is.EqualTo(1));
        }

        [Test]
        public void Hit_RecordsTargetThenDamageAndDeathRunBeforeEcbPlayback()
        {
            var enemy = CreateEnemy(new float3(4f, 10f, 0f), 2);
            var projectile = CreateProjectile(float3.zero, new float3(30f, 0f, 0f), damage: 2);
            world.SetTime(new TimeData(0d, 0.25f));

            gridSystem.Update(world.Unmanaged);
            hitSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Assert.That(entityManager.GetComponentData<ProjectileCombat>(projectile).HitEntity, Is.EqualTo(enemy));
            Assert.That(entityManager.GetComponentData<Health>(enemy).Current, Is.EqualTo(2));
            Assert.That(entityManager.GetBuffer<DamageEvent>(enemy).Length, Is.Zero);

            movementSystem.Update(world.Unmanaged);
            damageSystem.Update(world.Unmanaged);
            deathSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Assert.That(entityManager.GetComponentData<Health>(enemy).Current, Is.Zero);
            Assert.That(entityManager.GetBuffer<DamageEvent>(enemy).Length, Is.Zero);
            Assert.That(entityManager.GetComponentData<ProjectileCombat>(projectile).HitEntity, Is.EqualTo(Entity.Null));
            Assert.That(entityManager.Exists(enemy), Is.True, "Structural changes must wait for ECB.");
            Assert.That(entityManager.Exists(projectile), Is.True);

            endSimulation.Update();
            Assert.That(entityManager.Exists(enemy), Is.False);
            Assert.That(entityManager.Exists(projectile), Is.False);
        }

        [Test]
        public void Hit_SelectsFirstContactInsteadOfNearestCenterAndOnlyDamagesOnce()
        {
            var nearCenter = CreateEnemy(new float3(3f, 0f, 0.52f));
            var firstContact = CreateEnemy(new float3(3.1f, 0f, 0f));
            var behind = CreateEnemy(new float3(6f, 0f, 0f));
            CreateProjectile(float3.zero, new float3(40f, 0f, 0f));

            Tick(0.25f);
            Tick(0.25f);

            AssertHealth(firstContact, 2);
            AssertHealth(nearCenter, 3);
            AssertHealth(behind, 3);
            AssertNoProjectiles();
        }

        [Test]
        public void Hit_TiesUseEntityIdentity()
        {
            var first = CreateEnemy(new float3(4f, 0f, 0f));
            var second = CreateEnemy(new float3(4f, 0f, 0f));
            var expected = first.Index < second.Index ? first : second;
            var other = expected == first ? second : first;
            CreateProjectile(float3.zero, new float3(40f, 0f, 0f));

            Tick(0.25f);

            AssertHealth(expected, 2);
            AssertHealth(other, 3);
        }

        [TestCase(1.5f, 2f)]
        [TestCase(-2.5f, -2f)]
        public void Hit_IncludesRadiusAcrossCellBoundaries(float projectileZ, float enemyZ)
        {
            var enemy = CreateEnemy(new float3(-6f, 0f, enemyZ));
            CreateProjectile(new float3(-10f, 0f, projectileZ), new float3(32f, 0f, 0f));

            Tick(0.25f);

            AssertHealth(enemy, 2);
            AssertNoProjectiles();
        }

        [Test]
        public void Hit_SweepsLongDiagonalAcrossNegativeCells()
        {
            var enemy = CreateEnemy(new float3(-3f, 0f, -3f));
            CreateProjectile(new float3(-25f, 0f, -25f), new float3(50f, 0f, 50f));

            Tick(1f);

            AssertHealth(enemy, 2);
            AssertNoProjectiles();
        }

        [TestCase(0f, 0f, 1f, true)]
        [TestCase(5f, 1f, 1f, true)]
        [TestCase(11f, 0f, 1f, true)]
        [TestCase(5f, 1.01f, 1f, false)]
        [TestCase(-2f, 0f, 1f, false)]
        [TestCase(11.01f, 0f, 1f, false)]
        public void Segment_IncludesOverlapTangentAndEndpointButRejectsMisses(
            float x, float z, float radius, bool expected)
        {
            var hit = ProjectileHitSystem.TryGetHitFraction(
                float2.zero, new float2(10f, 0f), new float2(x, z), radius, out var fraction);

            Assert.That(hit, Is.EqualTo(expected));
            if (hit)
            {
                Assert.That(fraction, Is.InRange(0f, 1f));
            }
        }

        [Test]
        public void Hit_StationaryProjectileHitsOverlapButNotDistantEnemy()
        {
            var overlapping = CreateEnemy(float3.zero);
            var distant = CreateEnemy(new float3(2f, 0f, 0f));
            CreateProjectile(float3.zero, float3.zero);
            var miss = CreateProjectile(new float3(5f, 0f, 0f), float3.zero);

            Tick(0.25f);

            AssertHealth(overlapping, 2);
            AssertHealth(distant, 3);
            Assert.That(entityManager.Exists(miss), Is.True);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void Hit_DoesNotRunWhenTimeDoesNotAdvance(float deltaTime)
        {
            var enemy = CreateEnemy(float3.zero);
            var projectile = CreateProjectile(float3.zero, float3.zero);

            Tick(deltaTime);

            AssertHealth(enemy, 3);
            Assert.That(entityManager.Exists(projectile), Is.True);
            Assert.That(entityManager.GetComponentData<Projectile>(projectile).LifetimeRemaining, Is.EqualTo(2f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void Hit_AlreadyExpiredProjectileCannotDealDamage(float lifetime)
        {
            var enemy = CreateEnemy(float3.zero);
            CreateProjectile(float3.zero, new float3(100f, 0f, 0f), lifetime);

            Tick(1f);

            AssertHealth(enemy, 3);
            AssertNoProjectiles();
        }

        [TestCase(4f, 2)]
        [TestCase(6f, 3)]
        public void Hit_ClampsSweepToRemainingLifetime(float enemyX, int expectedHealth)
        {
            var enemy = CreateEnemy(new float3(enemyX, 0f, 0f));
            CreateProjectile(float3.zero, new float3(20f, 0f, 0f), 0.25f);

            Tick(10f);

            AssertHealth(enemy, expectedHealth);
            AssertNoProjectiles();
        }

        [Test]
        public void Hit_IgnoresDeadEnemiesAndPrefabs()
        {
            var dead = CreateEnemy(new float3(1f, 0f, 0f), 0);
            var prefab = CreateEnemy(new float3(2f, 0f, 0f));
            entityManager.AddComponent<Prefab>(prefab);
            var live = CreateEnemy(new float3(3f, 0f, 0f));
            var projectilePrefab = CreateProjectile(float3.zero, new float3(20f, 0f, 0f));
            entityManager.AddComponent<Prefab>(projectilePrefab);
            CreateProjectile(float3.zero, new float3(20f, 0f, 0f));

            Tick(0.25f);

            AssertHealth(prefab, 3);
            AssertHealth(live, 2);
            Assert.That(entityManager.Exists(dead), Is.False);
            Assert.That(entityManager.GetComponentData<Projectile>(projectilePrefab).LifetimeRemaining, Is.EqualTo(2f));
            Assert.That(entityManager.GetComponentData<ProjectileCombat>(projectilePrefab).HitEntity, Is.EqualTo(Entity.Null));
        }

        [Test]
        public void Hit_WithoutTargetMovesAndEventuallyExpires()
        {
            var projectile = CreateProjectile(float3.zero, new float3(10f, 0f, 0f));

            Tick(0.25f);

            Assert.That(entityManager.GetComponentData<LocalTransform>(projectile).Position,
                Is.EqualTo(new float3(2.5f, 0f, 0f)));
            Tick(2f);
            Assert.That(entityManager.Exists(projectile), Is.False);
        }

        [Test]
        public void Damage_ConsumesBufferOnceAndIgnoresNegativeDamage()
        {
            var enemy = CreateEnemy(float3.zero, 10);
            var events = entityManager.GetBuffer<DamageEvent>(enemy);
            events.Add(new DamageEvent { Amount = 2 });
            events.Add(new DamageEvent { Amount = -100 });
            events.Add(new DamageEvent { Amount = 3 });

            Tick(0.1f);
            Tick(0.1f);

            AssertHealth(enemy, 5);
            Assert.That(entityManager.GetBuffer<DamageEvent>(enemy).Length, Is.Zero);
        }

        [Test]
        public void Damage_HandlesTenThousandSimultaneousHitsWithoutLossOrReplay()
        {
            var enemy = CreateEnemy(new float3(3f, 0f, 0f), 20000);
            var prefab = CreateProjectile(float3.zero, new float3(30f, 0f, 0f));
            entityManager.AddComponent<Prefab>(prefab);
            using var instances = entityManager.Instantiate(prefab, 10000, Allocator.Temp);

            Tick(0.25f);
            Tick(0.25f);

            AssertHealth(enemy, 10000);
            Assert.That(entityManager.GetBuffer<DamageEvent>(enemy).Length, Is.Zero);
            AssertNoProjectiles();
            Assert.That(entityManager.Exists(prefab), Is.True);
        }

        [Test]
        public void Damage_DiscardsHitIfTargetWasRemovedBeforeTransfer()
        {
            var enemy = CreateEnemy(new float3(2f, 0f, 0f));
            var projectile = CreateProjectile(float3.zero, new float3(20f, 0f, 0f));
            world.SetTime(new TimeData(0d, 0.25f));
            gridSystem.Update(world.Unmanaged);
            hitSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            entityManager.DestroyEntity(enemy);

            damageSystem.Update(world.Unmanaged);
            movementSystem.Update(world.Unmanaged);
            endSimulation.Update();

            Assert.That(entityManager.Exists(projectile), Is.False);
        }

        [Test]
        public void Death_HandlesTenThousandEnemiesAndOverkillWithoutTouchingPrefabOrPlayer()
        {
            var prefab = CreateEnemy(float3.zero, int.MaxValue);
            entityManager.AddComponent<Prefab>(prefab);
            var events = entityManager.GetBuffer<DamageEvent>(prefab);
            events.Add(new DamageEvent { Amount = int.MaxValue });
            events.Add(new DamageEvent { Amount = int.MaxValue });
            using var enemies = entityManager.Instantiate(prefab, 10000, Allocator.Temp);
            var player = entityManager.CreateEntity(typeof(Player), typeof(Health), typeof(LocalTransform));

            Tick(0.1f);

            using var query = entityManager.CreateEntityQuery(typeof(Enemy));
            Assert.That(query.CalculateEntityCount(), Is.Zero);
            Assert.That(entityManager.Exists(enemies[0]), Is.False);
            Assert.That(entityManager.Exists(enemies[enemies.Length - 1]), Is.False);
            AssertHealth(prefab, int.MaxValue);
            Assert.That(entityManager.GetBuffer<DamageEvent>(prefab).Length, Is.EqualTo(2));
            Assert.That(entityManager.Exists(player), Is.True);
        }

        [Test]
        public void Grid_RebuildWaitsForHitReaderAndDropsDestroyedEnemy()
        {
            var first = CreateEnemy(new float3(2f, 0f, 0f), 1);
            var second = CreateEnemy(new float3(4f, 0f, 0f), 1);
            CreateProjectile(float3.zero, new float3(20f, 0f, 0f));
            world.SetTime(new TimeData(0d, 0.25f));

            for (var frame = 0; frame < 4; frame++)
            {
                gridSystem.Update(world.Unmanaged);
                hitSystem.Update(world.Unmanaged);
            }

            movementSystem.Update(world.Unmanaged);
            damageSystem.Update(world.Unmanaged);
            deathSystem.Update(world.Unmanaged);
            endSimulation.Update();
            Assert.That(entityManager.Exists(first), Is.False);
            CreateProjectile(float3.zero, new float3(20f, 0f, 0f));

            Tick(0.25f);

            Assert.That(entityManager.Exists(second), Is.False);
            AssertNoProjectiles();
        }

        [Test]
        public void Simulation_FiresPrefabSettingsThenKillsEnemyAfterPlayerRemoval()
        {
            var prefab = CreateProjectile(float3.zero, float3.zero, damage: 3);
            entityManager.AddComponent<Prefab>(prefab);
            // Baked prefabs carry LocalToWorld; firing sets it on instantiation.
            entityManager.AddComponent<LocalToWorld>(prefab);
            var player = entityManager.CreateEntity(
                typeof(Player), typeof(Weapon), typeof(WeaponState), typeof(LocalTransform));
            entityManager.SetComponentData(player, LocalTransform.Identity);
            entityManager.SetComponentData(player, Weapon.Create(prefab, 1f, 20f, 30f, 2f));
            var enemy = CreateEnemy(new float3(4f, 0f, 0f), 3);

            Tick(0.25f);
            using var projectiles = entityManager.CreateEntityQuery(typeof(Projectile), typeof(ProjectileCombat));
            Assert.That(projectiles.CalculateEntityCount(), Is.EqualTo(1));
            Assert.That(projectiles.GetSingleton<ProjectileCombat>().Damage, Is.EqualTo(3));
            Assert.That(projectiles.GetSingleton<ProjectileCombat>().Radius, Is.EqualTo(0.125f));
            AssertHealth(enemy, 3);
            entityManager.DestroyEntity(player);

            Tick(0.25f);

            Assert.That(entityManager.Exists(enemy), Is.False);
            AssertNoProjectiles();
            Assert.That(entityManager.Exists(prefab), Is.True);
        }

        [Test]
        public void GridHit_MatchesIndependentBruteForceOracle()
        {
            var random = Random.CreateFromIndex(482u);
            var enemies = new Entity[512];
            for (var index = 0; index < enemies.Length; index++)
            {
                var point = random.NextFloat2(new float2(-40f), new float2(40f));
                enemies[index] = CreateEnemy(new float3(point.x, 0f, point.y));
            }

            gridSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            ref var grid = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(gridSystem);
            var transforms = endSimulation.GetComponentLookup<LocalTransform>(true);
            var healths = endSimulation.GetComponentLookup<Health>(true);
            var buffers = endSimulation.GetBufferLookup<DamageEvent>(true);

            for (var sample = 0; sample < 64; sample++)
            {
                var start = random.NextFloat2(new float2(-45f), new float2(45f));
                var end = random.NextFloat2(new float2(-45f), new float2(45f));
                var radius = random.NextFloat(0f, 2f);
                var expected = Entity.Null;
                var bestFraction = double.PositiveInfinity;
                foreach (var enemy in enemies)
                {
                    // Independent projection/chord calculation in double precision.
                    var toCenter = (double2)(transforms[enemy].Position.xz - start);
                    var step = (double2)(end - start);
                    var length = math.length(step);
                    var direction = step / length;
                    var along = math.dot(toCenter, direction);
                    var perpendicularSq = math.lengthsq(toCenter - along * direction);
                    var combinedRadius = (double)radius + Enemy.CollisionRadius;
                    var radiusSq = combinedRadius * combinedRadius;
                    if (perpendicularSq > radiusSq)
                    {
                        continue;
                    }

                    var halfChord = math.sqrt(radiusSq - perpendicularSq);
                    if (along + halfChord < 0d || along - halfChord > length)
                    {
                        continue;
                    }

                    var fraction = math.max(0d, (along - halfChord) / length);
                    if (fraction < bestFraction
                        || (fraction == bestFraction && (expected == Entity.Null || enemy.Index < expected.Index)))
                    {
                        expected = enemy;
                        bestFraction = fraction;
                    }
                }

                var actual = ProjectileHitSystem.FindFirstEnemy(grid.Grid, transforms, healths, buffers,
                    new float3(start.x, 0f, start.y), new float3(end.x, 0f, end.y), radius);
                Assert.That(actual, Is.EqualTo(expected), $"Sweep sample {sample}");
            }
        }

        private Entity CreateEnemy(float3 position, int health = 3)
        {
            var entity = entityManager.CreateEntity(typeof(Enemy), typeof(LocalTransform), typeof(Health));
            entityManager.SetComponentData(entity, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(entity, new Health { Current = health });
            entityManager.AddBuffer<DamageEvent>(entity);
            return entity;
        }

        private Entity CreateProjectile(float3 position, float3 velocity, float lifetime = 2f, int damage = 1)
        {
            var entity = entityManager.CreateEntity(typeof(Projectile), typeof(ProjectileCombat), typeof(LocalTransform));
            entityManager.SetComponentData(entity, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(entity, new Projectile { Velocity = velocity, LifetimeRemaining = lifetime });
            entityManager.SetComponentData(entity, ProjectileCombat.Create(damage, 0.125f));
            return entity;
        }

        private void Tick(float deltaTime)
        {
            world.SetTime(new TimeData(world.Time.ElapsedTime + math.max(0f, deltaTime), deltaTime));
            simulation.Update();
        }

        private void AssertHealth(Entity entity, int expected)
        {
            Assert.That(entityManager.GetComponentData<Health>(entity).Current, Is.EqualTo(expected));
        }

        private void AssertNoProjectiles()
        {
            using var query = entityManager.CreateEntityQuery(typeof(Projectile));
            Assert.That(query.CalculateEntityCount(), Is.Zero);
        }
    }
}
