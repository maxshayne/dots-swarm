# DOTS Swarm — Handoff

Последнее обновление: 2026-09-19

## Состояние проекта

- Этап: фундамент игровой сцены
- Выполнено: 12 из 16 MVP-итераций; итерация 13 реализована и проверена автоматически, ожидает ручной проверки и коммита
- Репозиторий: Git инициализирован
- Unity-проект: создан на Unity 6.4 с URP и DOTS-пакетами
- Цель MVP: трёхминутная survivor-сессия с 20 000 активных врагов при стабильных 60 FPS

> [!IMPORTANT]
> **Текущая задача — итерация 13: Game balance.**
>
> Итерации 11–12 закоммичены одним коммитом (`8312dd8`). Итерация 13 остаётся в рабочем дереве. После ручной проверки и коммита переходить к итерации 14.

## Правила итерации

1. В работе находится только одна итерация.
2. Итерация должна завершаться состоянием, которое чисто импортируется и компилируется в Unity.
3. Перед handoff проверяются diff, компиляция в Unity и релевантные автоматические тесты. Тесты запускаются только из консоли, без ручного взаимодействия с Unity UI. Standalone build и ручные проверки, включая Play Mode, выполняет разработчик, если он явно не попросил иное.
4. Codex не выполняет `git commit`, а отдаёт одну готовую строку Conventional Commit.
5. После ручного коммита текущей становится следующая незавершённая итерация.

## Текущая итерация

### 13. Game balance — READY FOR MANUAL CHECK

- [x] Заменить постоянные 200 spawn/s кривой `rate(t) = initial + (peak − initial) · (t / ramp)^exponent`: 3 → 425 врагов/с за 160 с, exponent 2.5, дальше peak. Budget пополняется точным интегралом кривой по `SpawnState.Elapsed`, поэтому расписание не зависит от FPS и длинных кадров; рестарт сбрасывает часы кривой.
- [x] Отражать ось spawn-смещения, выходящую за арену: у стен и в углах враги появляются на полном spawn radius, а не на игроке. Clamp остаётся запасным вариантом для арены уже spawn radius.
- [x] Сбалансировать врагов, оружие и contact damage: скорость врагов 3 → 2.25, HP 3 → 2; cooldown оружия 0.2 → 0.15; HP игрока 10 → 12. Контакт (1 урон / 0,5 с), дальность, скорость и lifetime пуль не менялись. Значения синхронизированы в defaults, `Enemy.prefab` и `ArenaSubScene`.
- [x] Показывать HP игрока в Survival HUD; изменение HP обновляет текст сразу, без ожидания окна sampling.
- [x] Проверить полные сессии через весь pipeline с defaults: AFK, круг без уклонения и трёхминутный orbit-прогон до cap и победы.
- [x] Покрыть кривую, интеграл, независимость от FPS, профиль по умолчанию, отражение spawn, сброс часов кривой при рестарте и HP в HUD.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

Подбор параметров: офлайн C#-прототип того же порядка систем (spawn → movement → grid → weapon → hit → projectile → contact → damage → death → ECB) на 60 Гц с ботами: AFK, круг радиусом 30 без уклонения и lookahead-бот (24 направления, горизонт 0,9 с), 20 seed × 3 набора весов. Прототип лежит вне репозитория. Бот — грубая замена игрока, а не оценка «честной» сложности.

| Профиль | AFK | Круг | Lookahead-бот | Враги 0:30 / 1:00 / 1:30 / 2:00 / 2:30 / 3:00 |
|---|---|---|---|---|
| До: 200/s, speed 3, HP 3, cooldown 0.2, 10 HP | поражение 17,6 с | 9 с | 0/60, ~18 с | cap не достигается |
| После | поражение ~41 с | ~58 с | 35/60 побед, у победителей в среднем 8,3 HP | ~100 / 680 / 2,6k / 7,1k / 15,5k / 20k, cap ≈ 2:41 |

Наблюдения: с clamp-спавном у стен тот же профиль давал 0/18 побед бота; разброс скорости врагов ±20% снижал победы до 3/18, поэтому он не добавлен. Урон бот получает в основном между 1:00 и 2:00. После cap новых волн почти нет, поэтому финал с 20k зрелищнее, но легче середины: без separation весь рой стягивается в след за игроком.

