# PixelEngine website

This folder contains the static project website. It has no build-time dependencies.

## Preview locally

Open `index.html` in a browser. The page uses the accompanying `style.css` and `favicon.svg`.

## Deploy with GitHub Pages

The repository workflow `.github/workflows/pages.yml` publishes this folder when it changes on `main`.

One-time repository setup (required before the public URL stops returning 404):
1. Open [Repository Settings → Pages](https://github.com/fanxcz/pixelengine/settings/pages).
2. Under **Build and deployment**, set **Source** to **GitHub Actions**.
3. Save the setting, then run **Actions → Deploy PixelEngine website → Run workflow** (or push a later change under `docs/`).

The workflow validates local site assets and performs a real deployment; it no longer reports success while silently skipping deployment.

Published project URL: https://fanxcz.github.io/pixelengine/

The Windows package also includes this directory as `Website/`. The repository source is under the MIT License: [LICENSE](../LICENSE).
