// End-to-end tests of the published app in headless Chromium at iPhone size.
// Run: dotnet publish src/SofiesUnivers.App -c Release -o artifacts/publish && npm test (in tests/e2e)
const { test, before, after, describe } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const { startServer, launch, newPhone, open, tmpFile, readZip, pngBytes, addSession } = require('./helpers.cjs');

let server;
let browser;
let base;

before(async () => {
  server = await startServer(5301);
  base = server.url;
  browser = await launch();
});

after(async () => {
  await browser?.close();
  server?.stop();
});

describe('personality themes', () => {
  test('one tap switches the theme immediately, without confirmation, and it persists', async () => {
    const { context, page } = await newPhone(browser);
    let dialogs = 0;
    page.on('dialog', d => { dialogs++; d.dismiss(); });
    await open(page, base);
    for (const id of ['boheme', 'victoriansk', 'hippie', 'muse', 'diva']) {
      await page.click(`[data-theme-id=${id}]`);
      assert.equal(await page.getAttribute('html', 'data-theme'), id);
      assert.equal(await page.getAttribute(`[data-theme-id=${id}]`, 'aria-pressed'), 'true');
      const bg = await page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--primary').trim());
      assert.ok(bg.length > 0);
    }
    assert.equal(dialogs, 0);
    await page.reload();
    await page.waitForSelector('.shell');
    assert.equal(await page.getAttribute('html', 'data-theme'), 'diva');
    assert.deepEqual(page.errors, []);
    await context.close();
  });
});

describe('sessions and payments', () => {
  test('register a session in a few taps; it survives a restart and creates one clinic trip', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base);
    await page.click('text=Ny session');
    await page.waitForSelector('#price');
    await page.locator('button.chip', { hasText: 'Massage' }).click();
    await page.fill('#price', '800');
    await page.click('[data-testid=save-session]');
    await page.waitForSelector('.toast');
    assert.match(await page.innerText('.toast'), /Session gemt/);
    assert.match(await page.innerText('.toast'), /Kørsel til klinikken/);

    await addSession(page, base, { price: 650 }); // same day: no second trip

    await page.reload();
    await page.waitForSelector('.shell');
    await open(page, base + 'sessioner');
    assert.equal(await page.locator('.list-item').count(), 2);
    await open(page, base + 'koersel');
    assert.equal(await page.locator('.list-item').count(), 1);
    assert.deepEqual(page.errors, []);
    await context.close();
  });

  test('partial payment shows as outstanding and a later payment settles it', async () => {
    const { context, page } = await newPhone(browser);
    await addSession(page, base, { price: 800, paid: 300, nickname: 'Undertøjsfabrikanten' });
    await open(page, base + 'udestaaende');
    assert.match(await page.innerText('.content'), /Mangler 500,00 kr\./);

    await page.click('text=Registrér betaling');
    assert.equal(await page.inputValue('#amount'), '500');
    await page.locator('button.chip', { hasText: 'Kontant' }).click();
    await page.click('text=Gem betaling');
    await page.waitForSelector('text=Alt er betalt');
    await context.close();
  });

  test('up to three elements, with a new type added inline', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base + 'session/ny');
    await page.waitForSelector('#price');
    await page.locator('button.chip', { hasText: 'Massage' }).click();
    await page.locator('button.chip', { hasText: 'Sexologisk samtale' }).click();
    await page.click('text=+ Ny type');
    await page.fill('#elements-label-new', 'Tantra');
    await page.click('text=+ Tilføj “Tantra”');
    await page.waitForSelector('button.chip[aria-pressed=true]:has-text("Tantra")');
    assert.equal(await page.locator('button.chip[aria-pressed=true]').filter({ hasText: /Massage|Sexologisk|Tantra/ }).count(), 3);

    // A fourth element cannot be selected
    await page.click('text=+ Ny type');
    await page.fill('#elements-label-new', 'Yoni');
    await page.click('text=+ Tilføj “Yoni”');
    await page.waitForSelector('button.chip[aria-pressed=false]:has-text("Yoni")');
    assert.ok(await page.locator('button.chip:has-text("Yoni")').isDisabled());
    await context.close();
  });

  test('overpayment is refused with a clear message', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base + 'session/ny');
    await page.waitForSelector('#price');
    await page.locator('button.chip', { hasText: 'Massage' }).click();
    await page.fill('#price', '800');
    await page.fill('#pay-amount-0', '900');
    await page.click('[data-testid=save-session]');
    await page.waitForSelector('.error-summary');
    assert.match(await page.innerText('.error-summary'), /højere end prisen/);
    await context.close();
  });

  test('leaving a form with unsaved changes asks first', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base + 'session/ny');
    await page.waitForSelector('#price');
    await page.fill('#price', '800');
    let asked = false;
    page.once('dialog', d => { asked = true; d.dismiss(); });
    await page.click('.bottomnav >> text=Udgifter');
    await page.waitForTimeout(300);
    assert.ok(asked);
    assert.match(page.url(), /session\/ny/);
    await context.close();
  });
});

