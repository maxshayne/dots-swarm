using Unity.Entities;
using UnityEngine;

namespace DotsSwarm.Spawning
{
    [DisallowMultipleComponent]
    public sealed class SpawnConfigAuthoring : MonoBehaviour
    {
        [SerializeField]
        private GameObject enemyPrefab;

        [SerializeField]
        [Min(0f)]
        private float spawnRadius = SpawnConfig.DefaultSpawnRadius;

        [SerializeField]
        [Min(0f)]
        private float spawnRate = SpawnConfig.DefaultSpawnRate;

        [SerializeField]
        [Min(0)]
        private int maxEnemies = SpawnConfig.DefaultMaxEnemies;

        [SerializeField]
        private uint randomSeed = SpawnConfig.DefaultRandomSeed;

        private void OnValidate()
        {
            spawnRadius = Mathf.Max(0f, spawnRadius);
            spawnRate = Mathf.Max(0f, spawnRate);
            maxEnemies = Mathf.Max(0, maxEnemies);
        }

        private sealed class SpawnConfigBaker : Baker<SpawnConfigAuthoring>
        {
            public override void Bake(SpawnConfigAuthoring authoring)
            {
                if (authoring.enemyPrefab == null)
                {
                    return;
                }

                var entity = GetEntity(TransformUsageFlags.None);
                var prefabEntity = GetEntity(
                    authoring.enemyPrefab,
                    TransformUsageFlags.Dynamic);

                AddComponent(entity, SpawnConfig.Create(
                    prefabEntity,
                    authoring.spawnRadius,
                    authoring.spawnRate,
                    authoring.maxEnemies,
                    authoring.randomSeed));
                AddComponent(entity, new SpawnState());
            }
        }
    }
}
