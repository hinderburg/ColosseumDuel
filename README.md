# Colosseum Duel

Тактическая дуэльная арена: два игрока выставляют по три гладиатора и дерутся один на один.
Ход состоит из фазы планирования и фазы действия, решения принимаются вслепую и исполняются
одновременно. Против бота, в браузере.

**▶ Играть: _(ссылка появится после первого деплоя)_**

Unity-порт веб-прототипа. Дизайн — [`GDD.md`](GDD.md), состояние работ и журнал найденных
проблем — [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md).

## Управление

| Действие | Как |
|---|---|
| Рывок | Потяни от гладиатора и отпусти — как рогатку. Во время натяжения показывается траектория с отскоками от стен |
| Защита | Кнопка «Защита» или `Space` |
| Способность | Кнопка «Способность» или `Q` (доступна только на полной шкале ярости) |
| Выбор гладиатора | Кнопки на экране выбора или `1` / `2` / `3` |

Способность не занимает ход — она прикрепляется к рывку или защите.

## Как устроен проект

Вся игровая логика — стейт-машина матча, физика, урон, способности, предметы, опасные
зоны, ИИ бота — лежит в [`Assets/Scripts/Core/`](Assets/Scripts/Core) и **не зависит от
сцены**: ни одного обращения к `GameObject`. Поэтому она целиком покрыта обычными
юнит-тестами и её можно менять, не запуская редактор.

Презентация ([`Gameplay/View`](Assets/Scripts/Gameplay/View)),
ввод и HUD ([`Gameplay/Hud`](Assets/Scripts/Gameplay/Hud)) читают состояние матча каждый
кадр и ничего не решают сами.

**Сцена и настройки проекта генерируются скриптом, а не хранятся как ручная работа.**
`Tools → Colosseum → Bootstrap project` создаёт URP-ассет, настройки плеера, палитру
материалов, процедурные текстуры и саму сцену арены. Пересобрать всё с нуля — одна команда.

## Сборка и проверка

Требуется Unity **6000.3.21f1** с модулем WebGL Build Support.

```bash
# настроить проект и собрать сцену
Unity.exe -batchmode -quit -projectPath . -executeMethod ColosseumDuel.EditorTools.ProjectBootstrap.RunAll

# тесты (логика — 40 штук, сцена и HUD — 31)
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml
Unity.exe -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults results.xml

# WebGL-билд в Build/WebGL
Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod ColosseumDuel.EditorTools.ProjectBootstrap.BuildWebGL
```

Собранный билд нельзя открыть по `file://` — загрузчик тянет `.data` и `.wasm` запросами,
которые браузер на файловой схеме блокирует. Для локальной проверки:

```bash
powershell -ExecutionPolicy Bypass -File Tools/serve-webgl.ps1
```

Билд сжат gzip с `decompressionFallback`: распаковщик встроен в загрузчик, поэтому он
работает на любом статическом хостинге без серверных заголовков — 11 МБ вместо 44 МБ
несжатых.

## Лицензии

Код — в этом репозитории. Шрифт Inter — SIL Open Font License 1.1,
см. [`Assets/Fonts/Inter-LICENSE.txt`](Assets/Fonts/Inter-LICENSE.txt).