describe('expenses and driving', () => {
  test('expense with a photo receipt', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base + 'udgift/ny?retur=udgifter');
    await page.waitForSelector('#amount');
    await page.fill('#amount', '249,95');
    await page.locator('button.chip', { hasText: 'Materialer' }).click();
    await page.fill('#description', 'Massageolie');
    await page.setInputFiles('[data-testid=pick-receipt] input[type=file]', { name: 'kvittering.png', mimeType: 'image/png', buffer: pngBytes() });
    await page.waitForSelector('.receipt-preview img');
    await page.click('[data-testid=save-expense]');
    await page.waitForURL(/udgifter$/);
    assert.match(await page.innerText('.list'), /249,95 kr\./);
    assert.match(await page.innerText('.list'), /Kvittering/);
    await context.close();
  });

  test('unsupported receipt files are refused kindly', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base + 'udgift/ny');
    await page.waitForSelector('#amount');
    await page.setInputFiles('[data-testid=pick-receipt] input[type=file]', { name: 'note.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') });
    await page.waitForSelector('.error-text');
    assert.match(await page.innerText('.error-text'), /Filtypen understøttes ikke/);
    await context.close();
  });

  test('manual trip and the tax report flag', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base + 'koersel/ny?retur=koersel');
    await page.waitForSelector('#km');
    await page.locator('button.chip', { hasText: 'Indkøb' }).click();
    await page.click('text=Anden rute');
    await page.fill('#to', 'Bauhaus Odense');
    await page.fill('#km', '12,5');
    await page.click('[data-testid=save-trip]');
    await page.waitForURL(/koersel$/);
    const flag = page.locator('button.badge', { hasText: 'I skatteoversigt' });
    await flag.click();
    await page.waitForSelector('button.badge:has-text("Ikke i skatteoversigt")');
    await page.reload();
    await page.waitForSelector('button.badge:has-text("Ikke i skatteoversigt")');
    await context.close();
  });
});

