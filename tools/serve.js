// Minimal static file server for the published app (used by browser tests and local checks).
// Serves files from a folder under an optional base path and falls back to index.html for
// deep links, like GitHub Pages' 404.html trick.
// Usage: node tools/serve.js <folder> [port] [basePath]
const http = require('http');
const fs = require('fs');
const path = require('path');

const root = path.resolve(process.argv[2] || 'artifacts/publish/wwwroot');
const port = Number(process.argv[3] || 5173);
const base = (process.argv[4] || '/').replace(/\/?$/, '/');

const types = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
  '.json': 'application/json', '.wasm': 'application/wasm', '.png': 'image/png', '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon', '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream',
  '.dll': 'application/octet-stream', '.pdb': 'application/octet-stream', '.txt': 'text/plain', '.woff2': 'font/woff2',
};

http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  if (!url.startsWith(base)) { res.writeHead(404); res.end('Not found'); return; }
  let file = path.join(root, url.slice(base.length));
  if (!file.startsWith(root)) { res.writeHead(403); res.end(); return; }
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    const index = path.join(file, 'index.html');
    file = fs.existsSync(index) ? index : path.join(root, 'index.html');
  }
  res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
  fs.createReadStream(file).pipe(res);
}).listen(port, () => console.log(`Serving ${root} at http://localhost:${port}${base}`));
