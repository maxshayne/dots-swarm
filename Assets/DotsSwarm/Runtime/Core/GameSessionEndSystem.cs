using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(DeathSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct GameSessionEndSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GameSession>();
            state.RequireForUpdate<Player>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var session = SystemAPI.GetSingleton<GameSession>();
            if (!session.CanSimulate)
                return;

            // Stress runs continue until explicitly restarted or switched back.
            if (SystemAPI.TryGetSingleton<BenchmarkState>(out var benchmark) && benchmark.IsStress)
                return;

            var player = SystemAPI.GetSingletonEntity<Player>();
            // This read completes the damage job before choosing the outcome.
            var health = SystemAPI.GetComponent<Health>(player);
            session.Elapsed = math.min(GameSession.Duration,
                session.Elapsed + math.max(0f, SystemAPI.Time.DeltaTime));
            if (health.Current <= 0)
                session.Status = SessionStatus.Lost;
            else if (session.Elapsed >= GameSession.Duration)
                session.Status = SessionStatus.Won;

            SystemAPI.SetSingleton(session);
        }
    }
}