describe('exports, backup and restore', () => {
  test('tax report is a real xlsx without nicknames; private workbook includes them', async () => {
    const { context, page } = await newPhone(browser);
    await addSession(page, base, { price: 900, nickname: 'Hemmelig Kunde' });
    await open(page, base + 'overblik');

    const [taxDownload] = await Promise.all([page.waitForEvent('download'), page.click('[data-testid=export-tax]')]);
    const taxPath = tmpFile(taxDownload.suggestedFilename());
    await taxDownload.saveAs(taxPath);
    assert.match(taxDownload.suggestedFilename(), /^Regnskabsoversigt-\d{4}\.xlsx$/);
    const tax = readZip(fs.readFileSync(taxPath));
    assert.ok(tax['xl/workbook.xml']);
    const allTax = Object.values(tax).map(b => b.toString('utf8')).join('\n');
    assert.ok(!allTax.includes('Hemmelig Kunde'));
    assert.ok(allTax.includes('900'));

    const [privDownload] = await Promise.all([page.waitForEvent('download'), page.click('[data-testid=export-private]')]);
    const privPath = tmpFile(privDownload.suggestedFilename());
    await privDownload.saveAs(privPath);
    const priv = readZip(fs.readFileSync(privPath));
    assert.ok(Object.values(priv).map(b => b.toString('utf8')).join('\n').includes('Hemmelig Kunde'));
    await context.close();
  });

  test('complete backup, then full restore into an empty app with the receipt', async () => {
    // 1. Create data and a backup on "the old phone".
    const old = await newPhone(browser);
    await addSession(old.page, base, { price: 777, nickname: 'Kunde 042', paid: 500 });
    await open(old.page, base + 'udgift/ny?retur=udgifter');
    await old.page.waitForSelector('#amount');
    await old.page.fill('#amount', '120');
    await old.page.locator('button.chip', { hasText: 'Udstyr' }).click();
    await old.page.setInputFiles('[data-testid=pick-receipt] input[type=file]', { name: 'kvit.png', mimeType: 'image/png', buffer: pngBytes(60, 40) });
    await old.page.waitForSelector('.receipt-preview img');
    await old.page.click('[data-testid=save-expense]');
    await old.page.waitForURL(/udgifter$/);

    await open(old.page, base + 'backup');
    await old.page.click('[data-testid=make-backup]');
    await old.page.waitForSelector('[data-testid=share-backup]');
    const [download] = await Promise.all([old.page.waitForEvent('download'), old.page.click('[data-testid=share-backup]')]);
    assert.match(download.suggestedFilename(), /^Sofie-Backup-\d{4}-\d{2}-\d{2}\.zip$/);
    const zipPath = tmpFile(download.suggestedFilename());
    await download.saveAs(zipPath);
    await old.page.click('[data-testid=confirm-backup]');
    await old.page.waitForSelector('text=Alle dine registreringer er med');

    const zip = readZip(fs.readFileSync(zipPath));
    assert.ok(zip['Sofies-Regnskab.xlsx']);
    assert.ok(zip['Backup-info.json']);
    assert.equal(Object.keys(zip).filter(n => n.startsWith('Kvitteringer/')).length, 1);
    await old.context.close();

    // 2. Restore on "a new phone" (fresh browser storage).
    const fresh = await newPhone(browser);
    await open(fresh.page, base + 'gendan');
    await fresh.page.setInputFiles('[data-testid=pick-backup] input[type=file]', zipPath);
    await fresh.page.waitForSelector('[data-testid=restore-summary]');
    assert.match(await fresh.page.innerText('[data-testid=restore-summary]'), /Alle kontrolsummer passer/);
    assert.ok(await fresh.page.isDisabled('[data-testid=restore-start]'));
    await fresh.page.check('[data-testid=restore-understood]');
    await fresh.page.click('[data-testid=restore-start]');
    await fresh.page.click('text=Ja, erstat alle data');
    await fresh.page.waitForSelector('.hero');
    assert.match(await fresh.page.innerText('.toast'), /Gendannelsen lykkedes/);

    await fresh.page.reload();
    await open(fresh.page, base + 'sessioner');
    assert.match(await fresh.page.innerText('.list'), /777,00 kr\./);
    assert.match(await fresh.page.innerText('.list'), /Kunde 042/);
    await open(fresh.page, base + 'udestaaende');
    assert.match(await fresh.page.innerText('.content'), /Mangler 277,00 kr\./);
    await open(fresh.page, base + 'udgifter');
    await fresh.page.click('.list-item');
    await fresh.page.waitForSelector('.receipt-preview img');
    assert.deepEqual(fresh.page.errors, []);
    await fresh.context.close();
  });

  test('a damaged backup is refused and current data stays', async () => {
    const { context, page } = await newPhone(browser);
    await addSession(page, base, { price: 555, nickname: 'Bliver her' });
    const bad = tmpFile('Sofie-Backup-2026-01-01.zip');
    fs.writeFileSync(bad, Buffer.from('PK\u0003\u0004 not really a zip'));
    await open(page, base + 'gendan');
    await page.setInputFiles('[data-testid=pick-backup] input[type=file]', bad);
    await page.waitForSelector('[data-testid=restore-errors]');
    assert.match(await page.innerText('[data-testid=restore-errors]'), /ikke ændret/);
    await open(page, base + 'sessioner');
    assert.match(await page.innerText('.list'), /Bliver her/);
    await context.close();
  });

  test('IndexedDB replacement is atomic: a failing restore transaction leaves data intact', async () => {
    const { context, page } = await newPhone(browser);
    await addSession(page, base, { price: 444, nickname: 'Atomar' });
    const result = await page.evaluate(async () => {
      const storage = await import('./js/storage.js');
      const before = JSON.parse(await storage.loadAll());
      const broken = { ...before, sessions: [{ noId: true }] }; // violates the key path → the transaction aborts
      let error = null;
      try { await storage.replaceAll(JSON.stringify(broken), []); } catch (e) { error = e.message; }
      const afterLoad = JSON.parse(await storage.loadAll());
      return { error, before: before.sessions.length, after: afterLoad.sessions.length, nickname: afterLoad.sessions[0]?.clientNickname };
    });
    assert.ok(result.error);
    assert.equal(result.after, result.before);
    assert.equal(result.nickname, 'Atomar');
    await context.close();
  });
});

describe('layout', () => {
  test('main pages fit an iPhone screen without sideways scrolling', async () => {
    const { context, page } = await newPhone(browser);
    await addSession(page, base, { price: 1234567.5, nickname: 'Et meget langt kaldenavn der bliver ved og ved' });
    for (const p of ['', 'sessioner', 'udestaaende', 'udgifter', 'udgift/ny', 'koersel', 'koersel/ny', 'overblik', 'historik', 'indstillinger', 'backup', 'gendan', 'lister', 'rute', 'hjaelp', 'systemtjek', 'session/ny']) {
      await open(page, base + p);
      await page.waitForTimeout(150);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      assert.ok(overflow <= 0, `${p || 'forside'} overflows by ${overflow}px`);
    }
    assert.deepEqual(page.errors, []);
    await context.close();
  });

  test('important controls are large enough to tap', async () => {
    const { context, page } = await newPhone(browser);
    await open(page, base);
    const small = await page.evaluate(() => [...document.querySelectorAll('.btn, .chip, .bottomnav a, .theme-option, .quick')]
      .map(el => el.getBoundingClientRect())
      .filter(r => r.width > 0 && (r.height < 40 || r.width < 40)).length);
    assert.equal(small, 0);
    await context.close();
  });
});
