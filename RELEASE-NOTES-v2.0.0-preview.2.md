# PixelEngine 2.0.0-preview.2

Исправленный Windows x64 preview-релиз PixelEngine 2.0.

## Изменения относительно preview.1

- Исправлены все кнопки скачивания в README и на сайте: теперь они ведут прямо на страницу prerelease, а не на GitHub `releases/latest`, который предназначен для обычного latest-релиза.
- В пакет включён полный статический сайт `Website/`: HTML, CSS, favicon, sitemap и robots.txt.
- В архив включены оба self-contained приложения, примеры LIC, README и release notes.
- Сохранены исправления автономного запуска Studio и bundled Player из `preview.1`.

## Состав архива

- `PixelEngine.Editor.exe` — Windows Studio.
- `PixelEngine.Sandbox.exe` — отдельный MonoGame Player.
- `Lic/` — демонстрационные и тестовые проекты.
- `Website/` — статический сайт PixelEngine.
- `README.md`, `README.txt`, `RELEASE-NOTES.md`.
- Файл `.sha256` опубликован рядом с ZIP.

## Проверки

Release workflow выполняет restore, Release build, runtime smoke tests и editor headless tests; затем публикует self-contained Windows x64 Studio и Player, проверяет наличие демо-проекта и файлов сайта, собирает ZIP и SHA-256.

## Известные ограничения

- Preview для Windows x64; Studio построена на WinForms.
- Runtime остаётся 2D; 3D workspace выключен.
- Lua выполняется через MoonSharp SoftSandbox. C#-скрипты хранятся и редактируются, но текущий Player их не исполняет.
- Терминал не является полной реализацией VT100/ANSI.
- CI не заменяет ручной запуск GUI и проверку графики на реальной Windows-машине.
- GitHub Pages нужно один раз включить в Settings → Pages → Build and deployment → Source → GitHub Actions.
- В репозитории пока не добавлен LICENSE.

Это предварительный релиз для проверки и обратной связи.
