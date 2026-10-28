// Prepares the published app for static hosting under a sub path, e.g. GitHub Pages
// (https://<user>.github.io/<repository>/).
//
//   node tools/prepare-pages.js <wwwroot folder> </base/path/>
//
// 1. Sets <base href> in index.html to the sub path, so routing and asset URLs work.
// 2. Updates the service worker's integrity hash for index.html (otherwise offline install fails).
// 3. Copies index.html to 404.html, so deep links such as /<repository>/sessioner open the app.
// 4. Adds .nojekyll, so GitHub Pages serves the _framework folder.
// 5. Removes stale precompressed copies of index.html.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const root = path.resolve(process.argv[2] || 'artifacts/publish/wwwroot');
let base = process.argv[3] || '/';
if (!base.startsWith('/')) base = '/' + base;
if (!base.endsWith('/')) base += '/';

const indexPath = path.join(root, 'index.html');
let html = fs.readFileSync(indexPath, 'utf8');
if (!/<base href="[^"]*"\s*\/?>/.test(html)) throw new Error('No <base href> found in index.html');
html = html.replace(/<base href="[^"]*"\s*\/?>/, `<base href="${base}" />`);
fs.writeFileSync(indexPath, html);

const hash = 'sha256-' + crypto.createHash('sha256').update(fs.readFileSync(indexPath)).digest('base64');
const manifestPath = path.join(root, 'service-worker-assets.js');
let manifest = fs.readFileSync(manifestPath, 'utf8');
const pattern = /("hash":\s*")[^"]+(",\s*"url":\s*"index\.html")/;
if (!pattern.test(manifest)) throw new Error('index.html not found in service-worker-assets.js');
manifest = manifest.replace(pattern, `$1${hash}$2`);
// A new version string makes installed apps pick up the change.
manifest = manifest.replace(/"version":\s*"([^"]+)"/, (_, v) => `"version": "${v}-${crypto.createHash('sha256').update(base).digest('hex').slice(0, 6)}"`);
fs.writeFileSync(manifestPath, manifest);

fs.copyFileSync(indexPath, path.join(root, '404.html'));
fs.writeFileSync(path.join(root, '.nojekyll'), '');
for (const stale of ['index.html.br', 'index.html.gz', 'service-worker-assets.js.br', 'service-worker-assets.js.gz']) {
  fs.rmSync(path.join(root, stale), { force: true });
}

console.log(`Prepared ${root} for base path ${base} (index.html ${hash})`);
