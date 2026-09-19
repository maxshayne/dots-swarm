using DotsSwarm.Core;
using DotsSwarm.Gameplay;
using DotsSwarm.Spatial;
using DotsSwarm.Spawning;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Tests.Gameplay
{
    // Whole sessions through the sorted pipeline with the shipped default tuning.
    // They pin the balance contract; a human playthrough still judges the feel.
    [Category("Balance")]
    public sealed class SurvivalBalanceTests
    {
        private const float FrameTime = 1f / 60f;
        private const float OrbitRadius = 30f;

        private World world;
        private EntityManager manager;
        private SimulationSystemGroup simulation;
        private Entity sessionEntity;
        private Entity player;
        private Entity input;
        private Entity spawner;
        private EntityQuery enemies;

        [SetUp]
        public void SetUp()
        {
            world = new World("Survival balance tests");
            manager = world.EntityManager;
            simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<GameSessionStartSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<SpawnSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<EnemyMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<EnemySpatialGridSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<WeaponSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileHitSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerContactDamageSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<DamageSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<DeathSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<GameSessionEndSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<TransformSystemGroup>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
            simulation.SortSystems();

            sessionEntity = manager.CreateEntity(typeof(GameSession), typeof(GameConfig), typeof(BenchmarkState));
            manager.SetComponentData(sessionEntity, GameConfig.FromArenaSize(new float2(GameConfig.DefaultArenaSize)));
            input = manager.CreateEntity(typeof(PlayerInput));

            // Baked prefabs carry LocalToWorld; spawns and shots set it on instantiation.
            var projectilePrefab = manager.CreateEntity(typeof(Prefab), typeof(Projectile),
                typeof(ProjectileCombat), typeof(LocalTransform), typeof(LocalToWorld));
            manager.SetComponentData(projectilePrefab, LocalTransform.Identity);
            manager.SetComponentData(projectilePrefab,
                ProjectileCombat.Create(ProjectileCombat.DefaultDamage, ProjectileCombat.DefaultRadius));

            player = manager.CreateEntity(typeof(Player), typeof(Health), typeof(LocalTransform),
                typeof(PlayerContactDamage), typeof(PlayerContactState), typeof(Weapon), typeof(WeaponState));
            manager.SetComponentData(player, new Player { MovementSpeed = PlayerAuthoring.DefaultMovementSpeed });
            manager.SetComponentData(player, Health.Create(Health.DefaultPlayerHealth));
            manager.SetComponentData(player, LocalTransform.FromPosition(new float3(0f, 0.5f, 0f)));
            manager.SetComponentData(player,
                PlayerContactDamage.Create(PlayerContactDamage.DefaultDamage, PlayerContactDamage.DefaultInterval));
            manager.SetComponentData(player, Weapon.Create(projectilePrefab, Weapon.DefaultCooldown,
                Weapon.DefaultRange, Weapon.DefaultProjectileSpeed, Weapon.DefaultProjectileLifetime));
            manager.AddBuffer<DamageEvent>(player);

            var enemyPrefab = manager.CreateEntity(typeof(Prefab), typeof(Enemy), typeof(Health), typeof(LocalTransform),
                typeof(LocalToWorld));
            manager.SetComponentData(enemyPrefab, Enemy.Create(Enemy.DefaultMovementSpeed));
            manager.SetComponentData(enemyPrefab, Health.Create(Health.DefaultEnemyHealth));
            manager.SetComponentData(enemyPrefab, LocalTransform.FromPosition(new float3(0f, 0.5f, 0f)));
            manager.AddBuffer<DamageEvent>(enemyPrefab);

            spawner = manager.CreateEntity(typeof(SpawnConfig), typeof(SpawnState));
            manager.SetComponentData(spawner, DefaultSpawnConfig(enemyPrefab));
            enemies = manager.CreateEntityQuery(typeof(Enemy));
        }

        [TearDown]
        public void TearDown() => world.Dispose();

        [Test]
        public void IdlePlayer_IsOverrunWithinFirstMinute()
        {
            while (Session.Status == SessionStatus.Running) Tick();

            TestContext.WriteLine($"Idle player: {Session.Status} at {Session.Elapsed:F2} s.");
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Lost));
            // The opening waves arrive after the first enemies cross the spawn radius.
            Assert.That(Session.Elapsed, Is.InRange(20f, 60f));
        }

        [Test]
        public void OrbitWithoutDodging_LosesBeforeDeadline()
        {
            while (Session.Status == SessionStatus.Running)
            {
                SteerAlongOrbit();
                Tick();
            }

            TestContext.WriteLine($"Orbit without dodging: {Session.Status} at {Session.Elapsed:F2} s.");
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Lost),
                "Running laps without dodging must not beat the swarm.");
        }

        [Test]
        public void FullSession_FollowsSpawnCurveToSwarmCapAndWinsAtDeadline()
        {
            // Survival rules stay on; only the health pool is large enough to reach the finale.
            manager.SetComponentData(player, Health.Create(int.MaxValue));
            var config = manager.GetComponentData<SpawnConfig>(spawner);
            var capReachedAt = -1f;
            while (Session.Status == SessionStatus.Running)
            {
                SteerAlongOrbit();
                Tick();
                var count = enemies.CalculateEntityCount();
                Assert.That(count, Is.LessThanOrEqualTo(config.MaxEnemies));
                if (capReachedAt >= 0f)
                    continue;
                if (count == config.MaxEnemies)
                {
                    capReachedAt = Session.Elapsed;
                    continue;
                }

                // Until the cap binds, the pipeline spawns exactly the integrated schedule.
                var spawnState = manager.GetComponentData<SpawnState>(spawner);
                Assert.That((int)spawnState.SpawnSequence, Is.EqualTo(
                    (int)math.floor(config.CalculateScheduledSpawns(spawnState.Elapsed))).Within(1));
            }

            var spawned = manager.GetComponentData<SpawnState>(spawner).SpawnSequence;
            TestContext.WriteLine($"Full session: {Session.Status}, cap at {capReachedAt:F2} s, " +
                $"{spawned} spawned, {enemies.CalculateEntityCount()} alive.");
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(Session.Elapsed, Is.EqualTo(GameSession.Duration));
            Assert.That(capReachedAt, Is.InRange(GameSession.Duration - 30f, GameSession.Duration - 10f));
            // Kills from the final update are replaced on the next one.
            Assert.That(enemies.CalculateEntityCount(), Is.InRange(config.MaxEnemies - 5, config.MaxEnemies));
            Assert.That(spawned, Is.GreaterThan((uint)config.MaxEnemies), "Kills are replaced while the swarm is capped.");
        }

        private static SpawnConfig DefaultSpawnConfig(Entity enemyPrefab) => SpawnConfig.Create(enemyPrefab,
                SpawnConfig.DefaultSpawnRadius, SpawnConfig.DefaultInitialSpawnRate,
                SpawnConfig.DefaultMaxEnemies, SpawnConfig.DefaultRandomSeed)
            .WithRamp(SpawnConfig.DefaultPeakSpawnRate, SpawnConfig.DefaultRampDuration,
                SpawnConfig.DefaultRampExponent);

        private GameSession Session => manager.GetComponentData<GameSession>(sessionEntity);

        // Counter-clockwise laps around the arena centre, correcting toward the orbit radius.
        private void SteerAlongOrbit()
        {
            var position = manager.GetComponentData<LocalTransform>(player).Position.xz;
            var distance = math.length(position);
            var move = new float2(1f, 0f);
            if (distance > 0.001f)
            {
                var outward = position / distance;
                move = math.normalize(new float2(-outward.y, outward.x) + outward * (OrbitRadius - distance) * 0.3f);
            }

            manager.SetComponentData(input, new PlayerInput { Move = move });
        }

        private void Tick()
        {
            world.SetTime(new TimeData(world.Time.ElapsedTime + FrameTime, FrameTime));
            simulation.Update();
            manager.CompleteAllTrackedJobs();
        }
    }
}
