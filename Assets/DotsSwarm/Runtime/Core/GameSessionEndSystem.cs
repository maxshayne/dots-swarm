using Unity.Burst;
using Unity.Collections;
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
            // Completes only last frame's outcome job, which has long finished.
            var session = SystemAPI.GetSingleton<GameSession>();
            if (!session.CanSimulate)
                return;

            // Stress runs continue until explicitly restarted or switched back.
            if (SystemAPI.TryGetSingleton<BenchmarkState>(out var benchmark) && benchmark.IsStress)
                return;

            // A job rather than a main-thread Health read: reading it here would wait
            // for the whole combat chain before TransformSystemGroup could schedule.
            // Main-thread GameSession readers (next frame, HUD) complete this job.
            state.Dependency = new ResolveOutcomeJob
            {
                Player = SystemAPI.GetSingletonEntity<Player>(),
                Healths = SystemAPI.GetComponentLookup<Health>(true),
                DeltaTime = SystemAPI.Time.DeltaTime
            }.Schedule(state.Dependency);
        }

        [BurstCompile]
        private partial struct ResolveOutcomeJob : IJobEntity
        {
            public Entity Player;
            [ReadOnly] public ComponentLookup<Health> Healths;
            public float DeltaTime;

            private void Execute(ref GameSession session)
            {
                session.Elapsed = math.min(GameSession.Duration,
                    session.Elapsed + math.max(0f, DeltaTime));
                if (Healths[Player].Current <= 0)
                    session.Status = SessionStatus.Lost;
                else if (session.Elapsed >= GameSession.Duration)
                    session.Status = SessionStatus.Won;
            }
        }
    }
}
