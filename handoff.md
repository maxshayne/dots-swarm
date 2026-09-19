# DOTS Swarm — Handoff

Последнее обновление: 2026-09-20

## Состояние проекта

- Этап: фундамент игровой сцены
- Выполнено: 14 из 16 MVP-итераций; итерация 15: цель p95 ≤ 16,67 мс при 20 000 врагов подтверждена release-бенчмарком (p95 4,0–4,7 мс), ждёт коммита результатов
- Репозиторий: Git инициализирован
- Unity-проект: создан на Unity 6.4 с URP и DOTS-пакетами
- Цель MVP: трёхминутная survivor-сессия с 20 000 активных врагов при стабильных 60 FPS

> [!IMPORTANT]
> **Текущая задача — итерация 15: Performance target.**
>
> Код итерации 15 закоммичен (`02372d5`). Три release-прогона `-benchmark 20k` выполнили цель с запасом ~3,5× (худший p95 4,74 мс), дальнейшие оптимизации не нужны. В рабочем дереве — только результаты в handoff. После коммита переходить к итерации 16.

## Правила итерации

1. В работе находится только одна итерация.
2. Итерация должна завершаться состоянием, которое чисто импортируется и компилируется в Unity.
3. Перед handoff проверяются diff, компиляция в Unity и релевантные автоматические тесты. Тесты запускаются только из консоли, без ручного взаимодействия с Unity UI. Standalone build и ручные проверки, включая Play Mode, выполняет разработчик, если он явно не попросил иное.
4. Codex не выполняет `git commit`, а отдаёт одну готовую строку Conventional Commit.
5. После ручного коммита текущей становится следующая незавершённая итерация.

## Текущая итерация

### 15. Performance target — TARGET MET, READY FOR COMMIT

- [x] Считать p95 кадра: `FrameTimeHistory` хранит последние длительности кадров в кольце фиксированного размера и выдаёт avg/p50/p95/p99/max методом nearest rank. Массивы выделяются один раз; сводка сортирует копию без managed allocations. HUD показывает p95 за последние 600 кадров (~10 с при 60 FPS) рядом со средним за 0,25 с.
- [x] Добавить автоматический бенчмарк для standalone: `dots-swarm.exe -benchmark 20k [-benchmark-warmup 20] [-benchmark-duration 30] [-benchmark-output <file.csv>]`. `BenchmarkRunner` отключает VSync и frame cap, включает stress-пресет, ждёт кадра с заполненным роем, прогревает, пишет каждый кадр по `Time.unscaledDeltaTime`, дописывает строку в CSV и закрывает player. Коды выхода: 0 — записано, 1 — неверные аргументы, 2 — нет сессии, 3 — ошибка записи.
- [x] Убрать main-thread sync point в `GameSessionEndSystem`: чтение `Health` игрока на главном потоке ждало всю цепочку hit → transfer → damage, и `TransformSystemGroup` не мог запланироваться раньше. Итог сессии считает Burst-job.
- [x] Закрепить `PlayerMovementSystem` перед `EnemyMovementSystem`: её main-thread foreach по `LocalTransform` иначе завершал бы jobs движения врагов и grid. В default world порядок и раньше был таким, но только по hash-сортировке; поведение не меняется.
- [x] Отключить per-object motion vectors у Enemy и Projectile (`m_MotionVectors: 0`, Camera). Камера без TAA и motion blur их не рисует, а Entities Graphics пекла `unity_MatrixPreviousM` (64 байта на entity), каждый кадр копировала матрицы всего роя в `MatrixPreviousSystem` и загружала их на GPU. Изображение не меняется.
- [x] Аудит без изменений кода. Managed allocations в кадре нет: HUD и bridges покрыты тестами. Grid растёт геометрически: до 32 768 элементов около 9 resize за сессию, не каждый кадр. Structural changes в установившемся режиме — только ECB-команды выстрелов, смертей и spawn, единицы за кадр. Одномоментное заполнение stress-пресета в замер не входит.
- [x] Собрать release standalone build и снять бенчмарк на зафиксированном железе (разработчик): три прогона, p95 4,0–4,7 мс. Profiler-сессия не понадобилась.
- [x] Если p95 > 16,67 мс, выбрать рычаг по профилю и повторить замер — не понадобилось.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

