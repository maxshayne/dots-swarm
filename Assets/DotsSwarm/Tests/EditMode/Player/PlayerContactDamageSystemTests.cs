using DotsSwarm.Core;
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
    public sealed class PlayerContactDamageSystemTests
    {
        private World world;
        private EntityManager entityManager;
        private SimulationSystemGroup simulation;
        private SystemHandle gridSystem;
        private SystemHandle contactSystem;
        private SystemHandle damageSystem;

        [SetUp]
        public void SetUp()
        {
            world = new World("Player contact test");
            entityManager = world.EntityManager;
            simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            gridSystem = world.GetOrCreateSystem<EnemySpatialGridSystem>();
            contactSystem = world.GetOrCreateSystem<PlayerContactDamageSystem>();
            damageSystem = world.GetOrCreateSystem<DamageSystem>();

            // Reverse registration exercises production ordering, including both
            // damage producers sharing the existing combat pipeline.
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<TransformSystemGroup>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<DeathSystem>());
            simulation.AddSystemToUpdateList(damageSystem);
            simulation.AddSystemToUpdateList(contactSystem);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileHitSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<WeaponSystem>());
            simulation.AddSystemToUpdateList(gridSystem);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<EnemyMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<SpawnSystem>());
            simulation.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            world.Dispose();
        }

        [Test]
        public void Settings_ClampInvalidDamageAndInterval()
        {
            var contact = PlayerContactDamage.Create(-10, -1f);
            Assert.That(contact.Damage, Is.Zero);
            Assert.That(contact.Interval, Is.EqualTo(PlayerContactDamage.MinimumInterval));
        }

        [TestCase(0f, 0f, true)]
        [TestCase(0.9f, 0f, true)]
        [TestCase(-0.9f, 0f, true)]
        [TestCase(0f, 0.9f, true)]
        [TestCase(0.901f, 0f, false)]
        [TestCase(0.7f, 0.7f, false)]
        public void Contact_UsesInclusiveCircleOnXZ(float x, float z, bool expectedHit)
        {
            var player = CreatePlayer(float3.zero);
            CreateEnemy(new float3(x, 100f, z));

            Tick(0.125f);

            AssertHealth(player, expectedHit ? 9 : 10);
            Assert.That(entityManager.GetBuffer<DamageEvent>(player).Length, Is.Zero);
        }

        [TestCase(1.75f, 2.25f)]
        [TestCase(-1.75f, -2.25f)]
        [TestCase(-0.25f, 0.25f)]
        public void Contact_SearchesAcrossCellBoundaries(float playerCoordinate, float enemyCoordinate)
        {
            var player = CreatePlayer(new float3(playerCoordinate, 0f, playerCoordinate));
            CreateEnemy(new float3(enemyCoordinate, 0f, enemyCoordinate));

            Tick(0.125f);

            AssertHealth(player, 9);
        }

        [Test]
        public void Contact_TenThousandEnemiesProduceOneBufferedHitPerInterval()
        {
            var player = CreatePlayer(float3.zero, damage: 2);
            var prefab = CreateEnemy(float3.zero);
            entityManager.AddComponent<Prefab>(prefab);
            using var enemies = entityManager.Instantiate(prefab, 10000, Allocator.Temp);
            world.SetTime(new TimeData(0d, 0.125f));

            gridSystem.Update(world.Unmanaged);
            contactSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            AssertHealth(player, 10);
            var events = entityManager.GetBuffer<DamageEvent>(player);
            Assert.That(events.Length, Is.EqualTo(1));
            Assert.That(events[0].Amount, Is.EqualTo(2));
            damageSystem.Update(world.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            AssertHealth(player, 8);

            Tick(0.25f);
            AssertHealth(player, 8);
            Tick(0.25f);
            AssertHealth(player, 6);
            Assert.That(entityManager.GetBuffer<DamageEvent>(player).Length, Is.Zero);
            AssertHealth(prefab, 3);
        }

        [Test]
        public void Contact_NoTargetDoesNotStartCooldownAndLongFramesDoNotBankHits()
        {
            var player = CreatePlayer(float3.zero);
            Tick(20f);
            AssertHealth(player, 10);
            Assert.That(entityManager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.Zero);

            CreateEnemy(float3.zero);
            Tick(20f);
            AssertHealth(player, 9);
            Assert.That(entityManager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.EqualTo(0.5f));
            Tick(0.25f);
            AssertHealth(player, 9);
            Tick(20f);
            AssertHealth(player, 8);
        }

        [Test]
        public void Contact_CooldownSurvivesLeavingAndReenteringContact()
        {
            var player = CreatePlayer(float3.zero);
            var enemy = CreateEnemy(float3.zero);
            Tick(0.125f);
            entityManager.SetComponentData(enemy, LocalTransform.FromPosition(new float3(10f, 0f, 0f)));
            Tick(0.125f);
            entityManager.SetComponentData(enemy, LocalTransform.Identity);
            Tick(0.125f);
            AssertHealth(player, 9);
            Tick(0.25f);
            AssertHealth(player, 8);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void Contact_NonPositiveTimeNeitherDamagesNorTicksCooldown(float deltaTime)
        {
            var player = CreatePlayer(float3.zero);
            CreateEnemy(float3.zero);
            Tick(deltaTime);
            AssertHealth(player, 10);
            Tick(0.125f);
            Tick(deltaTime);
            AssertHealth(player, 9);
            Assert.That(entityManager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.EqualTo(0.5f));
        }

        [Test]
        public void Contact_ZeroDamageDoesNotStartProtection()
        {
            var player = CreatePlayer(float3.zero, damage: 0);
            CreateEnemy(float3.zero);
            Tick(1f);
            AssertHealth(player, 10);
            Assert.That(entityManager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.Zero);
        }

        [Test]
        public void Contact_IgnoresDeadDisabledPrefabAndHealthlessEnemies()
        {
            var player = CreatePlayer(float3.zero);
            var dead = CreateEnemy(float3.zero, health: 0);
            var prefab = CreateEnemy(float3.zero);
            entityManager.AddComponent<Prefab>(prefab);
            var disabled = CreateEnemy(float3.zero);
            entityManager.AddComponent<Disabled>(disabled);
            var healthless = CreateEnemy(float3.zero);
            entityManager.RemoveComponent<Health>(healthless);

            Tick(0.125f);

            AssertHealth(player, 10);
            Assert.That(entityManager.Exists(dead), Is.False);
            Assert.That(entityManager.Exists(prefab), Is.True);
            Assert.That(entityManager.Exists(disabled), Is.True);
        }

        [Test]
        public void Contact_LethalDamageSaturatesAndKeepsPlayerForGameFlow()
        {
            var player = CreatePlayer(float3.zero, damage: int.MaxValue);
            CreateEnemy(float3.zero);
            Tick(0.125f);
            AssertHealth(player, 0);
            Tick(1f);
            AssertHealth(player, 0);
            Assert.That(entityManager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.Zero,
                "Dead players must not receive new contacts or restart protection.");
            Assert.That(entityManager.GetBuffer<DamageEvent>(player).Length, Is.Zero);
        }

        [Test]
        public void Contact_DoesNotModifyPlayerPrefab()
        {
            var prefab = CreatePlayer(float3.zero);
            entityManager.AddComponent<Prefab>(prefab);
            CreateEnemy(float3.zero);
            Tick(1f);
            AssertHealth(prefab, 10);
            Assert.That(entityManager.GetBuffer<DamageEvent>(prefab).Length, Is.Zero);
            Assert.That(entityManager.GetComponentData<PlayerContactState>(prefab).CooldownRemaining, Is.Zero);
        }

        [Test]
        public void Contact_UsesPlayerPositionAfterMovement()
        {
            var player = CreatePlayer(new float3(5f, 0f, 0f));
            entityManager.SetComponentData(player, new Player { MovementSpeed = 5f });
            var input = entityManager.CreateEntity(typeof(PlayerInput));
            entityManager.SetComponentData(input, new PlayerInput { Move = new float2(-1f, 0f) });
            var config = entityManager.CreateEntity(typeof(GameConfig));
            entityManager.SetComponentData(config, new GameConfig { ArenaHalfExtents = new float2(100f) });
            CreateEnemy(float3.zero);

            Tick(1f);

            Assert.That(entityManager.GetComponentData<LocalTransform>(player).Position, Is.EqualTo(float3.zero));
            AssertHealth(player, 9);
        }

        [Test]
        public void Contact_UsesEnemyPositionAfterMovementAndGridRebuild()
        {
            var player = CreatePlayer(float3.zero);
            var enemy = CreateEnemy(new float3(1.5f, 0f, 0f));
            entityManager.SetComponentData(enemy, Enemy.Create(1f));

            Tick(1f);

            AssertHealth(player, 9);
        }

        [Test]
        public void Contact_AndLethalProjectileApplyInSameDamagePass()
        {
            var player = CreatePlayer(float3.zero);
            var enemy = CreateEnemy(float3.zero);
            var projectile = entityManager.CreateEntity(typeof(Projectile), typeof(ProjectileCombat), typeof(LocalTransform));
            entityManager.SetComponentData(projectile, LocalTransform.Identity);
            entityManager.SetComponentData(projectile, new Projectile { LifetimeRemaining = 1f });
            entityManager.SetComponentData(projectile, ProjectileCombat.Create(3, 0.125f));

            Tick(0.125f);

            AssertHealth(player, 9);
            Assert.That(entityManager.Exists(enemy), Is.False);
            Assert.That(entityManager.Exists(projectile), Is.False);
            Tick(1f);
            AssertHealth(player, 9);
        }

        [Test]
        public void Grid_RebuildWaitsForContactReaderAndHandlesRemovedEnemyAndPlayer()
        {
            var player = CreatePlayer(float3.zero);
            var enemy = CreateEnemy(float3.zero);
            world.SetTime(new TimeData(0d, 0.125f));
            for (var frame = 0; frame < 4; frame++)
            {
                gridSystem.Update(world.Unmanaged);
                contactSystem.Update(world.Unmanaged);
                damageSystem.Update(world.Unmanaged);
            }

            entityManager.CompleteAllTrackedJobs();
            AssertHealth(player, 9);
            entityManager.DestroyEntity(enemy);
            Tick(1f);
            AssertHealth(player, 9);
            entityManager.DestroyEntity(player);
            Tick(1f);
        }

        [Test]
        public void Contact_WithoutGridIsSafe()
        {
            using var emptyWorld = new World("No grid");
            var manager = emptyWorld.EntityManager;
            var player = manager.CreateEntity(typeof(Player), typeof(PlayerContactDamage), typeof(PlayerContactState),
                typeof(Health), typeof(LocalTransform));
            manager.AddBuffer<DamageEvent>(player);
            manager.SetComponentData(player, Health.Create(10));
            manager.SetComponentData(player, PlayerContactDamage.Create(1, 0.5f));
            emptyWorld.SetTime(new TimeData(1d, 1f));

            emptyWorld.GetOrCreateSystem<PlayerContactDamageSystem>().Update(emptyWorld.Unmanaged);

            Assert.That(manager.GetComponentData<Health>(player).Current, Is.EqualTo(10));
        }

        private Entity CreatePlayer(float3 position, int damage = 1)
        {
            var player = entityManager.CreateEntity(typeof(Player), typeof(LocalTransform), typeof(Health),
                typeof(PlayerContactDamage), typeof(PlayerContactState));
            entityManager.SetComponentData(player, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(player, Health.Create(10));
            entityManager.SetComponentData(player, PlayerContactDamage.Create(damage, 0.5f));
            entityManager.AddBuffer<DamageEvent>(player);
            return player;
        }

        private Entity CreateEnemy(float3 position, int health = 3)
        {
            var enemy = entityManager.CreateEntity(typeof(Enemy), typeof(LocalTransform), typeof(Health));
            entityManager.SetComponentData(enemy, LocalTransform.FromPosition(position));
            entityManager.SetComponentData(enemy, new Health { Current = health });
            entityManager.AddBuffer<DamageEvent>(enemy);
            return enemy;
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
    }
}
