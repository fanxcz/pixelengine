# PixelEngine 2.0 — Stage 10 button audit

Дата: 2026-11-04
База: `PixelEngine-2.0-stage10-fixed2.zip`

## Результат

Проведён статический аудит интерактивных элементов `PixelEngine.Editor`: кнопок основной панели, Scene View, Game Preview, Output, Asset Browser, Prefab Browser, Component Inspector, Script Editor, Stage Tools, контекстных меню и Command Palette.

Проверены привязки обработчиков, путь от обработчика к операции проекта/runtime, условия доступности команд и поведение при отсутствии проекта, сцены или выделенного элемента. Также проверены 86 исходных C# файлов, 10 LIC JSON проектов и XML-файлы проектов.

## Что исправлено

1. **Защита несохранённых изменений.** New Project, Open и закрытие редактора спрашивают о сохранении. Отмена диалога сохранения отменяет соответствующее действие.
2. **Запуск игры.** Play не запускает Player, если сохранение отменено или завершилось ошибкой.
3. **Состояние кнопок проекта.** Save, Check, New Scene, New Script, Create Object, Fit, Scene Tools, Run и Stop отключаются, когда проект/сцена/процесс недоступны. Кнопки Run/Stop синхронны в верхней панели и во вкладке Game Preview.
4. **Scene View context menu.** Правый клик сначала выбирает объект под курсором; команды контекстного меню больше не применяются к старому выделению. Команды выбранного объекта выключены, если объект не выделен.
5. **Component Inspector.** Правый клик выбирает компонент под курсором перед открытием меню; пункты перемещения активны только при допустимом индексе.
6. **Prefab Browser.** Create/Update/Propagate и остальные действия отражают наличие проекта, выбранного объекта, выбранного prefab и связанных экземпляров.
7. **Asset Browser.** Import выключен без проекта; Rename/Assign/Export/Remove требуют выбранный ресурс; Play WAV доступен только для WAV, Stop — только при активном preview. Контекстное меню проверяет тип ресурса.
8. **Stage Tools.** Remove/Add Key/Fill/Flood Fill/Generate Cave доступны только при подходящем выбранном элементе и допустимом состоянии карты/палитры.
9. **Script Editor.** Find/Prev/Replace/All отключены, пока поисковая строка пуста.
10. **Output.** Clear/Export активны только при наличии записей, Copy — только при выбранной строке; экспорт обрабатывает ошибки доступа/файловой системы.
11. **Diagnostics.** При отсутствии проблем показывается явная строка `No issues found`, а не пустой список.
12. **Audio hint.** Устаревшее сообщение о неподдерживаемом `PlayOnAwake` убрано; подсказка соответствует текущему Runtime.

## Проверенные группы команд

- File: New, Open, Save, Save As, Exit.
- Edit: Undo, Redo, Command Palette.
- Scene/Node: создание объектов, переименование, фокус, дублирование, удаление, выравнивание, создание Script и Prefab.
- Scene toolbar: выбор инструментов Select/Move/Rotate/Scale, Snap, размер сетки, Grid, Labels, Colliders, Fit.
- Preview: Run, Stop, запуск/остановка из вкладки Game Preview.
- Project: Import Assets, Check Lua Scripts, Build Solution.
- Assets: Import, Rename, Assign, Export, WAV Play/Stop, Remove, drag-and-drop.
- Prefabs: Create, Duplicate, Rename, Edit Template, Update, Apply to Instances, Instantiate, Delete.
- Components: Add, Copy, Reset, Remove, Move Up/Down.
- Stage Tools: Diagnostics, Safe Fixes, Copy/Export report, Animation clips/keyframes, Lighting, Particle Emitters, TileMap New/Remove/Fill/Flood Fill/Generate Cave, Profiler.
- Script Editor: Find, Go to line, Completion, API Reference, Snippets, Reindent, Wrap, Zoom, Replace, context actions.
- Output: Filter, Search, Clear, Copy, Export.
- Dialogs/Command Palette: OK/Cancel, Enter/Double-click, keyboard shortcuts.

## Статические проверки

- C# files scanned: 86.
- LIC JSON files parsed: 10.
- Explicit `.Click` registrations: 46.
- Menu/context action handlers were traced separately.
- C# delimiter / source structure checks: PASS.
- LIC JSON and project XML structural checks: PASS.

## Ограничение проверки

Это аудит исходного кода и структурных связей, а не ручной UI-тест. В текущей среде нет .NET SDK, Windows Forms и графической Windows-сессии, поэтому здесь нельзя реально нажать все кнопки, проверить оконные диалоги или выполнить `dotnet build`. Финальная проверка компиляции и ручного клика должна быть выполнена на Windows.

## Проверка на Windows

Из корня проекта:

```powershell
 dotnet build .\PixelEngine.sln -c Debug
 dotnet run --project PixelEngine.SmokeTests -c Debug
 dotnet run --project PixelEngine.Editor -c Debug
```

В редакторе рекомендуется отдельно проверить: New/Open/Save и Cancel при несохранённых изменениях; Run/Stop в обеих панелях; правый клик по разным объектам и компонентам; импорт/экспорт ресурсов; все операции Prefab; Animation/Lighting/Particles/TileMap; Find/Replace; Clear/Copy/Export Output.
