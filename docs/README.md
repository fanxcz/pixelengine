# PixelEngine website

This folder contains the static project website. It has no build-time dependencies.

## Preview locally

Open `index.html` in a browser. The page uses the accompanying `style.css` and `favicon.svg`.

## Deploy with GitHub Pages

GitHub Pages is configured for this repository with **GitHub Actions** as the deployment source. Every change under `docs/` on `main` triggers the deployment workflow.

The workflow validates the website files and local asset references, uploads this directory, and deploys it to GitHub Pages. Check the [deployment workflow](https://github.com/fanxcz/pixelengine/actions/workflows/pages.yml) for the current status.

Published project URL: https://fanxcz.github.io/pixelengine/

The Windows release package also includes this directory as `Website/`. The repository source is under the MIT License: [LICENSE](../LICENSE).
