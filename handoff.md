# DOTS Swarm — Handoff

Последнее обновление: 2026-09-05

## Состояние проекта

- Этап: фундамент игровой сцены
- Выполнено: 6 из 16 MVP-итераций; итерации 7 и 8 реализованы и проверены, ожидают общего коммита
- Репозиторий: Git инициализирован
- Unity-проект: создан на Unity 6.4 с URP и DOTS-пакетами
- Цель MVP: трёхминутная survivor-сессия с 20 000 активных врагов при стабильных 60 FPS

> [!IMPORTANT]
> **Текущие задачи — итерации 7 и 8: Automatic weapon и Projectile simulation.**
>
> Две итерации взяты вместе по прямому запросу разработчика. Реализация и автоматическая проверка завершены. После ручного коммита переходить к итерации 9; до этого её не начинать.

## Правила итерации

1. В работе находится только одна итерация.
2. Итерация должна завершаться состоянием, которое чисто импортируется и компилируется в Unity.
3. Перед handoff проверяются diff, компиляция в Unity и релевантные автоматические тесты. Тесты запускаются только из консоли, без ручного взаимодействия с Unity UI. Standalone build и ручные проверки, включая Play Mode, выполняет разработчик, если он явно не попросил иное.
4. Codex не выполняет `git commit`, а отдаёт одну готовую строку Conventional Commit.
5. После ручного коммита текущей становится следующая незавершённая итерация.

## Текущие итерации

### 7. Automatic weapon — READY TO COMMIT

- [x] Добавить baked `Weapon` и `WeaponState` с настраиваемыми cooldown, range, скоростью и lifetime пуль.
- [x] Реализовать Burst `WeaponSystem` и поиск ближайшего врага через spatial grid во всех клетках радиуса, с отсечением по дистанции и стабильным выбором при равных расстояниях.
- [x] Создавать projectile entities через ECB `ParallelWriter`, возвращать reader dependency владельцу grid.
- [x] Создать Projectile prefab с baker и материалом, подключить оружие к Player в `ArenaSubScene`.
- [x] Покрыть cooldown, границу радиуса, отсутствие цели, совпадающие позиции, сравнение с brute-force oracle и rebuild после чтения grid.

### 8. Projectile simulation — READY TO COMMIT

- [x] Реализовать движение пуль через Burst `ISystem` и параллельный `IJobEntity`.
- [x] Добавить lifetime и удаление через ECB `ParallelWriter`, включая нулевой lifetime и большой шаг времени.
- [x] Ограничить движение оставшимся lifetime; продолжать обработку пуль после удаления игрока.
- [x] Покрыть движение, expiry, сохранность prefab, удаление 10 000 пуль и общий цикл выстрел → движение → удаление в отсортированной группе систем.

Проверка: Unity 6000.4.5f1 в batchmode импортировал assets и скомпилировал проект без ошибок. Финальный запуск из консоли с `-burst-force-sync-compilation`: все 33 Edit Mode-теста прошли (17 исходных и 16 новых), без предупреждений о порядке систем. Открытого Editor на момент проверки не было. Standalone build и Play Mode не запускались.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-weapon-projectile.log`, `Logs/weapon-projectile-results.xml`.

Ручные проверки разработчику:
- В `Main` дождаться врагов в радиусе 20: игрок автоматически выпускает жёлтые пули в ближайшую цель.
- Проверить стрельбу при WASD-движении, движение пуль и исчезновение примерно через 2 секунды.
- Попадания, урон и уничтожение врагов появятся в итерации 9; текущие пули проходят через врагов.

Общий плановый коммит: `feat(weapon): add auto fire and projectile expiry`

## Завершённые итерации

### 6. Enemy spatial grid — COMPLETE

- [x] Реализовать system-owned `NativeParallelMultiHashMap`.
- [x] Настроить геометрический рост capacity, disposal и явные job dependencies.
- [x] Покрыть cell math, соседние клетки, rebuild и рост capacity Edit Mode-тестами.
- [x] Устранить найденную при контрольном импорте Burst-ошибку в query setup `SpawnSystem`.

Проверка: Unity 6000.4.5f1 чисто импортировал и скомпилировал проект; все исходные 17 Edit Mode-тестов прошли, включая прогон 10 000 врагов без managed allocations и 5 spatial-grid тестов.

Коммит: `feat(spatial): build reusable enemy grid` (`e3bcaea`)

### 5. Enemy pursuit — COMPLETE

- [x] Добавить настраиваемую скорость в baked `Enemy`.
- [x] Реализовать `EnemyMovementSystem` через `ISystem` и `IJobEntity`.
- [x] Включить Burst, `Unity.Mathematics` и параллельный schedule.
- [x] Покрыть направление, overshoot и прогон 10 000 врагов без managed allocations Edit Mode-тестами.

Проверка: Unity 6000.4.5f1 чисто импортировал и скомпилировал runtime/test assemblies; все исходные 12 Edit Mode-тестов прошли в составе полного набора из 17 тестов. Ручную проверку движения врагов выполняет разработчик.

Коммит: `feat(enemy): add Burst-powered pursuit` (`fc613da`)

### 4. Enemy spawning — COMPLETE

- [x] Создать Enemy prefab и baker.
- [x] Добавить `SpawnConfig`.
- [x] Реализовать spawn по радиусу и с лимитом через ECB.
- [x] Покрыть spawn budget, лимит и расчёт позиций Edit Mode-тестами.

Коммит: `feat(spawn): add configurable enemy spawning` (`ab4c7c8`)

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
- Оружие ищет цель на плоскости XZ в настраиваемом радиусе; при равной дистанции выбирает меньший Entity index/version.
- Cooldown запускается после успешного выстрела; максимум один выстрел за update, без накопления очереди за время простоя или длинного кадра.
- Пули создаются в конце Simulation через ECB и начинают движение на следующем update. Порядок: grid → weapon → projectile movement → transforms → EndSimulation ECB; weapon также ждёт PlayerMovementSystem.
- Финальные показатели снимаются в standalone build, а не в Editor.
