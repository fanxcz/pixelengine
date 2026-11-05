# PixelEngine website

This folder contains the static project website. It has no build-time dependencies.

## Preview locally

Open `index.html` in a browser. The page uses the accompanying `style.css` and `favicon.svg`.

## Deploy with GitHub Pages

The repository workflow `.github/workflows/pages.yml` publishes this folder when it changes on `main`.

One-time repository setup:
1. Open **Settings → Pages**.
2. Under **Build and deployment**, set **Source** to **GitHub Actions**.
3. Re-run the “Deploy PixelEngine website” workflow if it has already failed because Pages was disabled.

Published project URL: https://fanxcz.github.io/pixelengine/

The release workflow also copies this directory into the Windows package as `Website/`.
