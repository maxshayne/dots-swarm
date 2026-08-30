using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DotsSwarm.Gameplay
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class PlayerInputBridge : MonoBehaviour
    {
        [SerializeField]
        private InputActionAsset inputActions;

        [SerializeField]
        private string moveActionName = "Player/Move";

        private InputAction moveAction;
        private World inputWorld;
        private Entity inputEntity;
        private bool ownsInputEntity;
        private bool reportedDuplicateSingleton;

        private void OnEnable()
        {
            moveAction = inputActions == null
                ? null
                : inputActions.FindAction(moveActionName, false);

            if (moveAction == null)
            {
                Debug.LogError($"Input action '{moveActionName}' was not found.", this);
                enabled = false;
                return;
            }

            moveAction.Enable();
        }

        private void Update()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            if (inputWorld != world)
            {
                inputWorld = world;
                inputEntity = Entity.Null;
                ownsInputEntity = false;
            }

            var entityManager = world.EntityManager;
            if (inputEntity == Entity.Null || !entityManager.Exists(inputEntity))
            {
                using var inputQuery = entityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerInput>());
                var inputCount = inputQuery.CalculateEntityCount();

                if (inputCount > 1)
                {
                    if (!reportedDuplicateSingleton)
                    {
                        Debug.LogError("More than one PlayerInput singleton exists.", this);
                        reportedDuplicateSingleton = true;
                    }

                    return;
                }

                if (inputCount == 1)
                {
                    inputEntity = inputQuery.GetSingletonEntity();
                }
                else
                {
                    inputEntity = entityManager.CreateEntity(typeof(PlayerInput));
                    entityManager.SetName(inputEntity, "Player Input");
                    ownsInputEntity = true;
                }
            }

            var move = moveAction.ReadValue<Vector2>();
            entityManager.SetComponentData(inputEntity, new PlayerInput
            {
                Move = new float2(move.x, move.y)
            });
        }

        private void OnDisable()
        {
            moveAction?.Disable();

            if (inputWorld != null && inputWorld.IsCreated &&
                inputEntity != Entity.Null)
            {
                var entityManager = inputWorld.EntityManager;
                if (entityManager.Exists(inputEntity))
                {
                    if (ownsInputEntity)
                    {
                        entityManager.DestroyEntity(inputEntity);
                    }
                    else
                    {
                        entityManager.SetComponentData(inputEntity, default(PlayerInput));
                    }
                }
            }

            inputWorld = null;
            inputEntity = Entity.Null;
            ownsInputEntity = false;
            reportedDuplicateSingleton = false;
        }
    }
}
