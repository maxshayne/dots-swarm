using System.Collections;
using System.Collections.Generic;
using DotsSwarm.Gameplay;
using DotsSwarm.Spawning;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace DotsSwarm.Tests.Integration
{
    // End-to-end survival sessions in the shipped Main scene. Input comes from a
    // virtual keyboard through the real bridges, so the tests follow the player's
    // path; the Edit Mode suites keep the per-system edge cases.
    [Category("Integration")]
    [Timeout(600000)]
    public sealed class SurvivalPipelineTests
    {
        private const float FrameTime = MainSceneSession.FrameTime;
        private const float Tolerance = 1e-3f;
        // A queued key event reaches the ECS singleton within two updates.
        private const int InputLatency = 2;

        private readonly InputTestFixture input = new InputTestFixture();
        private readonly MainSceneSession main = new MainSceneSession();
        private Keyboard keyboard;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // The fixture turns on stack-trace leak tracking for every native
            // allocation; keep the project's mode so frames stay cheap.
            var leakMode = NativeLeakDetection.Mode;
            input.Setup();
            NativeLeakDetection.Mode = leakMode;
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return main.Load();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return main.Unload();
            input.TearDown();
        }

        [UnityTest]
        public IEnumerator Main_StartsSurvivalFromBakedDefaults()
        {
            var manager = main.Manager;
            var session = main.Session;
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Running));
            Assert.That(main.Benchmark.ActivePreset, Is.EqualTo(BenchmarkPreset.Survival));
            Assert.That(main.ArenaHalfExtents, Is.EqualTo(new float2(50f)));

            var player = main.Player;
            Assert.That(session.InitialPlayerTransform.Position, Is.EqualTo(new float3(0f, 0.5f, 0f)));
            Assert.That(session.InitialPlayerHealth.Current, Is.EqualTo(Health.DefaultPlayerHealth));
            Assert.That(manager.GetComponentData<Player>(player).MovementSpeed,
                Is.EqualTo(PlayerAuthoring.DefaultMovementSpeed));
            var contact = manager.GetComponentData<PlayerContactDamage>(player);
            Assert.That(contact.Damage, Is.EqualTo(PlayerContactDamage.DefaultDamage));
            Assert.That(contact.Interval, Is.EqualTo(PlayerContactDamage.DefaultInterval));
            Assert.That(manager.HasBuffer<DamageEvent>(player), Is.True);

            var weapon = manager.GetComponentData<Weapon>(player);
            Assert.That(weapon.Cooldown, Is.EqualTo(Weapon.DefaultCooldown));
            Assert.That(weapon.Range, Is.EqualTo(Weapon.DefaultRange));
            Assert.That(weapon.ProjectileSpeed, Is.EqualTo(Weapon.DefaultProjectileSpeed));
            Assert.That(weapon.ProjectileLifetime, Is.EqualTo(Weapon.DefaultProjectileLifetime));
            AssertRenderablePrefab(weapon.ProjectilePrefab);
            var combat = manager.GetComponentData<ProjectileCombat>(weapon.ProjectilePrefab);
            Assert.That(combat.Damage, Is.EqualTo(ProjectileCombat.DefaultDamage));
            Assert.That(combat.Radius, Is.EqualTo(ProjectileCombat.DefaultRadius));

            var config = main.SpawnConfig;
            Assert.That(config.SpawnRadius, Is.EqualTo(SpawnConfig.DefaultSpawnRadius));
            Assert.That(config.InitialSpawnRate, Is.EqualTo(SpawnConfig.DefaultInitialSpawnRate));
            Assert.That(config.PeakSpawnRate, Is.EqualTo(SpawnConfig.DefaultPeakSpawnRate));
            Assert.That(config.RampDuration, Is.EqualTo(SpawnConfig.DefaultRampDuration));
            Assert.That(config.RampExponent, Is.EqualTo(SpawnConfig.DefaultRampExponent));
            Assert.That(config.MaxEnemies, Is.EqualTo(SpawnConfig.DefaultMaxEnemies));
            Assert.That(config.RandomSeed, Is.EqualTo(SpawnConfig.DefaultRandomSeed));
            AssertRenderablePrefab(config.EnemyPrefab);
            Assert.That(manager.GetComponentData<Enemy>(config.EnemyPrefab).MovementSpeed,
                Is.EqualTo(Enemy.DefaultMovementSpeed));
            Assert.That(manager.GetComponentData<Health>(config.EnemyPrefab).Current,
                Is.EqualTo(Health.DefaultEnemyHealth));
            Assert.That(manager.HasBuffer<DamageEvent>(config.EnemyPrefab), Is.True);

            // The bridge shows the HUD once the session has initialized.
            yield return null;
            Assert.That(main.HudCounters.gameObject.activeInHierarchy, Is.True);
            StringAssert.StartsWith("SURVIVAL", main.HudCounters.text);
            StringAssert.Contains($"HP {Health.DefaultPlayerHealth}\n", main.HudCounters.text);
            Assert.That(main.ResultPanel.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator HeldKeys_MovePlayerThroughInputBridgeAndStopAtArenaWall()
        {
            var speed = main.Manager.GetComponentData<Player>(main.Player).MovementSpeed;
            var step = speed * FrameTime;

            yield return Press(keyboard.dKey);
            yield return main.Frames(InputLatency);
            var from = main.PlayerPosition;
            yield return main.Frames(30);
            var moved = main.PlayerPosition - from;
            Assert.That(moved.x, Is.EqualTo(30 * step).Within(Tolerance), "D moves one full step per update.");
            Assert.That(math.length(moved.yz), Is.LessThan(1e-5f));

            // Diagonal input keeps the same speed.
            yield return Press(keyboard.wKey);
            yield return main.Frames(InputLatency);
            from = main.PlayerPosition;
            yield return main.Frames(30);
            moved = main.PlayerPosition - from;
            Assert.That(math.length(moved.xz), Is.EqualTo(30 * step).Within(Tolerance));
            Assert.That(moved.z, Is.EqualTo(moved.x).Within(Tolerance));
            Assert.That(moved.y, Is.Zero);

            yield return Release(keyboard.dKey);
            yield return Release(keyboard.wKey);
            yield return main.Frames(InputLatency);
            from = main.PlayerPosition;
            yield return main.Frames(10);
            Assert.That(main.PlayerPosition, Is.EqualTo(from), "Released keys stop the player.");

            // Holding into a wall clamps the player at the arena edge.
            var wall = -main.ArenaHalfExtents.x;
            yield return Press(keyboard.aKey);
            yield return main.RunUntil(() => main.PlayerPosition.x <= wall,
                (from.x - wall) / speed + 1f, "A did not reach the west wall.");
            yield return main.Frames(30);
            yield return Release(keyboard.aKey);
            Assert.That(main.PlayerPosition.x, Is.EqualTo(wall));
            Assert.That(main.PlayerPosition.z, Is.EqualTo(from.z).Within(1e-5f));
            Assert.That(main.Session.Status, Is.EqualTo(SessionStatus.Running));
        }

        [UnityTest]
        public IEnumerator Spawner_PlacesScheduledEnemiesOnRingAndRendersThemWhereSimulated()
        {
            var manager = main.Manager;
            var config = main.SpawnConfig;
            var prefab = manager.GetComponentData<LocalTransform>(config.EnemyPrefab);
            var center = main.PlayerPosition;
            var step = manager.GetComponentData<Enemy>(config.EnemyPrefab).MovementSpeed * FrameTime;
            var known = new Dictionary<Entity, float3>();
            var sequence = main.SpawnState.SpawnSequence;
            using (var existing = main.Enemies.ToEntityArray(Allocator.Temp))
            {
                foreach (var enemy in existing)
                    known[enemy] = manager.GetComponentData<LocalTransform>(enemy).Position;
            }

            // The first enemy enters weapon range after about nine seconds, so up
            // to eight seconds every spawned enemy is still alive.
            var spawned = 0;
            for (var frame = 0; main.SpawnState.Elapsed < 8f; frame++)
            {
                if (frame >= MainSceneSession.FramesFor(9f)) Assert.Fail("The spawn clock stalled.");
                yield return null;
                var spawnState = main.SpawnState;
                var scheduled = (int)math.floor(config.CalculateScheduledSpawns(spawnState.Elapsed));
                Assert.That((int)spawnState.SpawnSequence, Is.InRange(scheduled - 1, scheduled),
                    "Spawns follow the integrated ramp.");
                Assert.That(main.Enemies.CalculateEntityCount(), Is.EqualTo((int)spawnState.SpawnSequence),
                    "Every spawned enemy is alive and active.");

                var expected = ExpectedSpawns(center, sequence, spawnState.SpawnSequence);
                using var enemies = main.Enemies.ToEntityArray(Allocator.Temp);
                foreach (var enemy in enemies)
                {
                    var transform = manager.GetComponentData<LocalTransform>(enemy);
                    // Includes the first frame after ECB playback: no pop-in at the prefab origin.
                    AssertNear(manager.GetComponentData<LocalToWorld>(enemy).Position, transform.Position,
                        $"Enemy {enemy} renders away from its simulated position");
                    if (known.TryGetValue(enemy, out var previous))
                    {
                        var closing = math.distance(previous.xz, center.xz) - math.distance(transform.Position.xz, center.xz);
                        Assert.That(closing, Is.EqualTo(step).Within(1e-4f), "Enemies close in one full step per update.");
                    }
                    else
                    {
                        TakeExpectedSpawn(expected, transform.Position, enemy);
                        Assert.That(transform.Scale, Is.EqualTo(prefab.Scale));
                        Assert.That(manager.GetComponentData<Health>(enemy).Current, Is.EqualTo(Health.DefaultEnemyHealth));
                        Assert.That(manager.HasComponent<MaterialMeshInfo>(enemy), Is.True);
                        spawned++;
                    }
                    known[enemy] = transform.Position;
                }
                Assert.That(expected, Is.Empty, "Every scheduled spawn appears in the world.");
                sequence = spawnState.SpawnSequence;
            }

            Assert.That(spawned, Is.GreaterThanOrEqualTo(20));
            Assert.That(main.Projectiles.IsEmpty, Is.True);
            Assert.That(main.PlayerHealth, Is.EqualTo(Health.DefaultPlayerHealth));
        }

        [UnityTest]
        public IEnumerator Weapon_ShootsNearestEnemyAndKillsItWithTwoHits()
        {
            var manager = main.Manager;
            var weapon = manager.GetComponentData<Weapon>(main.Player);
            yield return main.RunUntil(() => !main.Projectiles.IsEmpty, 15f, "The weapon never fired at the first wave.");

            using (var shots = main.Projectiles.ToEntityArray(Allocator.Temp))
                Assert.That(shots.Length, Is.EqualTo(1), "The weapon fires at most once per update.");
            var shot = main.Projectiles.GetSingletonEntity();
            var origin = main.PlayerPosition;
            var shotTransform = manager.GetComponentData<LocalTransform>(shot);
            var flight = manager.GetComponentData<Projectile>(shot);
            AssertNear(shotTransform.Position, origin, "Projectiles leave from the player");
            AssertNear(manager.GetComponentData<LocalToWorld>(shot).Position, shotTransform.Position,
                "A new projectile renders away from its simulated position");
            Assert.That(manager.HasComponent<MaterialMeshInfo>(shot), Is.True);
            Assert.That(flight.LifetimeRemaining, Is.EqualTo(weapon.ProjectileLifetime), "Flight starts on the next update.");
            Assert.That(math.length(flight.Velocity), Is.EqualTo(weapon.ProjectileSpeed).Within(Tolerance));

            // Enemies have not moved since the firing update selected its target.
            var target = NearestEnemy(origin, weapon.Range);
            Assert.That(target, Is.Not.EqualTo(Entity.Null), "A shot needs a target in range.");
            var aim = math.normalize(manager.GetComponentData<LocalTransform>(target).Position.xz - origin.xz);
            Assert.That(math.dot(math.normalize(flight.Velocity.xz), aim), Is.GreaterThan(0.9999f));

            var sawWounded = false;
            for (var frame = 0; manager.Exists(target); frame++)
            {
                // Two hits land well within one projectile lifetime.
                if (frame >= MainSceneSession.FramesFor(1.5f)) Assert.Fail("The first target survived its hits.");
                yield return null;
                if (!manager.Exists(target)) break;
                var health = manager.GetComponentData<Health>(target).Current;
                Assert.That(health, Is.GreaterThan(0), "A killed enemy is destroyed in the same update.");
                sawWounded |= health == Health.DefaultEnemyHealth - ProjectileCombat.DefaultDamage;
            }

            Assert.That(sawWounded, Is.True, "The first hit wounds without killing.");
            Assert.That(manager.Exists(shot), Is.False, "The first projectile is consumed by its hit, not by expiry.");
            Assert.That(main.Kills, Is.GreaterThanOrEqualTo(1));
            Assert.That(main.PlayerHealth, Is.EqualTo(Health.DefaultPlayerHealth), "Nothing has reached the player yet.");
        }

        [UnityTest]
        public IEnumerator IdlePlayer_TakesPacedContactDamageUntilDefeatThenResultScreenRestarts()
        {
            var interval = main.Manager.GetComponentData<PlayerContactDamage>(main.Player).Interval;
            var health = main.PlayerHealth;
            var lastHitAt = float.NegativeInfinity;
            var hits = 0;
            for (var frame = 0; main.Session.Status == SessionStatus.Running; frame++)
            {
                if (frame >= MainSceneSession.FramesFor(90f)) Assert.Fail("The idle player outlasted the swarm.");
                yield return null;
                var current = main.PlayerHealth;
                if (current != health)
                {
                    var elapsed = main.Session.Elapsed;
                    Assert.That(current, Is.EqualTo(health - PlayerContactDamage.DefaultDamage),
                        "One contact per interval, however many enemies touch.");
                    Assert.That(elapsed - lastHitAt, Is.GreaterThanOrEqualTo(interval - Tolerance));
                    lastHitAt = elapsed;
                    health = current;
                    hits++;
                }
                // Damage reaches the HUD in the same frame, not at the next sampling window.
                StringAssert.Contains($"HP {current}\n", main.HudCounters.text);
            }

            Assert.That(main.Session.Status, Is.EqualTo(SessionStatus.Lost));
            Assert.That(main.PlayerHealth, Is.Zero);
            Assert.That(hits, Is.EqualTo(Health.DefaultPlayerHealth));
            // The Edit Mode balance contract holds in the shipped scene.
            Assert.That(main.Session.Elapsed, Is.InRange(20f, 60f));
            Assert.That(main.ResultPanel.activeSelf, Is.True);
            Assert.That(main.ResultText.text, Is.EqualTo("Defeat"));

            // Nothing moves, spawns, fires or ticks after the outcome.
            var frozen = Snapshot();
            yield return main.Frames(60);
            AssertSameSnapshot(frozen, Snapshot());

            main.RestartButton.onClick.Invoke();
            yield return main.RunUntil(() => main.Session.RestartedThisFrame, 0.5f,
                "The result screen did not restart the session.");
            AssertFreshSession();
            Assert.That(main.ResultPanel.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator RestartKey_ClearsSessionStateAndReplaysSpawnSeed()
        {
            // Build up state worth clearing: live projectiles, a kill and a moved player.
            yield return main.RunUntil(() => !main.Projectiles.IsEmpty && main.Kills > 0, 15f,
                "No combat happened before the restart.");
            yield return Press(keyboard.dKey);
            yield return main.Frames(30);
            Assert.That(main.PlayerPosition.x, Is.GreaterThan(main.Session.InitialPlayerTransform.Position.x));
            Assert.That(main.AllEnemies.CalculateEntityCount(), Is.GreaterThan(0));

            yield return Release(keyboard.dKey);
            yield return Press(keyboard.rKey);
            yield return main.RunUntil(() => main.Session.RestartedThisFrame, 0.5f, "R did not restart the session.");
            AssertFreshSession();
            yield return Release(keyboard.rKey);

            // The new session replays spawn sequence 0 onward with the baked seed.
            var center = main.Session.InitialPlayerTransform.Position;
            var known = new HashSet<Entity>();
            var sequence = 0u;
            while (sequence < 3)
            {
                if (main.SpawnState.Elapsed > 2f) Assert.Fail("Spawning did not resume after the restart.");
                yield return null;
                var spawnState = main.SpawnState;
                Assert.That(spawnState.Elapsed, Is.EqualTo(main.Session.Elapsed), "Timer and spawn clock restart together.");
                var expected = ExpectedSpawns(center, sequence, spawnState.SpawnSequence);
                using var enemies = main.Enemies.ToEntityArray(Allocator.Temp);
                foreach (var enemy in enemies)
                {
                    if (known.Add(enemy))
                        TakeExpectedSpawn(expected, main.Manager.GetComponentData<LocalTransform>(enemy).Position, enemy);
                }
                Assert.That(expected, Is.Empty);
                sequence = spawnState.SpawnSequence;
            }
        }

        [UnityTest]
        public IEnumerator PresetKeys_FillStressSwarmThenReturnToCleanSurvival()
        {
            yield return Press(keyboard.digit1Key);
            yield return main.RunUntil(() => main.Enemies.CalculateEntityCount() == (int)BenchmarkPreset.Swarm1K, 0.5f,
                "Key 1 did not fill the 1k stress swarm.");
            yield return Release(keyboard.digit1Key);
            Assert.That(main.Benchmark.ActivePreset, Is.EqualTo(BenchmarkPreset.Swarm1K));
            Assert.That(main.SpawnState.SpawnSequence, Is.EqualTo((uint)BenchmarkPreset.Swarm1K));
            StringAssert.StartsWith("STRESS 1000", main.HudCounters.text);

            yield return Press(keyboard.digit0Key);
            yield return main.RunUntil(() => main.Session.RestartedThisFrame, 0.5f, "Key 0 did not return to Survival.");
            Assert.That(main.Benchmark.ActivePreset, Is.EqualTo(BenchmarkPreset.Survival));
            AssertFreshSession();
            yield return Release(keyboard.digit0Key);
        }

        [UnityTest]
        public IEnumerator UnloadingMain_RemovesEverySessionEntity()
        {
            yield return main.RunUntil(() => !main.Projectiles.IsEmpty, 15f, "No projectiles before unloading.");
            Assert.That(main.Enemies.IsEmpty, Is.False);

            yield return main.Unload();
            Assert.That(main.LeftoverEntities, Is.Zero,
                "Instantiated enemies and projectiles must leave with their SubScene.");
        }

        // InputTestFixture snapshots every key byte from the start of the key array up
        // to the changed key, so a second event queued in the same update would bring
        // back keys the first one released. One key change per update keeps state exact.
        private IEnumerator Press(ButtonControl key)
        {
            input.Press(key);
            yield return null;
        }

        private IEnumerator Release(ButtonControl key)
        {
            input.Release(key);
            yield return null;
        }

        private void AssertFreshSession()
        {
            var manager = main.Manager;
            var session = main.Session;
            Assert.That(session.Status, Is.EqualTo(SessionStatus.Running));
            Assert.That(session.Elapsed, Is.Zero);
            Assert.That(main.AllEnemies.CalculateEntityCount(), Is.Zero, "Restart destroys enemies, disabled ones included.");
            Assert.That(main.AllProjectiles.CalculateEntityCount(), Is.Zero, "Restart destroys projectiles, disabled ones included.");

            var player = main.Player;
            var transform = manager.GetComponentData<LocalTransform>(player);
            Assert.That(transform.Position, Is.EqualTo(session.InitialPlayerTransform.Position));
            Assert.That(transform.Rotation, Is.EqualTo(session.InitialPlayerTransform.Rotation));
            Assert.That(main.PlayerHealth, Is.EqualTo(Health.DefaultPlayerHealth));
            Assert.That(manager.GetComponentData<WeaponState>(player).CooldownRemaining, Is.Zero);
            Assert.That(manager.GetComponentData<PlayerContactState>(player).CooldownRemaining, Is.Zero);
            Assert.That(manager.GetBuffer<DamageEvent>(player, true).Length, Is.Zero);

            var spawnState = main.SpawnState;
            Assert.That(spawnState.Elapsed, Is.Zero);
            Assert.That(spawnState.SpawnBudget, Is.Zero);
            Assert.That(spawnState.SpawnSequence, Is.Zero);

            // Baked prefabs survive the purge.
            Assert.That(manager.HasComponent<Prefab>(main.SpawnConfig.EnemyPrefab), Is.True);
            Assert.That(manager.HasComponent<Prefab>(manager.GetComponentData<Weapon>(player).ProjectilePrefab), Is.True);
        }

        private List<float3> ExpectedSpawns(float3 center, uint from, uint to)
        {
            var config = main.SpawnConfig;
            var height = main.Manager.GetComponentData<LocalTransform>(config.EnemyPrefab).Position.y;
            var halfExtents = main.ArenaHalfExtents;
            var positions = new List<float3>();
            for (var sequence = from; sequence < to; sequence++)
            {
                positions.Add(SpawnSystem.CalculateSpawnPosition(center, height, config.SpawnRadius,
                    halfExtents, config.RandomSeed, sequence));
            }
            return positions;
        }

        // New enemies have not moved yet: each sits on its own scheduled spawn point.
        private static void TakeExpectedSpawn(List<float3> expected, float3 position, Entity enemy)
        {
            var match = expected.FindIndex(point => math.distance(point, position) <= Tolerance);
            Assert.That(match, Is.GreaterThanOrEqualTo(0), $"{enemy} spawned at {position}, off the seeded schedule.");
            expected.RemoveAt(match);
        }

        private Entity NearestEnemy(float3 origin, float range)
        {
            var nearest = Entity.Null;
            var best = range * range;
            using var enemies = main.Enemies.ToEntityArray(Allocator.Temp);
            foreach (var enemy in enemies)
            {
                var distance = math.distancesq(origin.xz, main.Manager.GetComponentData<LocalTransform>(enemy).Position.xz);
                if (distance > best) continue;
                best = distance;
                nearest = enemy;
            }
            return nearest;
        }

        private void AssertRenderablePrefab(Entity prefab)
        {
            var manager = main.Manager;
            Assert.That(manager.Exists(prefab), Is.True);
            Assert.That(manager.HasComponent<Prefab>(prefab), Is.True);
            Assert.That(manager.HasComponent<LocalTransform>(prefab), Is.True);
            Assert.That(manager.HasComponent<MaterialMeshInfo>(prefab), Is.True, "Baked prefabs keep their render data.");
        }

        private static void AssertNear(float3 actual, float3 expected, string message)
        {
            Assert.That(math.distance(actual, expected), Is.LessThanOrEqualTo(Tolerance),
                $"{message}: expected {expected}, was {actual}.");
        }

        private sealed class SessionSnapshot
        {
            public GameSession Session;
            public SpawnState Spawn;
            public WeaponState Weapon;
            public float3 PlayerPosition;
            public int PlayerHealth;
            public readonly Dictionary<Entity, float3> Positions = new Dictionary<Entity, float3>();
        }

        private SessionSnapshot Snapshot()
        {
            var manager = main.Manager;
            var snapshot = new SessionSnapshot
            {
                Session = main.Session,
                Spawn = main.SpawnState,
                Weapon = manager.GetComponentData<WeaponState>(main.Player),
                PlayerPosition = main.PlayerPosition,
                PlayerHealth = main.PlayerHealth
            };
            using var enemies = main.AllEnemies.ToEntityArray(Allocator.Temp);
            using var projectiles = main.AllProjectiles.ToEntityArray(Allocator.Temp);
            foreach (var entity in enemies)
                snapshot.Positions.Add(entity, manager.GetComponentData<LocalTransform>(entity).Position);
            foreach (var entity in projectiles)
                snapshot.Positions.Add(entity, manager.GetComponentData<LocalTransform>(entity).Position);
            return snapshot;
        }

        private static void AssertSameSnapshot(SessionSnapshot expected, SessionSnapshot actual)
        {
            Assert.That(actual.Session.Status, Is.EqualTo(expected.Session.Status));
            Assert.That(actual.Session.Elapsed, Is.EqualTo(expected.Session.Elapsed), "The session timer stopped.");
            Assert.That(actual.Spawn.Elapsed, Is.EqualTo(expected.Spawn.Elapsed), "The spawn clock stopped.");
            Assert.That(actual.Spawn.SpawnSequence, Is.EqualTo(expected.Spawn.SpawnSequence), "Spawning stopped.");
            Assert.That(actual.Weapon.CooldownRemaining, Is.EqualTo(expected.Weapon.CooldownRemaining), "The weapon stopped.");
            Assert.That(actual.PlayerPosition, Is.EqualTo(expected.PlayerPosition));
            Assert.That(actual.PlayerHealth, Is.EqualTo(expected.PlayerHealth));
            Assert.That(actual.Positions, Is.EquivalentTo(expected.Positions), "Enemies and projectiles stopped.");
        }
    }
}