Проверка: Unity 6000.4.5f1 из консоли в batchmode импортировал assets и скомпилировал runtime/test assemblies без ошибок. С `-burst-force-sync-compilation` прошли все 129 Edit Mode-тестов (114 исходных и 15 новых), exit code 0; без ошибок Burst, job safety и предупреждений о порядке систем. Полные сессии в Unity (категория `Balance`) совпали с прототипом: AFK — Lost на 41,05 с; orbit без уклонения — Lost на 55,75 с; трёхминутный orbit-прогон с неограниченным HP — лимит 20 000 на 161,05 с, 20 242 spawn (242 убитых восполнены), Won на 180 с. Весь набор выполняется ~3 с. `git diff --check` прошёл. Открытого Editor на момент проверки не было; standalone build и Play Mode не запускались.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-balance.log`, `Logs/balance-results.xml`.

Ручные проверки разработчику:
- Сыграть 2–3 полные сессии в Main. Первые ~30 с оружие успевает за врагами, давление растёт после 1:00, к ≈ 2:41 на арене 20 000 врагов. Победа должна быть достижима при активном уклонении; AFK проигрывает примерно за 40 с.
- Постоять у стены и в углу: новые враги не появляются рядом с игроком.
- HP в HUD уменьшается сразу при контакте; R и экран результата работают как раньше.
- Stress-пресеты: заполнение и refill не изменились; позиции spawn у стен теперь отражаются, seed по-прежнему повторяем.
- Оценить читаемость роя 20k и субъективную сложность. Основные ручки: `peakSpawnRate`/`rampExponent` в `ArenaSubScene` и `movementSpeed` в `Enemy.prefab`.

Коммит: `feat(balance): tune three-minute swarm`

## Завершённые итерации

### 12. Benchmark controls — COMPLETE

- [x] Добавить HUD с количеством активных enemies/projectiles и средним frame time в миллисекундах.
- [x] Добавить stress-пресеты 1k, 10k, 20k и 50k, доступные кнопками и клавишами 1–4; 0 возвращает Survival.
- [x] Переключать пресеты через полный session restart; R повторяет выбранный режим.
- [x] Использовать фиксированный seed = 1; заполнять выбранный лимит и пополнять потери.
- [x] Сохранять исходные SpawnConfig и правила Survival; stress делает игрока неуязвимым и отключает трёхминутное завершение, сохраняя combat врагов.
- [x] Заменить IMGUI на retained uGUI/TextMeshPro с постоянными queries и char buffer без создания строк при обновлении показаний.
- [x] Покрыть все четыре лимита, refill, переключение вниз и в Survival, seed/restart, invulnerability, sample math, UI callbacks и managed allocations.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

Проверка: Unity 6000.4.5f1 из консоли в batchmode импортировал assets и скомпилировал runtime/test assemblies без ошибок. С `-burst-force-sync-compilation` прошли все 114 Edit Mode-тестов (98 исходных и 16 новых), exit code 0; без ошибок Burst, job safety и предупреждений о порядке систем. Проверки sampling/formatting и повторного обновления TMP-текста после прогрева показали 0 managed allocations на текущем потоке. Это не замер GC/FPS всего отрисованного кадра. `git diff --check` прошёл. Открытого Editor на момент проверки не было; standalone build и Play Mode не запускались.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-benchmark.log`, `Logs/benchmark-results.xml`.

Ручные проверки разработчику:
- В Main проверить читаемость HUD и экрана результата при разных размерах Game view, работу кнопок и клавиш 0–4/R.
- Выбрать каждый stress-пресет: после reset/population врагов около выбранного лимита; погибшие восполняются на следующем simulation update, projectiles отражают живые пули. Проверить движение/стрельбу и сохранение HP игрока при контакте.
- Переключить 50k → 1k → Survival, повторить R: без остатков предыдущей сессии; в Survival снова работают contact damage и победа/поражение.
- Сверить frame avg и GC в Profiler после прогрева. HUD показывает среднее реальных длительностей кадров, включая ожидания/VSync, а не CPU/GPU work time или p95; целевые FPS и standalone benchmark относятся к итерации 15.

Коммит: общий с итерацией 11, `feat(gameplay): add survival and benchmark modes` (`8312dd8`)

### 11. Survival game flow — COMPLETE

- [x] Добавить baked GameSession со статусами Running, Won и Lost и таймером 180 секунд.
- [x] Определять результат после damage/death; при одновременном истечении таймера и смерти игрока выбирать Lost.
- [x] Останавливать spawn, движение, grid, оружие, пули, контактный урон и combat после завершения.
- [x] Добавить рестарт через R и кнопку на экране результата в Main.
- [x] Полностью очищать enemies/projectiles, включая disabled entities; сохранять prefab и baked окружение.
- [x] Восстанавливать начальные transform/health игрока, cooldown, damage buffers, input, spawn budget/sequence и таймер.
- [x] Покрыть таймер, приоритет поражения, остановку pipeline, массовую очистку, отложенные ECB, повторные рестарты и воспроизводимость seed.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

