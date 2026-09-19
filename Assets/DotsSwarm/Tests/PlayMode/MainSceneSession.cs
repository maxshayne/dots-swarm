using System;
using System.Collections;
using DotsSwarm.Core;
using DotsSwarm.Gameplay;
using DotsSwarm.Spawning;
using NUnit.Framework;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DotsSwarm.Tests.Integration
{
    // Runs the shipped Main scene in the default world: the baked ArenaSubScene,
    // automatic system creation and ordering, and the MonoBehaviour bridges.
    // Every frame advances the simulation by exactly one 1/60 s step, and test
    // code resumes between two complete simulation updates.
    internal sealed class MainSceneSession
    {
        public const string ScenePath = "Assets/DotsSwarm/Scenes/Main.unity";
        public const float FrameTime = 1f / 60f;

        // Real seconds: on a cold Library the first load also bakes the SubScene.
        private const double StreamingTimeout = 180.0;
        private const int UnloadFrames = 120;

        private Scene scene;
        private float previousCaptureDeltaTime;
        private bool capturing;
        private bool hasQueries;
        private EntityQuery sessionEntities;
        private Transform hud;

        public World World { get; private set; }
        public EntityManager Manager => World.EntityManager;
        public EntityQuery Sessions { get; private set; }
        public EntityQuery Players { get; private set; }
        public EntityQuery Spawners { get; private set; }
        // Active gameplay entities; the All* variants also count disabled ones.
        public EntityQuery Enemies { get; private set; }
        public EntityQuery AllEnemies { get; private set; }
        public EntityQuery Projectiles { get; private set; }
        public EntityQuery AllProjectiles { get; private set; }
        // Session-owned entities found after the last Unload; prefabs included.
        public int LeftoverEntities { get; private set; }

        public Entity SessionEntity => Sessions.GetSingletonEntity();
        public GameSession Session => Manager.GetComponentData<GameSession>(SessionEntity);
        public BenchmarkState Benchmark => Manager.GetComponentData<BenchmarkState>(SessionEntity);
        public float2 ArenaHalfExtents => Manager.GetComponentData<GameConfig>(SessionEntity).ArenaHalfExtents;
        public Entity Player => Players.GetSingletonEntity();
        public float3 PlayerPosition => Manager.GetComponentData<LocalTransform>(Player).Position;
        public int PlayerHealth => Manager.GetComponentData<Health>(Player).Current;
        public SpawnConfig SpawnConfig => Manager.GetComponentData<SpawnConfig>(Spawners.GetSingletonEntity());
        public SpawnState SpawnState => Manager.GetComponentData<SpawnState>(Spawners.GetSingletonEntity());
        // Survival only: every spawn that is no longer alive was killed.
        public int Kills => (int)SpawnState.SpawnSequence - Enemies.CalculateEntityCount();

        public TMP_Text HudCounters => Hud.Find("Benchmark/Counters").GetComponent<TMP_Text>();
        public GameObject ResultPanel => Hud.Find("Result").gameObject;
        public TMP_Text ResultText => Hud.Find("Result/Outcome").GetComponent<TMP_Text>();
        public Button RestartButton => Hud.Find("Result/Restart (R)").GetComponent<Button>();

        // The bridge keeps its HUD root active once the session has initialized.
        private Transform Hud
        {
            get
            {
                if (hud != null) return hud;
                var root = GameObject.Find("Session HUD");
                Assert.That(root, Is.Not.Null, "GameSessionBridge did not show the session HUD.");
                return hud = root.transform;
            }
        }

        public static int FramesFor(float seconds) => (int)math.ceil(seconds / FrameTime);

        public IEnumerator Load()
        {
            previousCaptureDeltaTime = Time.captureDeltaTime;
            capturing = true;
            // Fixed steps decouple simulated time from the editor or batchmode frame rate.
            Time.captureDeltaTime = FrameTime;

            var loading = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            Assert.That(loading, Is.Not.Null, $"{ScenePath} must be enabled in the build settings.");
            while (!loading.isDone) yield return null;
            scene = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(scene);

            World = World.DefaultGameObjectInjectionWorld;
            Assert.That(World != null && World.IsCreated, Is.True, "Play mode did not create the default world.");
            CreateQueries();

            // SubScene sections stream in asynchronously; the session initializes on
            // the first update that sees the baked player.
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!IsSessionInitialized())
            {
                if (timer.Elapsed.TotalSeconds > StreamingTimeout)
                    Assert.Fail("ArenaSubScene did not stream in a playable session.");
                yield return null;
            }
            Assert.That(World.Time.DeltaTime, Is.EqualTo(FrameTime).Within(1e-6f),
                "The default world must advance one fixed step per frame.");
        }

        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded)
            {
                var unloading = SceneManager.UnloadSceneAsync(scene);
                while (unloading != null && !unloading.isDone) yield return null;
            }
            scene = default;
            hud = null;

            if (hasQueries && World != null && World.IsCreated)
            {
                // The disabled SubScene unloads its sections on a later streaming update.
                for (var frame = 0; frame < UnloadFrames && !sessionEntities.IsEmptyIgnoreFilter; frame++)
                    yield return null;
                LeftoverEntities = sessionEntities.CalculateEntityCount();
                // Keep later tests isolated; the unload test asserts the count itself.
                if (LeftoverEntities > 0) Manager.DestroyEntity(sessionEntities);
                DisposeQueries();
            }
            hasQueries = false;
            World = null;

            if (capturing)
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
                capturing = false;
            }
        }

        public IEnumerator Frames(int count)
        {
            for (var frame = 0; frame < count; frame++) yield return null;
        }

        // Steps frame by frame until the condition holds, within a simulated-time budget.
        public IEnumerator RunUntil(Func<bool> condition, float maxSeconds, string failure)
        {
            var budget = FramesFor(maxSeconds);
            for (var frame = 0; !condition(); frame++)
            {
                if (frame >= budget) Assert.Fail(failure);
                yield return null;
            }
        }

        private bool IsSessionInitialized() => Sessions.CalculateEntityCount() == 1
            && Players.CalculateEntityCount() == 1 && Session.Initialized;

        private void CreateQueries()
        {
            var manager = Manager;
            Sessions = manager.CreateEntityQuery(ComponentType.ReadOnly<GameSession>());
            Players = manager.CreateEntityQuery(ComponentType.ReadOnly<Player>());
            Spawners = manager.CreateEntityQuery(ComponentType.ReadOnly<SpawnConfig>(), ComponentType.ReadOnly<SpawnState>());
            Enemies = manager.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
            Projectiles = manager.CreateEntityQuery(ComponentType.ReadOnly<Projectile>());
            AllEnemies = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(manager);
            AllProjectiles = new EntityQueryBuilder(Allocator.Temp).WithAll<Projectile>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(manager);
            sessionEntities = new EntityQueryBuilder(Allocator.Temp)
                .WithAny<GameSession, Player, PlayerInput, SpawnConfig, Enemy, Projectile>()
                .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                .Build(manager);
            hasQueries = true;
        }

        private void DisposeQueries()
        {
            Sessions.Dispose();
            Players.Dispose();
            Spawners.Dispose();
            Enemies.Dispose();
            Projectiles.Dispose();
            AllEnemies.Dispose();
            AllProjectiles.Dispose();
            sessionEntities.Dispose();
        }
    }
}