CSV по умолчанию — `benchmark-results.csv` в `Application.persistentDataPath`; относительный `-benchmark-output` считается от рабочей директории. Строка содержит avg/p50/p95/p99/max, признак цели p95, min/max врагов за запись, разрешение, VSync, тип сборки (`release`/`development`/`editor`), graphics API, GPU, CPU и версию Unity. Повторные запуски дописывают строки под одним заголовком.

Проверка: Editor закрыт, Unity 6000.4.5f1 из консоли в batchmode импортировал assets и скомпилировал runtime/test assemblies без ошибок и предупреждений; все прогоны с `-burst-force-sync-compilation`. Диагностический Play Mode-прогон до правок напечатал порядок Simulation в default world (`PlayerMovementSystem` уже шла перед `SpawnSystem`, `EnemyMovementSystem` и grid) и подтвердил, что prefabs пеклись с `MotionMode = Object`. Edit Mode: 153/153 (129 исходных и 24 новых), exit code 0. Play Mode: 11/11 (8 исходных и 3 новых), ~20 с, exit code 0; тест runner записал в Editor 1k-прогон (173 кадра, p95 6,9 мс — не показатель цели). При завершении Editor после Play Mode в логе, как и в итерации 14, `Leak detected: 2 preview scene(s) were not closed prior to shutdown`. Без ошибок Burst, job safety и предупреждений о порядке систем. `git diff --check` прошёл. Standalone build и бенчмарк на железе выполнил разработчик (результат ниже).

