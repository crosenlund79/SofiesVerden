// Verifies hosting under a sub path (GitHub Pages: https://<user>.github.io/<repository>/):
// deep links, static assets, service worker scope and offline start.
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');
const { startServer, launch, newPhone, repo, publishDir } = require('./helpers.cjs');

const basePath = '/SofiesVerden/';
let server;
let browser;

before(async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sofie-pages-'));
  fs.cpSync(publishDir, dir, { recursive: true });
  execFileSync(process.execPath, [path.join(repo, 'tools', 'prepare-pages.js'), dir, basePath]);
  server = await startServer(5302, basePath, dir);
  browser = await launch();
});

after(async () => {
  await browser?.close();
  server?.stop();
});

test('deep links and assets work under the repository sub path', async () => {
  const { context, page } = await newPhone(browser);
  const failed = [];
  page.on('response', r => { if (r.status() >= 400) failed.push(`${r.status()} ${r.url()}`); });
  await page.goto(server.url + 'koersel');
  await page.waitForSelector('h1:has-text("Kørsel")', { timeout: 60000 });
  await page.click('.bottomnav >> text=Sessioner');
  await page.waitForURL(server.url + 'sessioner');
  const manifest = await page.evaluate(async () => (await fetch('manifest.webmanifest')).json());
  assert.equal(manifest.start_url, './');
  const icon = await page.evaluate(async () => (await fetch('icons/apple-touch-icon.png')).status);
  assert.equal(icon, 200);
  assert.deepEqual(failed, []);
  assert.deepEqual(page.errors, []);
  await context.close();
});

test('a differently cased address is normalised to the base path', async () => {
  const { context, page } = await newPhone(browser);
  await page.goto(server.url.toLowerCase() + 'koersel');
  await page.waitForSelector('h1:has-text("Kørsel")', { timeout: 60000 });
  assert.equal(page.url(), server.url + 'koersel');
  await page.goto(server.url.toLowerCase());
  await page.waitForSelector('.hero', { timeout: 60000 });
  assert.equal(page.url(), server.url);
  assert.deepEqual(page.errors, []);
  await context.close();
});

test('service worker is scoped to the sub path and the app starts offline', async () => {
  const { context, page } = await newPhone(browser);
  await page.goto(server.url);
  await page.waitForSelector('.hero', { timeout: 60000 });
  const scope = await page.evaluate(async () => {
    const reg = await navigator.serviceWorker.ready;
    return reg.scope;
  });
  assert.equal(scope, server.url);
  // Give the worker time to cache every asset, then go offline.
  await page.waitForFunction(async () => {
    const keys = await caches.keys();
    if (keys.length === 0) return false;
    const cache = await caches.open(keys[0]);
    return (await cache.keys()).length > 20;
  }, null, { timeout: 60000 });
  await page.reload();
  await page.waitForSelector('.hero');
  await context.setOffline(true);
  await page.goto(server.url + 'udgifter');
  await page.waitForSelector('h1:has-text("Udgifter")', { timeout: 60000 });
  await context.setOffline(false);
  await context.close();
});
