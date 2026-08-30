# DOTS Swarm — Handoff

Последнее обновление: 2026-08-31

## Состояние проекта

- Этап: фундамент игровой сцены
- Выполнено: 3 из 16 MVP-итераций; итерация 4 реализована и ожидает проверки в Unity
- Репозиторий: Git инициализирован
- Unity-проект: создан на Unity 6.4 с URP и DOTS-пакетами
- Цель MVP: трёхминутная survivor-сессия с 20 000 активных врагов при стабильных 60 FPS

> [!IMPORTANT]
> **Текущая задача — итерация 4: Enemy spawning.**
>
> Реализация завершена. Перед коммитом нужен чистый импорт в открытом Unity Editor. Итерацию 5 до этого не начинать.

## Правила итерации

1. В работе находится только одна итерация.
2. Итерация должна завершаться состоянием, которое чисто импортируется и компилируется в Unity.
3. Перед handoff проверяются diff, компиляция в Unity и релевантные автоматические тесты. Тесты запускаются только из консоли, без ручного взаимодействия с Unity UI. Standalone build и ручные проверки, включая Play Mode, выполняет разработчик, если он явно не попросил иное.
4. Codex не выполняет `git commit`, а отдаёт одну готовую строку Conventional Commit.
5. После ручного коммита текущей становится следующая незавершённая итерация.

## Текущая итерация

### 4. Enemy spawning — UNITY CHECK PENDING

- [x] Создать Enemy prefab и baker.
- [x] Добавить `SpawnConfig`.
- [x] Реализовать spawn по радиусу и с лимитом через ECB.
- [x] Покрыть spawn budget, лимит и расчёт позиций Edit Mode-тестами.

Локальная проверка: runtime и test assembly компилируются с Unity Entities source generators; 8 test-методов проходят через Unity Mono из консоли. Отдельный Unity Editor не смог завершить чистый импорт из-за конфликта с Unity Licensing Client уже открытого Editor.

Критерий готовности: открытый Unity Editor чисто импортирует и компилирует проект, SubScene импортируется, 8 Edit Mode-тестов проходят из консоли. Ручную проверку появления врагов выполняет разработчик.

Плановый коммит: `feat(spawn): add configurable enemy spawning`

## Завершённые итерации

### 3. Player movement — COMPLETE

- [x] Создать baked Player entity с настраиваемой скоростью.
- [x] Добавить Input System boundary и `PlayerInput` singleton.
- [x] Реализовать Burst-compatible `PlayerMovementSystem`.
- [x] Нормализовать диагональный ввод и ограничить движение размерами арены.
- [x] Покрыть расчёт движения двумя Edit Mode-тестами.

Проверка: открытый Unity Editor чисто импортировал и скомпилировал проект; 4 Edit Mode-теста прошли из консоли. Ручную проверку WASD-движения выполняет разработчик.

Коммит: `feat(player): add WASD movement` (`a5cb1ac`)

### 2. Baked arena foundation — COMPLETE

- [x] Добавить runtime/test asmdef и структуру модулей.
- [x] Создать основную сцену, SubScene, арену, камеру и освещение.
- [x] Добавить первый `GameConfig` authoring/baker.
- [x] Включить Forward+ для совместимости с Entities Graphics.
- [x] Покрыть преобразование размеров арены Edit Mode-тестами.

Критерий готовности: проект чисто компилируется в Unity, SubScene импортируется, 2 Edit Mode-теста проходят. Ручную проверку сцены выполняет разработчик.

Коммит: `feat(core): add baked arena foundation`

### 1. Bootstrap Unity DOTS project — COMPLETE

- [x] Инициализировать Git-репозиторий.
- [x] Добавить Unity `.gitignore` и `.gitattributes`.
- [x] Создать URP-проект на совместимой версии Unity.
- [x] Подключить Entities, Entities Graphics, Burst, Collections, Mathematics, Input System и Test Framework.
- [x] Включить Visible Meta Files и Force Text serialization.
- [x] Зафиксировать версии пакетов в `Packages/manifest.json` и `Packages/packages-lock.json`.
- [x] Проверить, что проект открывается без ошибок и generated-директории не отслеживаются Git.

Критерий готовности: чистый Unity DOTS-проект компилируется и готов к разработке первой игровой сцены.

Коммит: `chore(project): bootstrap Unity DOTS project`

