using DotsSwarm.Spawning;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    // EndSimulation ECB from the previous frame has already played back. Reset
    // before any gameplay producer can queue commands for the new session.
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial struct GameSessionStartSystem : ISystem
    {
        private EntityQuery playerQuery;
        private EntityQuery enemies;
        private EntityQuery projectiles;

        public void OnCreate(ref SystemState state)
        {
            playerQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Player, Health, LocalTransform>().Build(ref state);
            enemies = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Enemy>().WithNone<Prefab>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            projectiles = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Projectile>().WithNone<Prefab>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities).Build(ref state);
            state.RequireForUpdate<GameSession>();
            state.RequireForUpdate(playerQuery);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Keep a value copy: destroying entities invalidates component refs.
            var session = SystemAPI.GetSingleton<GameSession>();
            var player = playerQuery.GetSingletonEntity();
            session.RestartedThisFrame = false;
            if (SystemAPI.TryGetSingleton<BenchmarkState>(out var benchmark) && benchmark.ChangeRequested)
            {
                if (BenchmarkState.IsValid(benchmark.RequestedPreset))
                {
                    benchmark.ActivePreset = benchmark.RequestedPreset;
                    session.RestartRequested = true;
                }
                benchmark.ChangeRequested = false;
                SystemAPI.SetSingleton(benchmark);
            }
            if (!session.Initialized)
            {
                session.InitialPlayerTransform = SystemAPI.GetComponent<LocalTransform>(player);
                session.InitialPlayerHealth = SystemAPI.GetComponent<Health>(player);
                session.Initialized = true;
            }

            if (session.RestartRequested)
            {
                state.EntityManager.DestroyEntity(enemies);
                state.EntityManager.DestroyEntity(projectiles);
                state.EntityManager.SetComponentData(player, session.InitialPlayerTransform);
                state.EntityManager.SetComponentData(player, session.InitialPlayerHealth);

                foreach (var cooldown in SystemAPI.Query<RefRW<WeaponState>>())
                    cooldown.ValueRW = default;
                foreach (var contact in SystemAPI.Query<RefRW<PlayerContactState>>())
                    contact.ValueRW = default;
                foreach (var spawn in SystemAPI.Query<RefRW<SpawnState>>())
                    spawn.ValueRW = default;
                foreach (var input in SystemAPI.Query<RefRW<PlayerInput>>())
                    input.ValueRW = default;
                foreach (var events in SystemAPI.Query<DynamicBuffer<DamageEvent>>())
                    events.Clear();

                session.Status = SessionStatus.Running;
                session.Elapsed = 0f;
                session.RestartRequested = false;
                // A reset frame leaves a clean, observable initial state. The grid
                // still rebuilds, clearing all references to destroyed enemies.
                session.RestartedThisFrame = true;
            }

            SystemAPI.SetSingleton(session);
        }
    }
}
