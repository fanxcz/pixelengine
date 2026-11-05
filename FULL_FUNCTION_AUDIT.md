# PixelEngine 2.0 — полный аудит функций Stage 10

Дата: 2026-11-04  
База: `PixelEngine-2.0-stage10-buttons-audited.zip` / предыдущий Stage 10 Fixed 2.

## Итог

Проведён расширенный статический аудит PixelEngine Core, Runtime, редактора и связанных проектных данных. Проверялись не только кнопки, но и связи между данными Inspector/LIC, компонентами сцены, Runtime, Lua API, сериализацией, диагностикой и тестами.

В этой среде удалось выполнить структурные проверки исходников и данных. **Компиляция C#, запуск SmokeTests и реальные клики в WinForms здесь не выполнялись**: отсутствуют .NET SDK и Windows GUI. Поэтому результат ниже разделяет то, что проверено статически, и то, что должно быть подтверждено на Windows.

## Объём аудита и структурные проверки

- 86 файлов C# просканированы структурной проверкой; ключевые связи Core/Runtime/Editor дополнительно прослежены по исходникам.
- 10 файлов `.csproj` успешно разобраны как XML.
- 4 проекта, объявленные в `PixelEngine.sln`, существуют; все 6 `ProjectReference` указывают на существующие проекты.
- 10 файлов `.lic` успешно разобраны как JSON.
- Проверены 9 непустых встроенных Base64-ресурсов; Base64 и PNG/WAV заголовки корректны.
- В коде редактора найдено 46 явных подписок `.Click +=`; маршруты контекстных меню и команд проверялись отдельно.
- Баланс скобок/квадратных/фигурных скобок проверен для всех 86 C# файлов: ошибок не найдено.
- В C# нет `TODO`, `FIXME` или `NotImplementedException`.
- Единственный активный `NotSupportedException` — ожидаемый отказ импортера для форматов, отличных от PNG/WAV.
- В `PixelFight.Droid` сохранены отдельные Android compatibility stubs, например беззвучный `Sfx`; это legacy MAUI-проект вне четырёх проектов `PixelEngine.sln`, а не функция PixelEngine Core. Аналогичные no-op свойства в `Compat.cs` — заглушки платформенного Graphics shim.

## Проверенные области и внесённые исправления

### Редактор и команды

Подробный перечень команд и исправлений находится в `BUTTON_AUDIT.md`. В том числе проверены File/New/Open/Save/Exit, Undo/Redo, Scene/Hierarchy, Scene View, Game Preview, Run/Stop, Inspector, Asset Browser, Prefab Browser, Script Editor, Output, Diagnostics, Stage Tools, Animation, Lighting, Particles, TileMap, Profiler, Command Palette и контекстные меню.

Исправлено в аудите кнопок:

1. Защита несохранённых изменений при New/Open/Exit; отмена диалога не продолжает действие.
2. Play не стартует после отменённого или неудачного сохранения.
3. Состояния Run/Stop синхронизированы между верхней панелью и Game Preview.
4. Команды, требующие проект, сцену, выделенный объект, ресурс или клип, отключаются, когда контекст недоступен.
5. Правый клик в Scene View и Inspector выбирает объект/компонент под курсором до открытия меню.
6. Prefab-команды учитывают наличие шаблона и экземпляров.
7. WAV preview доступен только для WAV; Stop — только при активном preview.
8. Output Clear/Copy/Export учитывают содержимое/выделение, ошибки файловой системы обрабатываются.
9. Diagnostics явно выводит `No issues found`, когда ошибок нет.

### Связь Inspector → LIC → Runtime

Найденные и исправленные разрывы:

- Компонент `Script` теперь действительно запускает скрипт из `ScriptName`; устаревшее поле `object.script` остаётся совместимым и имеет приоритет при одновременном заполнении.
- Inspector показывает и редактирует рабочие поля `SpriteRenderer`, `Rigidbody2D`, `BoxCollider2D`, `CircleCollider2D`, `AudioSource` и `CameraFollow`.
- `CameraFollow` можно добавить к LIC-объекту; цель разрешается без учёта регистра. Нулевой `SmoothSpeed` означает мгновенное следование к границе dead zone.
- Lua API получил `bind_button`, `unbind_key` и `unbind_button`; проверяются допустимость enum, пустые имена и лимиты InputActionMap.
- Lua `self.x/y/vx/vy/w/h` защищены от NaN/Infinity и недопустимых значений.
- Динамически создаваемым Lua-объектам назначаются уникальные имена.
- `CloneComponentData` копирует все текущие authoring-поля компонента и глубоко копирует extension data, чтобы Duplicate/Prefab не теряли настройки.
- AudioSource корректно приостанавливается/возобновляется при отключении/включении и освобождает instance при удалении; одноразовые звуки проходят через общий mixer.