Проверка: Unity 6000.4.5f1 в batchmode импортировал assets и скомпилировал runtime/test assemblies без ошибок. Запуск из консоли с `-burst-force-sync-compilation`: все 98 Edit Mode-тестов прошли (86 исходных и 12 новых), без ошибок Burst, job safety и предупреждений о порядке систем; exit code 0. Открытого Editor на момент проверки не было. `git diff --check` прошёл. Standalone build и Play Mode не запускались.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-gameflow.log`, `Logs/gameflow-results.xml`.

Ручные проверки подтверждены пользователем 2026-09-19:
- В `Main` проверить отсчёт от 03:00, WASD и стрельбу во время Running.
- При смерти игрока проверить экран Defeat и остановку движения, стрельбы и spawn; при выживании 180 секунд — You survived! и такую же остановку.
- Проверить кнопку Restart и клавишу R, в том числе R во время Running: игрок возвращается на старт с исходным HP, враги/пули исчезают, таймер возвращается к 03:00. Повторить несколько раз.

Коммит: общий с итерацией 12 (`8312dd8`)

### 10. Player health — COMPLETE

- [x] Добавить baked `Health` и `DynamicBuffer<DamageEvent>` игроку.
- [x] Настроить начальное здоровье, входящий контактный урон и интервал защиты в `PlayerAuthoring` и `ArenaSubScene`.
- [x] Реализовать Burst `PlayerContactDamageSystem`: поиск живого контакта на XZ через spatial grid после движения и запись события в буфер игрока перед `DamageSystem`.
- [x] Исключить гонки и суммирование контактов: одна job invocation владеет буфером и cooldown игрока, не более одного события за update/интервал независимо от числа врагов.
- [x] Покрыть границы радиуса и клеток, cooldown, 10 000 контактов, смерть, порядок систем, одновременный урон пули и контакт, повторный rebuild grid.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

Проверка: Unity 6000.4.5f1 в batchmode импортировал assets и скомпилировал проект без ошибок. Запуск из консоли с `-burst-force-sync-compilation`: все 86 Edit Mode-тестов прошли (62 исходных и 24 новых), без ошибок Burst, job safety и предупреждений о порядке систем; exit code 0. Открытого Editor на момент проверки не было. `git diff --check` прошёл. Standalone build и Play Mode не запускались.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-player-health.log`, `Logs/player-health-results.xml`.

Ручные проверки разработчику:
- В `Main` наблюдать `Health.Current` игрока в Entities: стартовое значение 10, контакт снимает 1 HP не чаще раза в 0,5 с независимо от числа врагов.
- Проверить контакт и выход из него при WASD-движении, сохранение интервала защиты при повторном входе и работу стрельбы.
- На момент итерации 10 при 0 HP игрок оставался в мире и больше не получал контактный урон; движение и оружие продолжали работать. Итерация 11 добавляет остановку и рестарт.

Коммит: `feat(player): add contact damage and health` (`71ab11c`)

### 9. Combat pipeline — COMPLETE

- [x] Добавить baked `Health` и `DynamicBuffer<DamageEvent>` врагам, `ProjectileCombat` с уроном и радиусом — пулям.
- [x] Реализовать Burst `ProjectileHitSystem`: проверку swept segment на плоскости XZ через spatial grid, первое пересечение и стабильный выбор при равных временах попадания.
- [x] Ограничить проверяемый путь оставшимся lifetime, исключить повторное попадание и двойное удаление при одновременном попадании и expiry.
- [x] Реализовать отдельный `DamageSystem`: одну job для переноса попаданий в буферы, затем параллельное применение урона по врагам и очистку буферов.
- [x] Реализовать отдельный `DeathSystem`, удаляющий врагов через ECB `ParallelWriter` в конце Simulation.
- [x] Добавить тесты геометрии, lifetime, порядка систем, конкурентных попаданий, смерти, rebuild grid и полного цикла выстрел → урон → смерть.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

Проверка: Unity 6000.4.5f1 в batchmode импортировал assets и скомпилировал проект без ошибок. Финальный запуск из консоли с `-burst-force-sync-compilation`: все 62 Edit Mode-теста прошли (33 исходных и 29 новых), без ошибок Burst, job safety и предупреждений о порядке систем. Открытого Editor на момент проверки не было. Standalone build и Play Mode не запускались.

