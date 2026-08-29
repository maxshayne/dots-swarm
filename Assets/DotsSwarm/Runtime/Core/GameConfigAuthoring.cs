using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsSwarm.Core
{
    [DisallowMultipleComponent]
    public sealed class GameConfigAuthoring : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Playable arena dimensions on the XZ plane.")]
        private Vector2 arenaSize = Vector2.one * GameConfig.DefaultArenaSize;

        public Vector2 ArenaSize
        {
            get => arenaSize;
            set => arenaSize = value;
        }

        private void OnValidate()
        {
            arenaSize.x = Mathf.Max(GameConfig.MinimumArenaSize, arenaSize.x);
            arenaSize.y = Mathf.Max(GameConfig.MinimumArenaSize, arenaSize.y);
        }

        private sealed class GameConfigBaker : Baker<GameConfigAuthoring>
        {
            public override void Bake(GameConfigAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var size = new float2(authoring.arenaSize.x, authoring.arenaSize.y);
                AddComponent(entity, GameConfig.FromArenaSize(size));
            }
        }
    }
}