### Геометрия, рендер и ввод

- Исправлена согласованность LIC polygon renderer и pointer picking: координаты Polygon теперь преобразуются текущими позицией, поворотом и масштабом `Transform`.
- Прямоугольники/круги и Polygon выбираются мышью в локальных координатах с учётом вращения и scale.
- Добавлены конечные проверки для геометрии и скорости, чтобы скриптовые NaN/Infinity не повреждали состояние объектов и не передавались в SpriteBatch.
- SortingLayerRegistry использует фиксированные диапазоны глубины: регистрация нового слоя не меняет ранее рассчитанные значения. Введён лимит 256 слоёв, превышение подсвечивается Diagnostics (`PE1215`), renderer сохраняет работоспособность.
- Camera2D корректно учитывает поворот viewport при ограничении камеры.
- Rigidbody freeze axes применяются после gravity/damping/forces, поэтому сила не может вернуть скорость по замороженной оси.

### Runtime/Core и проектные операции

Проверены исходные реализации и существующие smoke-сценарии для component lifecycle, scene mutation, физики, коллизий/триггеров, рендер-сортировки, камеры, ввода, AudioMixer, сохранения, сериализации, анимации, Sprite Sheets, атласов, освещения, теней, частиц, TileMap, prefab, diagnostics и asset import.

В `PixelEngine.SmokeTests/Program.cs` сейчас 32 именованные группы `PASS`-проверок. Новая группа `LIC transformed picking` проверяет вращённый/масштабированный прямоугольник и перемещённый Polygon. **Наличие теста проверено по коду, но сами тесты в этой среде не запускались.**

## Что не следует считать полностью реализованным

Ниже — осознанные ограничения текущего Stage 10, а не скрытые обещания:

- Физика пока без CCD, суставов, torque/angular velocity и полноценного устойчивого stacking; быстрые тела могут проходить сквозь тонкие коллайдеры.
- Skeletal Animation, bones/IK, blend trees и animation events отсутствуют.
- CPU lighting ограничен 16 источниками и 64 теневыми сегментами на источник; нет normal maps, мягкой penumbra и GPU light accumulation.
- Sprite Atlas builder и Runtime-интеграция есть, но отдельного визуального atlas workspace в редакторе нет.
- Частицы используют ограниченный CPU pool; GPU simulation, collision и particle texture sheets отсутствуют.
- TileMap имеет кисть/fill/flood fill/cave generation и статические коллизии, но rule-based AutoTile, асинхронная подгрузка чанков и animated tile materials отсутствуют.
- C#-скрипты импортируются/сохраняются, но не исполняются. Lua SoftSandbox не является жёсткой границей безопасности и строгий instruction/time budget ещё не реализован.
- Audio поддерживает встроенные WAV; нет streaming для длинной музыки и визуального waveform/mixer workspace редактора.
- Полноценные встроенные Game View, frame-step debugger, breakpoints, hot reload, visual scripting и Android publishing для PixelEngine Core отсутствуют.
- `PixelFight.Droid` — отдельный legacy MAUI-порт; его no-op SFX/compatibility-методы не входят в runtime PixelEngine Stage 10.

## Проверка на Windows

Открой PowerShell в корне распакованного проекта и выполни:

```powershell
dotnet restore .\PixelEngine.sln
dotnet build .\PixelEngine.sln -c Debug
dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Debug -- .
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug
```

Для демонстрации Stage 10:

```powershell
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -c Debug -- .\Lic\stage10-showcase.lic
```

Для повторяемой структурной проверки + restore/build/tests:

```powershell
.\verify-stage10.ps1
```

После успешного build следует вручную проверить New/Open/Save/Cancel, Run/Stop, выбор объектов Polygon после перемещения/поворота, Inspector, импорт/экспорт ресурсов, Prefab операции, TileMap, Animation, Lighting, Particles, Script Editor и Output. Только запуск в Windows сможет подтвердить работу WinForms диалогов, GPU/Audio backend и всех реальных взаимодействий.
