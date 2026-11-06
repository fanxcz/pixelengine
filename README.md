# PixelEngine 2.0

**PixelEngine** — открытый 2D-движок с Windows Studio, отдельным MonoGame Player и проектами в формате `.lic`. В репозитории также сохранены исходные проекты **Bloody Pixels**: старая игра, Windows-редактор, Player, Android MAUI-порт и регрессионные тесты.

[Сайт](https://fanxcz.github.io/pixelengine/) · [Релизы](https://github.com/fanxcz/pixelengine/releases) · [Исходный код](https://github.com/fanxcz/pixelengine) · [MIT License](LICENSE)

> **Текущая версия:** v2.0.0-preview.6 для Windows x64. Это предварительная сборка. CI подтверждает компиляцию и автоматические проверки, но не заменяет проверку графического запуска на конкретном компьютере.

## Скачать и запустить

1. Откройте [страницу релизов](https://github.com/fanxcz/pixelengine/releases) и скачайте `PixelEngine-v2.0.0-preview.6-win-x64.zip`.
2. Распакуйте архив целиком в отдельную папку. Не удаляйте `Lic/`, `Games/`, `Player/`, `PixelFight*/` или корневой `avatar.png`. Последние файлы относятся к исходной версии Bloody Pixels.
3. Запустите `PixelEngine.Editor.exe`. По умолчанию откроется `Lic/demo.lic`.
4. Нажмите **F5**, чтобы запустить Player в отдельном окне; **Shift+F5** останавливает его.

Готовый архив включает self-contained .NET 8 сборки: устанавливать .NET SDK для запуска пакета не требуется. `Player/` содержит исполняемый файл и нативные библиотеки MonoGame/SDL/OpenAL. Не перемещайте `PixelEngine.Sandbox.exe` отдельно от этой папки. Требуются Windows x64 и совместимый графический драйвер с поддержкой OpenGL.

## Примеры и игры

В релизный архив входят все `.lic`-проекты из `Lic/` и полный каталог `Games/` с исходниками и ресурсами Ashfall.

| Проект | Назначение |
|---|---|
| `Lic/demo.lic` | Стартовая сцена, открывается по умолчанию |
| `Lic/knights.lic` | Игровой пример Knights |
| `Lic/merchant.lic` | Игровой пример Merchant |
| `Lic/strategy.lic` | Стратегический пример |
| `Lic/tower.lic` | Tower Defense |
| `Lic/stage8-showcase.lic` | Showcase возможностей Stage 8 |
| `Lic/stage10-showcase.lic` | Showcase возможностей Stage 10 |
| `Lic/world.lic` | Большой тестовый игровой проект |
| `Games/Ashfall/Ashfall.lic` | ASHFALL: The Last Beacon — top-down arena survival |

У Ashfall также сохранены `Game/game.lua`, каталог `Assets/`, `Preview.png`, README и запись о валидации. Откройте `Games/Ashfall/Ashfall.lic` через **File → Open** в Studio. Управление и описание игры находятся в [`Games/Ashfall/README.md`](Games/Ashfall/README.md).

### Bloody Pixels — оригинальная игра

Старые проекты не удалены и поставляются в архиве в исходной структуре: `PixelFight/`, `PixelFight.Editor/`, `PixelFight.Player/`, `PixelFight.Droid/`, `PixelFight.Tests/` и `avatar.png`. Windows-проекты требуют .NET 8 SDK; Android-порт требует MAUI workload. Полные инструкции находятся в [`BLOODY-PIXELS-LEGACY.md`](BLOODY-PIXELS-LEGACY.md).

Важно: это сохранённая оригинальная версия игры, а не полностью перенесённая в PixelEngine 2.0 игра. Новый MonoGame runtime пока не имеет полного паритета по игровой логике, физике и визуализации Bloody Pixels.

## Возможности Studio

- Иерархия сцены, 2D Scene View, сетка, snapping, zoom/pan и выбор объектов.
- Inspector и редактор компонентов, Prefabs, FileSystem и Asset Browser.
- Редактор Lua/C# с вкладками, подсветкой, поиском, переходом к строке, bookmarks и подсказками API.
- Undo/Redo, Command Palette (`Ctrl+Shift+P`), Quick Open (`Ctrl+P`) и сохраняемые настройки.
- Output, Problems и встроенный PowerShell/CMD терминал.
- Диагностика проекта и отображение ошибок сборки.

## Runtime

- MonoGame DesktopGL и отдельное окно Player.
- Сцены, объекты и компоненты, 2D-камера, физика, коллизии и триггеры.
- TileMap, анимации, sprite sheets, частицы и 2D-освещение.
- WAV-аудио, AudioSource и базовый микшер.
- Lua выполняется через MoonSharp SoftSandbox.

**Ограничения текущей версии:** runtime ориентирован на 2D. C#-скрипты можно хранить и редактировать в Studio, но Player пока их не исполняет. Полноценные 3D, hot reload C# и интерактивный debugger пока не заявляются.

## Сборка из исходников

**Требования:** Windows, .NET 8 SDK, доступ к NuGet. Для запуска Player нужна графическая среда с совместимым OpenGL.

```powershell
git clone https://github.com/fanxcz/pixelengine.git
cd pixelengine
dotnet restore .\PixelEngine.sln
dotnet build .\PixelEngine.sln -c Release

# Runtime smoke/regression checks
dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release -- .

# Editor headless tests
dotnet run --project .\PixelEngine.Editor.Tests\PixelEngine.Editor.Tests.csproj -c Release

# Studio
dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug

# Ashfall Player
dotnet run --project .\PixelEngine.Sandbox\PixelEngine.Sandbox.csproj -c Debug -- ".\Games\Ashfall\Ashfall.lic"

# Original Bloody Pixels game and legacy tests (Windows)
dotnet run --project .\PixelFight\PixelFight.csproj -c Release
dotnet run --project .\PixelFight.Tests\PixelFight.Tests.csproj -c Release
```

Или откройте `PixelEngine.sln` в Visual Studio 2022 с поддержкой .NET 8.

## Структура репозитория

| Каталог | Назначение |
|---|---|
| `PixelEngine.Core/` | Runtime, сцены, рендеринг, физика, аудио, сериализация и Lua |
| `PixelEngine.Editor/` | Windows Studio |
| `PixelEngine.Sandbox/` | Отдельный MonoGame Player |
| `PixelEngine.SmokeTests/` | Runtime smoke/regression checks |
| `PixelEngine.Editor.Tests/` | Headless проверки логики редактора |
| `Lic/` | LIC-примеры и тестовые проекты |
| `Games/` | Игры с исходниками и ресурсами, включая Ashfall |
| `PixelFight/`, `PixelFight.Editor/`, `PixelFight.Player/` | Оригинальная Bloody Pixels игра, редактор и Player |
| `PixelFight.Droid/`, `PixelFight.Tests/` | Android MAUI-порт и регрессионные тесты legacy-версии |
| `avatar.png` | Общий арт исходных проектов Bloody Pixels |
| `docs/` | Статический сайт |
| `.github/workflows/` | CI, релизная сборка и публикация Pages |

## Разработка и лицензия

PixelEngine распространяется по лицензии **MIT**. Вы можете создавать форки, изменять исходники, выпускать собственные версии и использовать проект в коммерческих продуктах. При распространении необходимо сохранять уведомление об авторских правах и текст лицензии.

Полный текст: [`LICENSE`](LICENSE).

Чтобы предложить изменение, создайте Fork, внесите правки в отдельную ветку и откройте Pull Request. Убедитесь, что разрешено распространять добавленные изображения, звуки, шрифты и другие сторонние материалы: сторонние зависимости могут иметь отдельные лицензии.

## Сообщить об ошибке

Создайте [Issue](https://github.com/fanxcz/pixelengine/issues) с версией PixelEngine, версией Windows, шагами воспроизведения и релевантным фрагментом лога. Логи редактора и Player находятся в `%LOCALAPPDATA%\PixelEngine\Logs\`. Не прикладывайте приватные проекты или секретные данные без необходимости.
