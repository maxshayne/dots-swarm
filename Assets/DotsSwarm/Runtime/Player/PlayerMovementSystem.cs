using DotsSwarm.Core;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsSwarm.Gameplay
{
    [BurstCompile]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial struct PlayerMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Player>();
            state.RequireForUpdate<PlayerInput>();
            state.RequireForUpdate<GameConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var move = SystemAPI.GetSingleton<PlayerInput>().Move;
            var arenaHalfExtents = SystemAPI.GetSingleton<GameConfig>().ArenaHalfExtents;
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, player) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<Player>>())
            {
                transform.ValueRW.Position = CalculatePosition(
                    transform.ValueRO.Position,
                    move,
                    player.ValueRO.MovementSpeed,
                    deltaTime,
                    arenaHalfExtents);
            }
        }

        public static float3 CalculatePosition(
            float3 position,
            float2 move,
            float movementSpeed,
            float deltaTime,
            float2 arenaHalfExtents)
        {
            var moveLengthSq = math.lengthsq(move);
            if (moveLengthSq > 1f)
            {
                move *= math.rsqrt(moveLengthSq);
            }

            position.xz += move * math.max(0f, movementSpeed) * math.max(0f, deltaTime);
            position.xz = math.clamp(position.xz, -arenaHalfExtents, arenaHalfExtents);
            return position;
        }
    }
}
