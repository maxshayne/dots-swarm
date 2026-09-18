using DotsSwarm.Spawning;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SpawnSystem))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct EnemyMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Enemy>();
            state.RequireForUpdate<Player>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton<GameSession>(out var session) && !session.CanSimulate)
                return;

            var playerEntity = SystemAPI.GetSingletonEntity<Player>();
            var playerPosition = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

            state.Dependency = new MoveTowardPlayerJob
            {
                TargetPosition = playerPosition,
                DeltaTime = SystemAPI.Time.DeltaTime
            }.ScheduleParallel(state.Dependency);
        }

        public static float3 CalculatePosition(
            float3 position,
            float3 targetPosition,
            float movementSpeed,
            float deltaTime)
        {
            var offset = targetPosition.xz - position.xz;
            var distanceSquared = math.lengthsq(offset);
            var maximumStep = math.max(0f, movementSpeed) * math.max(0f, deltaTime);
            if (distanceSquared == 0f || maximumStep == 0f)
            {
                return position;
            }

            var distance = math.sqrt(distanceSquared);
            position.xz += offset * (math.min(maximumStep, distance) / distance);
            return position;
        }

        [BurstCompile]
        private partial struct MoveTowardPlayerJob : IJobEntity
        {
            public float3 TargetPosition;
            public float DeltaTime;

            private void Execute(ref LocalTransform transform, in Enemy enemy)
            {
                transform.Position = CalculatePosition(
                    transform.Position,
                    TargetPosition,
                    enemy.MovementSpeed,
                    DeltaTime);
            }
        }
    }
}