Новые тесты включают 10 000 попаданий в одного врага без потери/повтора урона, удаление 10 000 врагов с overkill, сохранность prefab, сравнение grid hit с независимым brute-force oracle, границы клеток, совпадающие позиции, касание, длинный шаг, expiry и полный отсортированный цикл после удаления игрока.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-combat.log`, `Logs/combat-results.xml`.

Ручные проверки разработчику:
- В `Main` дождаться врагов в радиусе оружия: попавшая пуля исчезает; при стандартных настройках враг исчезает после трёх попаданий.
- Проверить стрельбу и попадания при WASD-движении, а также истечение lifetime у промахнувшихся пуль.
- На момент завершения итерации 9 здоровье игрока и contact damage ещё не были реализованы.

Коммит: `feat(combat): add buffered damage and death` (`0df5392`)

### 7. Automatic weapon — COMPLETE

- [x] Добавить baked `Weapon` и `WeaponState` с настраиваемыми cooldown, range, скоростью и lifetime пуль.
- [x] Реализовать Burst `WeaponSystem` и поиск ближайшего врага через spatial grid во всех клетках радиуса, с отсечением по дистанции и стабильным выбором при равных расстояниях.
- [x] Создавать projectile entities через ECB `ParallelWriter`, возвращать reader dependency владельцу grid.
- [x] Создать Projectile prefab с baker и материалом, подключить оружие к Player в `ArenaSubScene`.
- [x] Покрыть cooldown, границу радиуса, отсутствие цели, совпадающие позиции, сравнение с brute-force oracle и rebuild после чтения grid.

### 8. Projectile simulation — COMPLETE

- [x] Реализовать движение пуль через Burst `ISystem` и параллельный `IJobEntity`.
- [x] Добавить lifetime и удаление через ECB `ParallelWriter`, включая нулевой lifetime и большой шаг времени.
- [x] Ограничить движение оставшимся lifetime; продолжать обработку пуль после удаления игрока.
- [x] Покрыть движение, expiry, сохранность prefab, удаление 10 000 пуль и общий цикл выстрел → движение → удаление в отсортированной группе систем.

Проверка: Unity 6000.4.5f1 в batchmode импортировал assets и скомпилировал проект без ошибок. Финальный запуск из консоли с `-burst-force-sync-compilation`: все 33 Edit Mode-теста прошли (17 исходных и 16 новых), без предупреждений о порядке систем. Открытого Editor на момент проверки не было. Standalone build и Play Mode не запускались.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-weapon-projectile.log`, `Logs/weapon-projectile-results.xml`.