Первый полный Play Mode-прогон упал: `EntityQuery.GetSingleton` в этой версии Entities только проверяет safety и не завершает jobs, а `GameSessionBridge` читал `GameSession`, который теперь пишет job. Bridge и runner вызывают `CompleteDependency()` у query перед чтением.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-perf-order.log`, `Logs/perf-order-results.xml`, `Logs/verify-perf-editmode.log`, `Logs/perf-editmode-results.xml`, `Logs/verify-perf-playmode.log`, `Logs/perf-playmode-results.xml`.

Протокол замера:
1. Собрать Windows x64 standalone **без** Development Build. Закрыть фоновые приложения; ноутбук — от сети.
2. Трижды подряд запустить `Builds\dots-swarm.exe -benchmark 20k -benchmark-output <file.csv>`: 20 с прогрева и 30 с записи. FullScreenWindow берёт родное разрешение экрана; оно попадает в CSV.
3. Цель выполнена, если во всех трёх строках `build = release`, `enemies_min` около 20 000 (убитых восполняет следующий update) и `p95_target_met = true` (`p95_ms ≤ 16.67`).
4. При промахе — тот же запуск в Development Build с Profiler; строка пометится `development`. Определить, упирается ли кадр в GPU, render thread или main thread.

Результат замера 2026-09-20 (release, Direct3D12, 2560×1440, VSync off, Unity 6000.4.5f1; NVIDIA GeForce RTX 4070, Intel Core i7-12700). Stress 20k, 20 с прогрева, 30 с записи; CSV лежат в `Builds/file.csv`, `file1.csv`, `file2.csv` (Git игнорирует):

| Прогон | Кадров | avg, мс | p50, мс | p95, мс | p99, мс | max, мс | Врагов min–max |
|---|---|---|---|---|---|---|---|
| 1 | 10 138 | 2,96 | 3,02 | 4,05 | 4,67 | 8,43 | 19 999–20 000 |
| 2 | 10 245 | 2,93 | 2,99 | 3,98 | 4,65 | 11,07 | 19 999–20 000 |
| 3 | 9 930 | 3,02 | 2,94 | 4,74 | 5,61 | 12,36 | 19 999–20 000 |

Цель `p95 ≤ 16,67 мс` выполнена во всех прогонах с запасом ~3,5× (~330 FPS в среднем); даже самый долгий кадр укладывается в бюджет 60 FPS. Границы замера: stress-пресет с неуязвимым игроком, который стоит на месте, поэтому к записи рой стянут к нему; одна машина. По числу врагов сценарий совпадает с пиком Survival (20 000 с ~2:41), но движение игрока и полный трёхминутный Survival в release не замерялись.

Запас на будущее, если понадобится (например 50k или слабое железо): у Enemy `m_CastShadows: 1` при shadow distance 50, и весь рой рисуется второй раз в shadow map; меш врага — встроенная Sphere (768 треугольников, ~15 млн треугольников за проход при 20k). Оба рычага меняют картинку.

Ручные проверки разработчику:
- Открыть проект в Editor: импорт и компиляция без ошибок и предупреждений; SubScene перепекается из-за смены motion vectors.
- В Main HUD показывает `Frame avg` и `p95`; p95 сбрасывается при рестарте и смене пресета. Кнопки, клавиши 0–4/R и экран результата работают как раньше.
- Enemy и Projectile выглядят как раньше.
- Изменения рабочего дерева после сборки не относятся к итерации: `ProjectSettings.asset` (`preloadedAssets` с project-wide Input Actions, Input System добавляет их при сборке) и пустая папка `Assets/Settings/Build Profiles` с `.meta`. Решить, коммитить ли их.

Коммиты: `perf(benchmark): add p95 runner, trim frame costs` (`02372d5`), затем `docs(benchmark): record 20k release p95 results` (handoff).

## Завершённые итерации

### 14. Gameplay integration tests — COMPLETE

- [x] Добавить Play Mode-сборку `DotsSwarm.Tests.PlayMode` и harness `MainSceneSession`: additive-загрузка `Main`, ожидание стриминга `ArenaSubScene` и инициализации сессии в default world, выгрузка с проверкой остатков. Шаг симуляции зафиксирован на 1/60 с через `Time.captureDeltaTime`, поэтому симулированное время не зависит от FPS Editor/batchmode.
- [x] Подавать ввод с виртуальной клавиатуры `InputTestFixture` через настоящие `PlayerInputBridge` и `GameSessionBridge`: WASD, R, 0–4, кнопка Restart на экране результата.
- [x] Покрыть spawn, movement, shot, damage и death в Main: baked defaults сцены; скорость, диагональ, остановку и стену; spawn по интегралу кривой в точках seed на кольце, преследование на полный шаг и `LocalToWorld` новых врагов и пуль в первом кадре; выстрел в ближайшего врага и убийство двумя попаданиями; контактный урон раз в 0,5 с до Lost на 20–60 с с HP в HUD в том же кадре; заморозку симуляции после Lost.
- [x] Проверить рестарт и очистку ECS: кнопка Restart после Lost, R во время Running, 1k stress → Survival; враги и пули (включая disabled) удалены, игрок, cooldown, contact, spawn и таймер сброшены, prefabs сохранены, новая сессия повторяет seed с `SpawnSequence = 0`. Выгрузка Main не оставляет session entities, включая созданные в runtime.
- [x] Привести тестовые префабы Edit Mode к baked-архетипу с `LocalToWorld`: незакоммиченная правка `SpawnSystem`/`WeaponSystem` ставит `LocalToWorld` через ECB при instantiate и без этого роняла 21 Edit Mode-тест.
- [x] Завершить компиляцию Unity, автоматические тесты и проверку diff.

Особенность `InputTestFixture`: delta-событие клавиши копирует байты массива клавиш от начала до изменённой, поэтому второе событие в том же update возвращает отпущенную первым клавишу. Тесты меняют одну клавишу за update (`Press`/`Release` с `yield`). Fixture включает leak detection со стеками для всех native-аллокаций; harness возвращает прежний режим.

Проверка: Editor закрыт пользователем, Unity 6000.4.5f1 из консоли в batchmode импортировал assets и скомпилировал runtime/test assemblies без ошибок; оба прогона с `-burst-force-sync-compilation`. Play Mode: 8/8 (новые), ~10 с, exit code 0. Edit Mode: 129/129, exit code 0; до правки тестовых префабов было 108/129 из-за `LocalToWorld`. Без ошибок Burst, job safety и предупреждений о порядке систем. При завершении Editor после Play Mode-прогона лог содержит `Leak detected: 2 preview scene(s) were not closed prior to shutdown`; в Edit Mode-прогоне этого нет, тесты сами preview scenes не создают, источник не выяснен. `git diff --check` прошёл. Standalone build не запускался; в открытом Editor импорт новой сборки не проверялся.

Артефакты проверки (локальные, Git игнорирует): `Logs/verify-playmode.log`, `Logs/playmode-results.xml`, `Logs/verify-editmode-integration.log`, `Logs/editmode-integration-results.xml`.

Ручные проверки разработчику:
- Открыть проект в Editor: `DotsSwarm.Tests.PlayMode` импортируется и компилируется без ошибок и предупреждений.
- В Main новые враги и пули появляются сразу в точке spawn/у игрока, без вспышки в центре арены.
- `DotsSwarmRenderPipeline.asset` (prefiltering Forward+/additional lights) и `UnityConnectSettings.asset` (`m_Enabled: 1`) закоммичены вместе с итерацией.

Коммиты: `fix(render): set LocalToWorld on spawned entities` (`49dd015`), затем `test(gameplay): cover full survival pipeline` (`539b981`)

### 13. Game balance — COMPLETE

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

Коммит: `feat(balance): tune three-minute swarm` (`53994c3`)

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
- `SpawnSystem` и `WeaponSystem` при instantiate через ECB задают и `LocalTransform`, и `LocalToWorld`: playback идёт после `TransformSystemGroup`, иначе первый отрисованный кадр берёт матрицу prefab. Поэтому prefabs врага и пули обязаны иметь `LocalToWorld`; baked Dynamic prefabs его имеют, тестовые prefabs в Edit Mode повторяют этот архетип.
- Play Mode-тесты (категория `Integration`) гоняют настоящую Main в default world с фиксированным шагом 1/60 с; код теста выполняется между двумя полными update. Ввод идёт через `InputTestFixture` и bridges, по одному изменению клавиши за update. Edit Mode-наборы остаются местом для граничных случаев отдельных систем.
- p95 кадра — nearest rank (наименьший кадр, не короче которого p% выборки) по реальным длительностям кадров `Time.unscaledDeltaTime`: включает ожидание GPU и present, не зависит от timeScale. Цель MVP измеряет только `BenchmarkRunner` в release standalone с выключенными VSync и frame cap: stress-пресет 20k, 20 с прогрева, 30 с записи, три запуска. HUD-p95 в Editor — только ориентир.
- `GameSessionEndSystem` не читает здоровье игрока на главном потоке: итог сессии считает job. Main-thread читатели `GameSession` вне систем (`GameSessionBridge`, `BenchmarkRunner`) вызывают `CompleteDependency()` у своей query: singleton-методы `EntityQuery` только проверяют safety. `SystemAPI` и `EntityManager` завершают jobs сами.
- Системы с main-thread доступом к `LocalTransform` (`PlayerMovementSystem`, чтение позиции игрока в Spawn и EnemyMovement) идут раньше систем, которые планируют jobs над `LocalTransform`.
- Рендер-prefabs роя не используют per-object motion vectors (`MotionVectorGenerationMode.Camera`), пока в проекте нет TAA и motion blur.
- Эталонное железо цели MVP: NVIDIA GeForce RTX 4070, Intel Core i7-12700, Windows 11, Direct3D12, 2560×1440. Release-замер итерации 15 на нём: stress 20k, p95 4,0–4,7 мс в трёх прогонах.
