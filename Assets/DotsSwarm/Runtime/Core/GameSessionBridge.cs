using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DotsSwarm.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameSessionBridge : MonoBehaviour
    {
        private World sessionWorld;
        private EntityQuery sessionQuery;
        private EntityQuery benchmarkQuery;
        private EntityQuery enemies;
        private EntityQuery projectiles;
        private GameSessionHud hud;
        private BenchmarkFrameTime frameTime;
        private int warmupFrames;
        private bool needsRefresh;

        private void OnEnable()
        {
            hud = new GameSessionHud(RequestPreset, RequestRestart);
            ResetMeasurements();
        }

        private void LateUpdate()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                hud.SetVisible(false);
                return;
            }
            if (sessionWorld != world)
            {
                ReleaseQueries();
                sessionWorld = world;
                var manager = world.EntityManager;
                sessionQuery = manager.CreateEntityQuery(typeof(GameSession));
                benchmarkQuery = manager.CreateEntityQuery(typeof(BenchmarkState));
                enemies = manager.CreateEntityQuery(ComponentType.ReadOnly<Enemy>());
                projectiles = manager.CreateEntityQuery(ComponentType.ReadOnly<Projectile>());
                ResetMeasurements();
            }
            if (sessionQuery.CalculateEntityCount() != 1 || benchmarkQuery.CalculateEntityCount() != 1)
            {
                hud.SetVisible(false);
                return;
            }
            var session = sessionQuery.GetSingleton<GameSession>();
            hud.SetVisible(session.Initialized);
            if (!session.Initialized) return;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) RequestRestart();
                if (keyboard.digit0Key.wasPressedThisFrame) RequestPreset(BenchmarkPreset.Survival);
                else if (keyboard.digit1Key.wasPressedThisFrame) RequestPreset(BenchmarkPreset.Swarm1K);
                else if (keyboard.digit2Key.wasPressedThisFrame) RequestPreset(BenchmarkPreset.Swarm10K);
                else if (keyboard.digit3Key.wasPressedThisFrame) RequestPreset(BenchmarkPreset.Swarm20K);
                else if (keyboard.digit4Key.wasPressedThisFrame) RequestPreset(BenchmarkPreset.Swarm50K);
            }
            if (session.RestartedThisFrame) ResetMeasurements();
            hud.SetStatus(session.Status);
            var refresh = needsRefresh;
            if (warmupFrames > 0) warmupFrames--;
            else refresh |= frameTime.AddFrame(Time.unscaledDeltaTime);
            if (!refresh) return;

            var benchmark = benchmarkQuery.GetSingleton<BenchmarkState>();
            // Observe counts after EndSimulation ECB playback; query caching avoids
            // entity arrays and per-frame query allocations.
            hud.Refresh(enemies.CalculateEntityCount(), projectiles.CalculateEntityCount(),
                frameTime.Milliseconds, session, benchmark);
            needsRefresh = false;
        }

        private void ResetMeasurements()
        {
            frameTime = default;
            // Exclude reset and the one-time population/mesh warmup frame.
            warmupFrames = 2;
            needsRefresh = true;
        }

        private void RequestPreset(BenchmarkPreset preset)
        {
            if (sessionWorld == null || !sessionWorld.IsCreated || benchmarkQuery.CalculateEntityCount() != 1)
                return;
            var benchmark = benchmarkQuery.GetSingleton<BenchmarkState>();
            benchmark.RequestedPreset = preset;
            benchmark.ChangeRequested = true;
            benchmarkQuery.SetSingleton(benchmark);
        }

        private void RequestRestart()
        {
            if (sessionWorld == null || !sessionWorld.IsCreated || sessionQuery.CalculateEntityCount() != 1)
                return;
            var session = sessionQuery.GetSingleton<GameSession>();
            session.RestartRequested = true;
            sessionQuery.SetSingleton(session);
        }

        private void OnDisable()
        {
            ReleaseQueries();
            hud?.Dispose();
            hud = null;
        }

        private void ReleaseQueries()
        {
            if (sessionWorld != null && sessionWorld.IsCreated)
            {
                sessionQuery.Dispose();
                benchmarkQuery.Dispose();
                enemies.Dispose();
                projectiles.Dispose();
            }
            sessionWorld = null;
        }
    }
}
