# Sofies Univers

A small, colourful, mobile-first administration app for a Danish self-employed massage and
session therapist. Sofie uses it on her iPhone to register sessions, payments, expenses (with
receipts) and driving, to see what is outstanding, to produce annual Excel reports for her
accountant, and to back up and restore everything as one ZIP file.

The interface is Danish and comes in five "personality universes" that Sofie switches between
with one tap.

* **No server, no account, no tracking.** The app is a Blazor WebAssembly Progressive Web App.
  All data lives in the browser's IndexedDB on Sofie's phone.
* **Data safety comes from backups.** A weekly ZIP backup (Excel workbook + receipts + data file +
  checksums) can be saved to Files/iCloud Drive or mailed, and restored completely.
* **Excel is first class.** A complete private workbook and an annual report without client
  nicknames, both real `.xlsx` files that open in Microsoft Excel.

## Quick start

Prerequisites: [.NET SDK 10](https://dotnet.microsoft.com/download) (LTS). Node 22 only for the browser tests.

```bash
dotnet test SofiesUnivers.slnx
```

```bash
dotnet run --project src/SofiesUnivers.App
```

Then open http://localhost:5080. (The development server has no offline caching; use the published
build below to test the PWA behaviour.)

### Published build and browser tests

```bash
dotnet publish src/SofiesUnivers.App -c Release -o artifacts/publish
```

```bash
node tools/serve.js artifacts/publish/wwwroot 5173
```

```bash
cd tests/e2e && npm ci && npx playwright install chromium && npm test
```

## Project structure

| Path | What |
|---|---|
| `src/SofiesUnivers.Core` | Domain model, validation and business rules (`Domain/Ledger.cs`), Danish formatting, reports, Excel writer/reader, backup ZIP, storage boundary and migrations. No UI, no browser APIs – fully unit tested. |
| `src/SofiesUnivers.App` | Blazor WebAssembly PWA: pages, components, five themes (`wwwroot/css/app.css`) and three small JS modules (`wwwroot/js`: IndexedDB, files/sharing, theme/install). |
| `tests/SofiesUnivers.Tests` | xUnit tests for rules, payments, driving, expenses, reports, Excel (validated with the Open XML SDK), backup/restore and storage. |
| `tests/e2e` | Playwright tests of the published app in Chromium at iPhone size, including sub-path hosting and offline start. |
| `tools` | Static test server, GitHub Pages preparation script, icon renderer. |
| `docs` | Architecture, data model, backup format, deployment, user guide, iPhone checklist, decision log. |

## Documentation

* [Architecture](docs/architecture.md) – structure, data flow, IndexedDB schema, migrations
* [Data model and business rules](docs/data-model.md)
* [Excel workbooks and backup format](docs/excel-and-backup.md)
* [Deployment (GitHub Pages) and PWA installation](docs/deployment.md)
* [Maintenance: route distance, lists, extending safely](docs/maintenance.md)
* [Brugervejledning (Danish user guide)](docs/brugervejledning.md)
* [iPhone test checklist](docs/iphone-test-checklist.md)
* [Decisions and known limitations](docs/decisions.md)
* [Phase 0 technical findings](docs/technical-findings.md)
* [Changelog](CHANGELOG.md)

## Important limitations

* Data is stored only in the browser profile on one device. Deleting the home-screen app, clearing
  Safari website data or losing the phone loses the data – **the ZIP backup is the safety net**.
* Data in IndexedDB is not encrypted by the app. Anyone who can use the unlocked phone can open it.
* The reports are working material for bookkeeping and the tax return. They are not approved by
  Danish authorities, and the app does not calculate VAT, payroll tax or deductions.

See [docs/decisions.md](docs/decisions.md) for the complete list and open questions.
