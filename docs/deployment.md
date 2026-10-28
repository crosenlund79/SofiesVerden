# Deployment and installation

The app is a set of static files. Anything that serves HTTPS can host it. The default is
**GitHub Pages** via `.github/workflows/deploy.yml`.

> Only application code and static assets are published. Sofie's data, receipts and backups exist
> only on her phone and in the backup files she saves. Never commit exports or backups – `.gitignore`
> blocks the usual file names (`*.zip`, `Sofie-Backup-*`, `Sofies-Regnskab*.xlsx`, `Regnskabsoversigt-*.xlsx`).

## GitHub Pages (default)

1. In the repository: **Settings → Pages → Build and deployment → Source: GitHub Actions**.
2. Merge to `main` (or run the "Deploy to GitHub Pages" workflow manually).
3. The workflow runs the tests, publishes the app, runs `tools/prepare-pages.js` and deploys.
4. The app is available at `https://<owner>.github.io/<repository>/`
   (for this repository: `https://crosenlund79.github.io/SofiesVerden/` – GitHub uses the repository
   name as written in the repository settings).

What `tools/prepare-pages.js` does, and why:

| Step | Why |
|---|---|
| `<base href="/<repository>/">` in `index.html` | Blazor resolves routes and `_framework/` relative to the base. |
| Update the SHA-256 of `index.html` in `service-worker-assets.js` | The service worker caches files with integrity checks; a stale hash would make offline installation fail. |
| Copy `index.html` → `404.html` | GitHub Pages has no SPA fallback; deep links such as `/SofiesVerden/sessioner` load the app through 404.html. |
| `.nojekyll` | Otherwise Jekyll drops the `_framework` folder (leading underscore). |

The sub-path setup, deep links, service-worker scope and offline start are verified by
`tests/e2e/pages.test.cjs`.

### Private repository

GitHub Pages for a **private** repository requires a paid GitHub plan, and the published site is
public either way (it only contains code). Alternatives if the repository must stay private on a
free plan: Cloudflare Pages or Netlify (both free for static sites) – publish
`artifacts/publish/wwwroot` after running `node tools/prepare-pages.js artifacts/publish/wwwroot /`
(base path `/` when the app is at the root of its own domain) and configure "serve index.html for
unknown paths".

### Custom domain

With a custom domain the app lives at the root: change the deploy step to
`node tools/prepare-pages.js artifacts/publish/wwwroot /`. **Changing the address later means a new
origin**: the browser treats it as a different site with empty storage. Sofie must then make a backup
on the old address and restore it on the new one.

## Updates

When a new version is deployed, the installed app downloads it in the background and shows
"✨ En ny version af appen er klar. [Opdatér]". Data in IndexedDB is not affected by updates. If a
release contains a data migration, ask Sofie to make a backup first (see architecture.md).

## Installing on the iPhone (Safari)

1. Open the app address in **Safari**.
2. Tap **Del** (square with arrow) → **Føj til hjemmeskærm** → **Tilføj**.
3. Open the app from the home-screen icon from now on.

Notes:

* Safari and the home-screen app have **separate storage**. Data entered in a Safari tab is not
  visible in the home-screen app and vice versa. Install first, then start using it.
* Home-screen web apps are exempt from Safari's 7-day eviction of website data for unused sites,
  which is another reason to use the icon. The app asks for persistent storage when installed.
* Deleting the home-screen icon deletes its data. Backups are essential.

## Local development

```bash
dotnet run --project src/SofiesUnivers.App
```

The development service worker does not cache anything. To test PWA behaviour locally:

```bash
dotnet publish src/SofiesUnivers.App -c Release -o artifacts/publish
```

```bash
node tools/serve.js artifacts/publish/wwwroot 5173
```

Service workers and `navigator.share` require HTTPS except on `localhost`. To try the app on a
phone during development, use a deployed preview or an HTTPS tunnel.