Общий коммит: `feat(weapon): add auto fire and projectile expiry` (`bcfcf84`)

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
- Пули создаются в конце Simulation через ECB и начинают движение/попадания на следующем update. Порядок: enemy movement → grid → weapon → projectile hit → projectile movement → player contact → damage → death → transforms → EndSimulation ECB; weapon и player contact также ждут PlayerMovementSystem.
- Hit detection проверяет отрезок предстоящего движения пули, ограниченный оставшимся lifetime, против текущих позиций врагов после их движения. Движение врага внутри шага не sweep-ится. При нулевом/отрицательном deltaTime попадания не рассчитываются.
- Радиус врага MVP фиксирован в мировых единицах: `Enemy.CollisionRadius = 0.4`, соответствует prefab диаметром 0.8. Радиус пули настраивается на prefab независимо от визуального scale, по умолчанию 0.125; начальные здоровье врага и урон пули — 2 и 1 (до итерации 13 здоровье было 3).
- Параллельный hit job пишет только состояние своей пули; одиночный transfer job переносит попадания в `DynamicBuffer<DamageEvent>` цели без конкурентной записи. Затем параллельный damage job применяет урон с насыщением до нуля и очищает буфер. Дополнительного ECB playback/sync point между hit и damage нет.
- Попадание обнуляет lifetime пули; только `ProjectileMovementSystem` ставит её удаление в ECB. `DeathSystem` удаляет только врагов, оставляя будущую обработку смерти игрока игровому flow.
- Контакт игрока проверяется по текущим позициям на XZ без sweep, с включённой границей суммы радиусов: `Player.CollisionRadius = 0.5`, `Enemy.CollisionRadius = 0.4`. Учитываются только враги из grid с положительным `Health.Current`.
- Настройки игрока по умолчанию: 12 HP (до итерации 13 — 10), контактный урон 1, интервал 0,5 с. Входящий урон одинаков для всех MVP-врагов и задаётся на `PlayerAuthoring`; `PlayerContactState` хранит общий cooldown игрока. Отсутствие контакта не запускает cooldown, выход из контакта не сбрасывает его; длинный кадр не накапливает очередь ударов, нулевой/отрицательный deltaTime не наносит урон и не меняет cooldown.
- Contact job пишет только собственный `DynamicBuffer<DamageEvent>` игрока и возвращает dependency владельцу grid. `DamageSystem` применяет контактные и projectile-события общим проходом: живой враг, убитый пулей в этом же update, ещё может нанести контактный урон. Уже мёртвый враг или игрок не создаёт новых контактов. Здоровье игрока насыщается до нуля, entity сохраняется для итерации game flow.
- `GameConfigAuthoring` также печёт `GameSession` и `BenchmarkState`. `GameSessionStartSystem` выполняется первым в Simulation и сохраняет начальные transform/health после загрузки игрока; до этого сессия не тикает. `GameSessionEndSystem` после damage/death определяет результат; смертельный урон имеет приоритет над истечением таймера в том же update. Таймер использует simulation deltaTime, не уменьшается при отрицательном шаге и ограничен 180 секундами.
- После Won/Lost gameplay-системы и grid перестают обновляться. ECB завершающего кадра штатно проигрывается в EndSimulation; его entities также удаляются при рестарте. При отсутствии GameSession сохраняется прежнее поведение изолированных систем и тестов.
- Рестарт обрабатывается до gameplay следующего кадра: удаляются все Enemy/Projectile, включая disabled, но без Prefab; восстанавливаются игрок и изменяемое состояние сессии. Кадр рестарта не двигает симуляцию и оставляет таймер на нуле; grid в нём очищается обычным rebuild с соблюдением reader dependencies. Capacity grid переиспользуется. Следующий кадр начинает новую сессию с тем же random seed и SpawnSequence = 0.
- `GameSessionBridge` в Main — MonoBehaviour boundary для HUD, результата, restart и выбора пресетов. В итерации 12 IMGUI заменён на uGUI/TextMeshPro. Минимальные TMP font/settings/shader resources импортированы из уже установленного `com.unity.ugui`, лицензия Liberation Sans сохранена; новые пакеты не добавлялись.
- `BenchmarkState` по умолчанию Survival. Выбор пресета вызывает reset до gameplay; режим stress использует отдельный фиксированный seed = 1 и целевое число врагов, не меняя baked SpawnConfig. SpawnSystem заполняет дефицит через существующий EndSimulation ECB, включая пополнение погибших со следующего update. DamageSystem очищает события игрока без уменьшения HP, продолжая обычный урон врагам; GameSessionEndSystem не завершает stress-сессию. Возврат в Survival полностью восстанавливает обычные правила.
- HUD использует кешированные queries, считает только активные entities без prefab в LateUpdate после ECB и обновляет числа примерно раз в 0,25 с, а при изменении HP игрока — сразу. Frame avg — арифметическое среднее `Time.unscaledDeltaTime`, независимое от timeScale и simulation delta; reset и первичное заполнение исключаются из окна. Форматирование и TMP SetCharArray переиспользуют буфер; managed allocations при числовом refresh проверены тестами. Полная стоимость рендера HUD/GC остаётся предметом профилирования.
- Survival spawn rate задаётся кривой `initial + (peak − initial) · (t / ramp)^exponent` по времени spawn-часов `SpawnState.Elapsed`, которые идут только во время Running и сбрасываются рестартом. Budget пополняется разностью замкнутого интеграла кривой между началом и концом update, поэтому число врагов к моменту t одинаково при любом FPS; дробный остаток переносится, при достигнутом лимите budget обнуляется, как раньше. `rampDuration = 0` означает постоянный initial rate. Stress-пресеты кривую не используют.
- Spawn-позиция остаётся на окружности spawn radius вокруг игрока. Если точка выходит за арену по оси, смещение по этой оси зеркалится; clamp к арене применяется только после этого, для арен уже spawn radius.
- Баланс по умолчанию (итерация 13): 3 → 425 врагов/с за 160 с, exponent 2.5, spawn radius 40, лимит 20 000; враг speed 2.25 и 2 HP; оружие cooldown 0.15, range 20, пули 30 ед./с и 2 с lifetime, урон 1; игрок speed 10 и 12 HP. Контракт покрыт тестами: AFK проигрывает за 20–60 с, orbit без уклонения проигрывает до конца, лимит достигается за 10–30 с до конца сессии.
- Финальные показатели снимаются в standalone build, а не в Editor.
