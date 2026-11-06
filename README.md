# PixelEngine 2.0

**PixelEngine** — 2D game engine with a Windows Studio editor, a separate MonoGame player and editable LIC projects. The repository includes the engine, authoring environment, sample projects and automated checks.

[Сайт проекта](https://fanxcz.github.io/pixelengine/) · [Скачать релиз](https://github.com/fanxcz/pixelengine/releases/tag/v2.0.0-preview.4) · [Исходный код](https://github.com/fanxcz/pixelengine)

> **Текущая версия:** v2.0.0-preview.4 — предварительная сборка для Windows x64. Это preview, а не заявление о завершённом 3D-движке или полном паритете со старой игрой.

## Скачать и запустить

1. Откройте [страницу релизов](https://github.com/fanxcz/pixelengine/releases/tag/v2.0.0-preview.4) и скачайте архив PixelEngine-v2.0.0-preview.4-win-x64.zip.
2. Распакуйте архив целиком в отдельную папку. Не удаляйте каталог Lic: в нём лежат демонстрационные проекты.
3. Запустите PixelEngine.Editor.exe. При первом старте редактор автоматически откроет Lic/demo.lic.
4. Нажмите **F5**, чтобы запустить игру в отдельном окне. **Shift+F5** останавливает Player. Полная версия статического сайта проекта также включена в архив в каталоге `Website/`.

Релизный архив содержит self-contained сборки .NET 8: устанавливать .NET SDK на компьютере, где запускается готовый пакет, не требуется. Полная папка `Player/` содержит исполняемый файл MonoGame и необходимые ему библиотеки SDL/OpenAL — не перемещайте `PixelEngine.Sandbox.exe` отдельно от этой папки. Для Player нужны Windows x64 и совместимый графический драйвер/OpenGL.

## Что входит в PixelEngine 2.0

### Studio — редактор проектов

- Тёмный интерфейс с док-панелями Scene, Scripts, Prefabs, Search, FileSystem, Inspector, Node, Signals, Output, Problems и Terminal.
- 2D Scene View: выбор и перемещение объектов, сетка, snapping, zoom/pan, коллайдеры и подписи.
- Редактор скриптов с вкладками, подсветкой Lua/C#, автопарными скобками, поиском, переходом к строке, bookmarks и API-подсказками.
- Prefabs и компоненты: создание, дублирование, применение и возврат шаблонов; редактирование SpriteRenderer, Rigidbody2D, Box/CircleCollider2D, AudioSource, Animator и Script metadata.
- Импорт PNG/WAV, предпросмотр ресурсов и проверка ссылок через Diagnostics.
- Undo/Redo, Command Palette (Ctrl+Shift+P), Quick Open (Ctrl+P) и сохраняемые настройки редактора.
- Встроенный терминал PowerShell/CMD с историей, выводом stdout/stderr и остановкой процесса.

### Runtime — запуск 2D-проектов

- MonoGame DesktopGL, отдельное окно Player и загрузка сцен из LIC.
- 2D-компоненты, физика, столкновения и триггеры, камера, TileMap, анимация трансформаций и sprite sheets.
- WAV-аудио, AudioSource и базовый микшер.
- Lua-скрипты выполняются через MoonSharp с ограниченной конфигурацией SoftSandbox.

## Пример проекта и команды

В архиве есть демонстрационный проект Lic/demo.lic. Для разработки из исходников на Windows:

**Требования:** .NET 8 SDK, доступ к NuGet и графическая среда для запуска MonoGame Player.

    git clone https://github.com/fanxcz/pixelengine.git
    cd pixelengine
    dotnet restore .\PixelEngine.sln
    dotnet build .\PixelEngine.sln -c Release

    # Smoke/regression checks
    dotnet run --project .\PixelEngine.SmokeTests\PixelEngine.SmokeTests.csproj -c Release -- .
    dotnet run --project .\PixelEngine.Editor.Tests\PixelEngine.Editor.Tests.csproj -c Release

    # Launch Studio
    dotnet run --project .\PixelEngine.Editor\PixelEngine.Editor.csproj -c Debug

Или откройте решение PixelEngine.sln в Visual Studio 2022 с поддержкой .NET 8. Для запуска демонстрационной игры из исходников используйте Run-Demo-Game.cmd.

## Формат проектов и скрипты

Проект хранится в JSON-формате LIC: сцены, объекты, компоненты, скрипты и встроенные ресурсы. Lua-код запускается в Player через MoonSharp. C#-скрипты можно хранить и редактировать в Studio, но в текущем runtime они **не исполняются**. Это ограничение текущей версии, а не поддержка полноценного C# hot reload.

## Ограничения preview

- Studio и готовый релизный комплект предназначены для **Windows x64**. Studio построена на WinForms.
- Текущий runtime — **2D**. Режим 3D в интерфейсе пока отключён.
- C#-скрипты остаются исходным текстом и не исполняются; отладчик, LSP и hot reload не заявляются.
- Встроенный терминал поддерживает обычную интерактивную работу, но не является полной реализацией VT100/ANSI и не предназначен для полноэкранных консольных программ.
- Автоматические проверки собирают проект и тестируют невизуальную логику. Они не заменяют ручную проверку окна редактора, драйверов и графического рендеринга на целевой машине.

Ошибки запуска записываются в %LOCALAPPDATA%\PixelEngine\Logs\editor-startup.log и player-startup.log.

## Структура репозитория

| Каталог | Назначение |
|---|---|
| PixelEngine.Core/ | Движок, сцены, рендеринг, физика, аудио, сериализация и Lua |
| PixelEngine.Editor/ | WinForms Studio |
| PixelEngine.Sandbox/ | MonoGame Player для LIC |
| PixelEngine.SmokeTests/ | Smoke/regression checks runtime |
| PixelEngine.Editor.Tests/ | Headless проверки логики редактора |
| Lic/ | Примеры и тестовые проекты |
| docs/ | Сайт проекта |
| .github/workflows/ | CI, сборка релизного архива и GitHub Pages |

## Отчёты об ошибках

Создайте [Issue в GitHub](https://github.com/fanxcz/pixelengine/issues) и приложите шаги воспроизведения, версию Windows, имя проекта и релевантный фрагмент лога. Не прикладывайте приватные проекты или секретные данные без необходимости.

## Вклад, форки и лицензия

PixelEngine распространяется по лицензии **MIT**. Вы можете свободно форкать репозиторий, изучать и изменять исходный код, создавать собственные версии, публиковать и распространять изменённые сборки, а также использовать проект в коммерческих продуктах. При распространении необходимо сохранить уведомление об авторских правах и текст лицензии.

Полный текст: [LICENSE](LICENSE).

Чтобы предложить улучшения: создайте Fork, внесите изменения в отдельную ветку и откройте Pull Request. Перед публикацией проверьте, что добавленные вами изображения, аудио, шрифты и другие сторонние материалы разрешено распространять. Внешние зависимости сохраняют собственные лицензии и условия.
