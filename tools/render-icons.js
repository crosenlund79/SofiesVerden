// Renders wwwroot/icons/icon.svg to the PNG sizes needed for the PWA and iOS.
// Usage: node tools/render-icons.js   (uses the preinstalled Playwright Chromium)
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const dir = path.join(__dirname, '..', 'src', 'SofiesUnivers.App', 'wwwroot', 'icons');
const svg = fs.readFileSync(path.join(dir, 'icon.svg'), 'utf8');
const targets = [
  ['favicon-32.png', 32, 0],
  ['apple-touch-icon.png', 180, 0],
  ['icon-192.png', 192, 0],
  ['icon-512.png', 512, 0],
  ['icon-maskable-512.png', 512, 0.12],
];

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  for (const [name, size, pad] of targets) {
    await page.setViewportSize({ width: size, height: size });
    const inner = Math.round(size * (1 - 2 * pad));
    await page.setContent(`<html><body style="margin:0;background:#b5452f;display:grid;place-items:center;width:${size}px;height:${size}px">
      <div style="width:${inner}px;height:${inner}px">${svg.replace('<svg ', `<svg width="${inner}" height="${inner}" `)}</div></body></html>`);
    await page.screenshot({ path: path.join(dir, name), omitBackground: false });
    console.log('wrote', name);
  }
  await browser.close();
})();
