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
    public sealed class GameSessionTests
    {
        private World world;
        private EntityManager manager;
        private SimulationSystemGroup simulation;
        private Entity sessionEntity;
        private Entity player;
        private Entity input;
        private Entity spawner;
        private Entity enemyPrefab;
        private Entity projectilePrefab;
        private SystemHandle grid;

        [SetUp]
        public void SetUp()
        {
            world = new World("Game session tests");
            manager = world.EntityManager;
            simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            // Reverse registration verifies ordering in the complete pipeline.
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystemManaged<TransformSystemGroup>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<GameSessionEndSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<DeathSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<DamageSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerContactDamageSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<ProjectileHitSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<WeaponSystem>());
            grid = world.GetOrCreateSystem<EnemySpatialGridSystem>();
            simulation.AddSystemToUpdateList(grid);
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<EnemyMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<PlayerMovementSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<SpawnSystem>());
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<GameSessionStartSystem>());
            simulation.SortSystems();

            sessionEntity = manager.CreateEntity(typeof(GameSession), typeof(GameConfig), typeof(BenchmarkState));
            manager.SetComponentData(sessionEntity, GameConfig.FromArenaSize(new float2(100f)));
            player = manager.CreateEntity(typeof(Player), typeof(Health), typeof(LocalTransform),
                typeof(PlayerContactDamage), typeof(PlayerContactState), typeof(WeaponState));
            manager.SetComponentData(player, new Player { MovementSpeed = 10f });
            manager.SetComponentData(player, Health.Create(10));
            manager.SetComponentData(player, LocalTransform.FromPosition(new float3(2f, 0.5f, 3f)));
            manager.SetComponentData(player, PlayerContactDamage.Create(1, 0.5f));
            manager.AddBuffer<DamageEvent>(player);
            input = manager.CreateEntity(typeof(PlayerInput));

            enemyPrefab = manager.CreateEntity(typeof(Prefab), typeof(Enemy), typeof(Health), typeof(LocalTransform));
            manager.SetComponentData(enemyPrefab, Enemy.Create(2f));
            manager.SetComponentData(enemyPrefab, Health.Create(3));
            manager.SetComponentData(enemyPrefab, LocalTransform.Identity);
            manager.AddBuffer<DamageEvent>(enemyPrefab);
            projectilePrefab = manager.CreateEntity(typeof(Prefab), typeof(Projectile), typeof(ProjectileCombat), typeof(LocalTransform));
            manager.SetComponentData(projectilePrefab, LocalTransform.Identity);
            manager.SetComponentData(projectilePrefab, ProjectileCombat.Create(1, 0.125f));

            spawner = manager.CreateEntity(typeof(SpawnConfig), typeof(SpawnState));
            manager.SetComponentData(spawner, SpawnConfig.Create(enemyPrefab, 40f, 0f, 20000, 42u));
        }

        [TearDown]
        public void TearDown() => world.Dispose();

        [Test]
        public void Timer_WinsAtThreeMinutesAndClampsLongFrame()
        {
            Tick(179f);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Running));
            Tick(0.5f);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Running));
            Tick(100f);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(Session.Elapsed, Is.EqualTo(180f));
            Tick(1f);
            Assert.That(Session.Elapsed, Is.EqualTo(180f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void Timer_NonpositiveDeltaDoesNotAdvance(float delta)
        {
            Tick(1f);
            Tick(delta);
            Assert.That(Session.Elapsed, Is.EqualTo(1f));
        }

        [Test]
        public void Timer_WaitsForBakedPlayerBeforeStarting()
        {
            manager.AddComponent<Disabled>(player);
            Tick(15f);
            Assert.That(Session.Initialized, Is.False);
            Assert.That(Session.Elapsed, Is.Zero);
            manager.RemoveComponent<Disabled>(player);
            Tick(1f);
            Assert.That(Session.Elapsed, Is.EqualTo(1f));
        }

        [TestCase(0f)]
        [TestCase(179.5f)]
        public void LethalContact_LosesAfterDamageEvenAtVictoryDeadline(float elapsed)
        {
            Tick(elapsed);
            manager.SetComponentData(player, Health.Create(1));
            var enemy = manager.Instantiate(enemyPrefab);
            manager.SetComponentData(enemy, manager.GetComponentData<LocalTransform>(player));
            Tick(0.5f);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Lost));
            Assert.That(manager.GetComponentData<Health>(player).Current, Is.Zero);
            Assert.That(manager.Exists(player), Is.True);
        }

        [TestCase(SessionStatus.Won)]
        [TestCase(SessionStatus.Lost)]
        public void TerminalState_FreezesEveryGameplayProducer(SessionStatus outcome)
        {
            Tick(0f);
            var session = Session;
            session.Status = outcome;
            manager.SetComponentData(sessionEntity, session);
            SetSpawnRate(10f);
            EquipWeapon();
            manager.SetComponentData(input, new PlayerInput { Move = new float2(1f, 0f) });
            var enemy = manager.Instantiate(enemyPrefab);
            manager.SetComponentData(enemy, LocalTransform.FromPosition(new float3(3f, 0.5f, 3f)));
            var deadEnemy = manager.Instantiate(enemyPrefab);
            manager.SetComponentData(deadEnemy, new Health());
            var projectile = manager.Instantiate(projectilePrefab);
            manager.SetComponentData(projectile, new Projectile { Velocity = new float3(10f, 0f, 0f), LifetimeRemaining = 0.25f });
            manager.GetBuffer<DamageEvent>(player).Add(new DamageEvent { Amount = 10 });
            manager.SetComponentData(player, new WeaponState { CooldownRemaining = 0.1f });
            manager.SetComponentData(player, new PlayerContactState { CooldownRemaining = 0.2f });
            var initial = manager.GetComponentData<LocalTransform>(player);
            Tick(10f);
            Tick(10f);
            Assert.That(Session.Status, Is.EqualTo(outcome));
            Assert.That(Session.Elapsed, Is.Zero);
            Assert.That(manager.GetComponentData<LocalTransform>(player).Position, Is.EqualTo(initial.Position));
            Assert.That(manager.GetComponentData<LocalTransform>(enemy).Position, Is.EqualTo(new float3(3f, 0.5f, 3f)));
            Assert.That(manager.GetComponentData<LocalTransform>(projectile).Position, Is.EqualTo(float3.zero));
            Assert.That(manager.GetComponentData<Projectile>(projectile).LifetimeRemaining, Is.EqualTo(0.25f));
            Assert.That(manager.GetComponentData<Health>(player).Current, Is.EqualTo(10));
            Assert.That(manager.GetComponentData<WeaponState>(player).CooldownRemaining, Is.EqualTo(0.1f));
            Assert.That(manager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.EqualTo(0.2f));
            Assert.That(manager.Exists(deadEnemy), Is.True);
            Assert.That(Count<Enemy>(), Is.EqualTo(2));
            Assert.That(Count<Projectile>(), Is.EqualTo(1));
            Assert.That(manager.GetComponentData<SpawnState>(spawner).SpawnSequence, Is.Zero);
        }

        [TestCase(SessionStatus.Running)]
        [TestCase(SessionStatus.Won)]
        [TestCase(SessionStatus.Lost)]
        public void Restart_RestoresEntireSessionAndPreservesPrefabs(SessionStatus outcome)
        {
            var initial = manager.GetComponentData<LocalTransform>(player);
            Tick(0f);
            using var enemies = manager.Instantiate(enemyPrefab, 10000, Allocator.Temp);
            using var projectiles = manager.Instantiate(projectilePrefab, 10000, Allocator.Temp);
            // Include disabled entities in the cleanup contract.
            manager.AddComponent<Disabled>(enemies[0]);
            manager.AddComponent<Disabled>(projectiles[0]);
            // Leave an outstanding grid job; reset must respect its dependencies.
            grid.Update(world.Unmanaged);
            var session = Session;
            session.Status = outcome;
            session.Elapsed = 120f;
            session.RestartRequested = true;
            manager.SetComponentData(sessionEntity, session);
            manager.SetComponentData(player, LocalTransform.FromPosition(new float3(20f)));
            manager.SetComponentData(player, new Health());
            manager.SetComponentData(player, new WeaponState { CooldownRemaining = 1f });
            manager.SetComponentData(player, new PlayerContactState { CooldownRemaining = 1f });
            manager.GetBuffer<DamageEvent>(player).Add(new DamageEvent { Amount = 100 });
            manager.SetComponentData(input, new PlayerInput { Move = new float2(1f) });
            manager.SetComponentData(spawner, new SpawnState { SpawnBudget = 0.9f, Elapsed = 50f, SpawnSequence = 100u });
            SetSpawnRate(20f);
            EquipWeapon();
            Tick(1f);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Running));
            Assert.That(Session.Elapsed, Is.Zero);
            Assert.That(Session.RestartRequested, Is.False);
            Assert.That(Count<Enemy>(), Is.Zero);
            Assert.That(Count<Projectile>(), Is.Zero);
            Assert.That(manager.Exists(enemies[0]), Is.False);
            Assert.That(manager.Exists(projectiles[0]), Is.False);
            Assert.That(manager.GetComponentData<LocalTransform>(player), Is.EqualTo(initial));
            Assert.That(manager.GetComponentData<Health>(player).Current, Is.EqualTo(10));
            Assert.That(manager.GetComponentData<WeaponState>(player).CooldownRemaining, Is.Zero);
            Assert.That(manager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.Zero);
            Assert.That(manager.GetComponentData<PlayerInput>(input).Move, Is.EqualTo(float2.zero));
            Assert.That(manager.GetBuffer<DamageEvent>(player).Length, Is.Zero);
            Assert.That(manager.GetComponentData<SpawnState>(spawner).SpawnBudget, Is.Zero);
            Assert.That(manager.GetComponentData<SpawnState>(spawner).SpawnSequence, Is.Zero);
            Assert.That(manager.GetComponentData<SpawnState>(spawner).Elapsed, Is.Zero, "Restart rewinds the spawn curve.");
            ref var gridSystem = ref world.Unmanaged.GetUnsafeSystemRef<EnemySpatialGridSystem>(grid);
            gridSystem.GridDependency.Complete();
            Assert.That(gridSystem.Grid.Count(), Is.Zero);
            Assert.That(manager.Exists(enemyPrefab), Is.True);
            Assert.That(manager.Exists(projectilePrefab), Is.True);
            Assert.That(manager.GetComponentData<Health>(enemyPrefab).Current, Is.EqualTo(3));
            Tick(0.1f);
            Assert.That(Count<Enemy>(), Is.EqualTo(2));
            Assert.That(Session.Elapsed, Is.EqualTo(0.1f));
        }

        [Test]
        public void Restart_CleansCommandsFromFinishingFrameAndRepeatsSeed()
        {
            SetSpawnRate(1f);
            Tick(1f);
            using var enemyQuery = manager.CreateEntityQuery(typeof(Enemy), typeof(LocalTransform));
            var original = manager.GetComponentData<LocalTransform>(enemyQuery.GetSingletonEntity()).Position;
            EquipWeapon();
            var session = Session;
            session.Elapsed = 179f;
            manager.SetComponentData(sessionEntity, session);
            Tick(1f); // Spawn/weapon ECB plays back after the transition to Won.
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Won));
            Assert.That(Count<Enemy>(), Is.EqualTo(2));
            Assert.That(Count<Projectile>(), Is.EqualTo(1));
            for (var cycle = 0; cycle < 3; cycle++)
            {
                session = Session;
                session.RestartRequested = true;
                manager.SetComponentData(sessionEntity, session);
                Tick(1f);
                Assert.That(Count<Enemy>(), Is.Zero);
                Assert.That(Count<Projectile>(), Is.Zero);
                Tick(1f);
                Assert.That(manager.GetComponentData<LocalTransform>(enemyQuery.GetSingletonEntity()).Position,
                    Is.EqualTo(original));
            }
        }

        [TestCase(BenchmarkPreset.Swarm1K)]
        [TestCase(BenchmarkPreset.Swarm10K)]
        [TestCase(BenchmarkPreset.Swarm20K)]
        [TestCase(BenchmarkPreset.Swarm50K)]
        public void Benchmark_FillsExactTargetAndReplenishesLosses(BenchmarkPreset preset)
        {
            RequestPreset(preset);
            Tick(1f / 60f);
            Assert.That(Count<Enemy>(), Is.Zero, "Preset switches use a clean reset frame.");
            Tick(1f / 60f);
            Assert.That(Count<Enemy>(), Is.EqualTo((int)preset));
            using var query = manager.CreateEntityQuery(typeof(Enemy));
            using var entities = query.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < 7; i++) manager.DestroyEntity(entities[i]);
            Tick(1f / 60f);
            Assert.That(Count<Enemy>(), Is.EqualTo((int)preset));
            Tick(1f / 60f);
            Assert.That(Count<Enemy>(), Is.EqualTo((int)preset), "Must not overshoot target.");
            Assert.That(manager.GetComponentData<SpawnState>(spawner).SpawnBudget, Is.Zero);
            Assert.That(manager.Exists(enemyPrefab), Is.True);
        }

        [Test]
        public void Benchmark_SwitchDownThenSurvivalRestoresRulesAndSpawnConfig()
        {
            SetSpawnRate(2f);
            var original = manager.GetComponentData<SpawnConfig>(spawner);
            RequestPreset(BenchmarkPreset.Swarm10K);
            Tick(0f);
            Tick(0.1f);
            RequestPreset(BenchmarkPreset.Swarm1K);
            Tick(0f);
            Assert.That(Count<Enemy>(), Is.Zero);
            Tick(0.1f);
            Assert.That(Count<Enemy>(), Is.EqualTo(1000));
            RequestPreset(BenchmarkPreset.Survival);
            Tick(1f);
            Assert.That(Count<Enemy>(), Is.Zero);
            Assert.That(manager.GetComponentData<SpawnConfig>(spawner), Is.EqualTo(original));
            Tick(1f);
            Assert.That(Count<Enemy>(), Is.EqualTo(2));
            Assert.That(Session.Elapsed, Is.EqualTo(1f));
            manager.GetBuffer<DamageEvent>(player).Add(new DamageEvent { Amount = 10 });
            Tick(0.1f);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Lost));
        }

        [Test]
        public void Benchmark_ProtectsPlayerKeepsCombatAndIgnoresSurvivalDeadline()
        {
            RequestPreset(BenchmarkPreset.Swarm1K);
            Tick(0f);
            Tick(0.1f);
            manager.GetBuffer<DamageEvent>(player).Add(new DamageEvent { Amount = int.MaxValue });
            using var query = manager.CreateEntityQuery(typeof(Enemy));
            using var entities = query.ToEntityArray(Allocator.Temp);
            var victim = entities[0];
            manager.GetBuffer<DamageEvent>(victim).Add(new DamageEvent { Amount = 3 });
            Tick(200f);
            Assert.That(manager.GetComponentData<Health>(player).Current, Is.EqualTo(10));
            Assert.That(manager.GetBuffer<DamageEvent>(player).Length, Is.Zero);
            Assert.That(manager.Exists(victim), Is.False);
            Assert.That(Session.Status, Is.EqualTo(SessionStatus.Running));
            Tick(0.1f);
            Assert.That(Count<Enemy>(), Is.EqualTo(1000));
        }

        [Test]
        public void Benchmark_RestartRepeatsFixedSeedRegardlessOfSurvivalSeed()
        {
            RequestPreset(BenchmarkPreset.Swarm1K);
            Tick(0f);
            Tick(0.1f);
            using var query = manager.CreateEntityQuery(typeof(Enemy), typeof(LocalTransform));
            using var first = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            // Match positions as a set; entity/chunk ordering is not the seed contract.
            var positions = new System.Collections.Generic.HashSet<float3>();
            foreach (var transform in first) positions.Add(transform.Position);
            var config = manager.GetComponentData<SpawnConfig>(spawner);
            config.RandomSeed = 999u;
            manager.SetComponentData(spawner, config);
            var session = Session;
            session.RestartRequested = true;
            manager.SetComponentData(sessionEntity, session);
            Tick(1f);
            Assert.That(manager.GetComponentData<BenchmarkState>(sessionEntity).ActivePreset,
                Is.EqualTo(BenchmarkPreset.Swarm1K));
            Tick(0.1f);
            using var second = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            foreach (var transform in second) Assert.That(positions.Remove(transform.Position), Is.True);
            Assert.That(positions.Count, Is.Zero);
        }

        [Test]
        public void Benchmark_RejectsInvalidPresetWithoutResettingSession()
        {
            Tick(1f);
            RequestPreset((BenchmarkPreset)1234);
            Tick(1f);
            Assert.That(Session.Elapsed, Is.EqualTo(2f));
            var benchmark = manager.GetComponentData<BenchmarkState>(sessionEntity);
            Assert.That(benchmark.ActivePreset, Is.EqualTo(BenchmarkPreset.Survival));
            Assert.That(benchmark.ChangeRequested, Is.False);
        }

        private void RequestPreset(BenchmarkPreset preset) => manager.SetComponentData(sessionEntity,
            new BenchmarkState { RequestedPreset = preset, ChangeRequested = true,
                ActivePreset = manager.GetComponentData<BenchmarkState>(sessionEntity).ActivePreset });

        private GameSession Session => manager.GetComponentData<GameSession>(sessionEntity);

        private void EquipWeapon() => manager.AddComponentData(player,
            Weapon.Create(projectilePrefab, 0.2f, 100f, 30f, 2f));

        private void SetSpawnRate(float rate)
        {
            var config = manager.GetComponentData<SpawnConfig>(spawner);
            config.InitialSpawnRate = rate;
            manager.SetComponentData(spawner, config);
        }

        private int Count<T>() where T : unmanaged, IComponentData
        {
            using var query = manager.CreateEntityQuery(typeof(T));
            return query.CalculateEntityCount();
        }

        private void Tick(float delta)
        {
            world.SetTime(new TimeData(world.Time.ElapsedTime + math.max(0f, delta), delta));
            simulation.Update();
            manager.CompleteAllTrackedJobs();
        }
    }
}
