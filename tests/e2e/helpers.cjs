// Shared helpers for the browser tests.
const { chromium } = require('playwright');
const { spawn } = require('child_process');
const path = require('path');
const fs = require('fs');
const os = require('os');
const zlib = require('zlib');

const repo = path.resolve(__dirname, '..', '..');
const publishDir = process.env.PUBLISH_DIR || path.join(repo, 'artifacts', 'publish', 'wwwroot');

async function startServer(port, basePath = '/', dir = publishDir) {
  if (!fs.existsSync(path.join(dir, 'index.html'))) {
    throw new Error(`Published app not found in ${dir}. Run: dotnet publish src/SofiesUnivers.App -c Release -o artifacts/publish`);
  }
  const proc = spawn(process.execPath, [path.join(repo, 'tools', 'serve.js'), dir, String(port), basePath], { stdio: 'pipe' });
  await new Promise((resolve, reject) => {
    proc.stdout.on('data', d => { if (String(d).includes('Serving')) resolve(); });
    proc.on('exit', code => reject(new Error('server exited ' + code)));
  });
  return { url: `http://localhost:${port}${basePath}`, stop: () => proc.kill() };
}

async function launch() {
  return chromium.launch(process.env.CHROMIUM_PATH ? { executablePath: process.env.CHROMIUM_PATH } : {});
}

async function newPhone(browser) {
  const context = await browser.newContext({
    viewport: { width: 390, height: 844 },
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true,
    locale: 'da-DK',
    timezoneId: 'Europe/Copenhagen',
    acceptDownloads: true,
  });
  const page = await context.newPage();
  page.errors = [];
  page.on('pageerror', e => page.errors.push(e.message));
  return { context, page };
}

async function open(page, url) {
  await page.goto(url);
  await page.waitForSelector('.shell', { timeout: 60000 });
}

function tmpFile(name) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sofie-e2e-'));
  return path.join(dir, name);
}

// Minimal ZIP reader (enough for .xlsx and our backups) so the tests need no extra packages.
function readZip(buffer) {
  const entries = {};
  let eocd = buffer.length - 22;
  while (eocd >= 0 && buffer.readUInt32LE(eocd) !== 0x06054b50) eocd--;
  if (eocd < 0) throw new Error('Not a zip file');
  const count = buffer.readUInt16LE(eocd + 10);
  let offset = buffer.readUInt32LE(eocd + 16);
  for (let i = 0; i < count; i++) {
    const method = buffer.readUInt16LE(offset + 10);
    const compressedSize = buffer.readUInt32LE(offset + 20);
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    const localOffset = buffer.readUInt32LE(offset + 42);
    const name = buffer.toString('utf8', offset + 46, offset + 46 + nameLength);
    const localNameLength = buffer.readUInt16LE(localOffset + 26);
    const localExtraLength = buffer.readUInt16LE(localOffset + 28);
    const start = localOffset + 30 + localNameLength + localExtraLength;
    const data = buffer.subarray(start, start + compressedSize);
    entries[name] = method === 0 ? Buffer.from(data) : zlib.inflateRawSync(data);
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

// A small PNG generated on the fly (solid color), used as a receipt photo.
function pngBytes(width = 40, height = 30) {
  const crcTable = [];
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crcTable[n] = c >>> 0;
  }
  const crc = buf => { let c = 0xffffffff; for (const b of buf) c = crcTable[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const td = Buffer.concat([Buffer.from(type), data]);
    const c = Buffer.alloc(4); c.writeUInt32BE(crc(td));
    return Buffer.concat([len, td, c]);
  };
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 3 + 1)] = 0;
    for (let x = 0; x < width; x++) {
      const i = y * (width * 3 + 1) + 1 + x * 3;
      raw[i] = 200; raw[i + 1] = (x * 6) & 255; raw[i + 2] = (y * 8) & 255;
    }
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0)),
  ]);
}

async function addSession(page, base, { price, nickname, paid, element = 'Massage' }) {
  await open(page, base + 'session/ny?retur=sessioner');
  await page.waitForSelector('#price');
  const chip = page.locator('button.chip', { hasText: element }).first();
  if ((await chip.getAttribute('aria-pressed')) !== 'true') await chip.click();
  await page.fill('#price', String(price));
  if (paid !== undefined) await page.fill('#pay-amount-0', String(paid));
  if (nickname) await page.fill('#nickname', nickname);
  await page.click('[data-testid=save-session]');
  await page.waitForURL(/sessioner$/);
}

module.exports = { startServer, launch, newPhone, open, tmpFile, readZip, pngBytes, addSession, repo, publishDir };