## MVP backlog

- [ ] **5. Enemy pursuit**
  - Реализовать `EnemyMovementSystem` через `ISystem` и `IJobEntity`.
  - Включить Burst и `Unity.Mathematics`.
  - Проверить параллельное движение 10 000 врагов без managed allocations.
  - Коммит: `feat(enemy): add Burst-powered pursuit`

- [ ] **6. Enemy spatial grid**
  - Реализовать system-owned `NativeParallelMultiHashMap`.
  - Настроить capacity, disposal и явные job dependencies.
  - Покрыть cell math и поиск соседних клеток тестами.
  - Коммит: `feat(spatial): build reusable enemy grid`

- [ ] **7. Automatic weapon**
  - Добавить weapon cooldown.
  - Искать ближайшего врага через spatial grid.
  - Создавать projectile entities через ECB.
  - Коммит: `feat(weapon): add grid-based auto fire`

- [ ] **8. Projectile simulation**
  - Реализовать движение пуль.
  - Добавить lifetime и гарантированное удаление.
  - Коммит: `feat(projectile): add movement and expiry`

- [ ] **9. Combat pipeline**
  - Добавить grid-based hit detection.
  - Передавать урон через `DynamicBuffer<DamageEvent>`.
  - Реализовать отдельные Damage и Death systems.
  - Выполнять structural changes через ECB `ParallelWriter`.
  - Коммит: `feat(combat): add buffered damage and death`

- [ ] **10. Player health**
  - Добавить здоровье игрока.
  - Реализовать contact damage и ограничение частоты попаданий.
  - Исключить race conditions при множественных контактах.
  - Коммит: `feat(player): add contact damage and health`

- [ ] **11. Survival game flow**
  - Добавить состояния Running, Won и Lost.
  - Реализовать трёхминутный таймер.
  - Добавить рестарт с полной очисткой состояния сессии.
  - Коммит: `feat(gameflow): add survival states`

- [ ] **12. Benchmark controls**
  - Добавить HUD со счётчиками enemies, projectiles и frame time.
  - Добавить stress-пресеты 1k, 10k, 20k и 50k.
  - Использовать фиксированный random seed и не создавать GC каждый кадр.
  - Коммит: `feat(debug): add swarm benchmark controls`

- [ ] **13. Game balance**
  - Настроить spawn curve.
  - Сбалансировать врагов, оружие и contact damage.
  - Проверить полную трёхминутную сессию.
  - Коммит: `feat(balance): tune three-minute swarm`

- [ ] **14. Gameplay integration tests**
  - Покрыть Play Mode-тестами spawn, movement, shot, damage и death.
  - Проверить рестарт и очистку ECS-состояния.
  - Коммит: `test(gameplay): cover full survival pipeline`

- [ ] **15. Performance target**
  - Профилировать standalone build.
  - Устранить GC, лишние resize, sync points и structural-change bottlenecks.
  - Проверить 20 000 отрисованных врагов с целью `p95 <= 16.67 ms` на зафиксированном железе.
  - Коммит: `perf(simulation): meet 20k swarm target`

- [ ] **16. Portfolio documentation**
  - Описать запуск, управление и архитектуру.
  - Добавить system ordering, benchmark protocol, результаты и скриншоты.
  - Подготовить репозиторий к публичному показу.
  - Коммит: `docs(readme): document architecture and results`

## После MVP

- [ ] Добавить выбор апгрейда каждые 30 секунд: Damage, Fire Rate и Projectile Count.
  - Коммит: `feat(upgrades): add timed weapon choices`
- [ ] Реализовать эквивалентный MonoBehaviour swarm baseline.
  - Коммит: `feat(benchmark): add managed swarm baseline`
- [ ] Опубликовать воспроизводимое сравнение DOTS и MonoBehaviour.
  - Коммит: `docs(benchmark): publish DOTS comparison`

## Зафиксированные технические решения

- Целевая платформа: Windows x64.
- Render pipeline: URP с Entities Graphics.
- Столкновения: собственные distance checks через spatial grid без Unity Physics.
- Пули MVP: уничтожаются после первого попадания.
- Input и UI остаются MonoBehaviour boundary-слоями.
- Brute-force target search разрешён только как тестовый oracle.
- Финальные показатели снимаются в standalone build, а не в Editor.
