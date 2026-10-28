# Architecture

## Overview

```
┌──────────────────────────── iPhone (Safari / home-screen PWA) ────────────────────────────┐
│                                                                                            │
│  Blazor WebAssembly UI (SofiesUnivers.App)                                                 │
│    Pages ─ Components ─ ThemeService ─ ToastService ─ Prefs                                │
│          │                                                                                 │
│          ▼                                                                                 │
│  Ledger (SofiesUnivers.Core) ── validation, business rules, in-memory dataset              │
│          │  ChangeSet (puts/deletes)                                                       │
│          ▼                                                                                 │
│  IDataStore ── IndexedDbStore (C#) ── storage.js ── IndexedDB "sofies-univers"             │
│                                                                                            │
│  Reports / PrivateWorkbook / TaxReportWorkbook / BackupWriter / BackupReader (Core, C#)    │
│          │ bytes                                                                           │
│          ▼                                                                                 │
│  BrowserFiles (C#) ── files.js ── navigator.share / download / object URLs / canvas        │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

* **Two projects**: `Core` (plain .NET library, no browser APIs) and `App` (Blazor WebAssembly).
  Everything that can be tested without a browser lives in Core.
* **No backend.** Static files only. Hosting cannot see any of Sofie's data.
* **Single source of truth**: `Ledger`. Sofie's dataset is small (hundreds of records per year), so
  the whole dataset is loaded into memory at start-up. Every change is validated, turned into a
  `ChangeSet`, written to IndexedDB in **one transaction**, and only then applied in memory. A
  failed write leaves both storage and memory unchanged, and the UI shows a friendly error.
* Services are registered as singletons (in WebAssembly that means "for the lifetime of the app").
  There are no repositories, mediators or extra layers.

## Key classes

| Class | Responsibility |
|---|---|
| `Core/Domain/Ledger` | Load, save, delete, undo, restore. Applies all business rules. Raises `Changed`. |
| `Core/Domain/Rules`, `DataValidator` | Field validation (Danish messages) and full-dataset validation before restore. |
| `Core/Domain/TripPlanner` | Creates automatic clinic trips; finds the automatic trip on a date. |
| `Core/Domain/BackupReminder` | When to show the weekly backup card. |
| `Core/Reports/ReportCalculator`, `History` | Annual figures, outstanding list, stable report references, history filtering. |
| `Core/Excel/XlsxWorkbook`, `XlsxReader` | Dependency-free `.xlsx` writer and reader. |
| `Core/Excel/PrivateWorkbook`, `TaxReportWorkbook`, `WorkbookImporter` | The two workbooks and restore-from-Excel. |
| `Core/Backup/BackupWriter`, `BackupReader` | ZIP backup creation and complete validation. |
| `Core/Storage/DataSerializer`, `DataMigrator`, `AppJsonContext` | JSON for IndexedDB and backups (source-generated, trimming safe), migrations, fingerprint. |
| `Core/Text/Dk`, `Names` | Danish money/date/duration formatting and parsing; name normalisation. |
| `App/Services/IndexedDbStore` | `IDataStore` over `wwwroot/js/storage.js`. |
| `App/Services/BrowserFiles`, `BrowserApp` | Share/download/preview/compress; theme, preferences, install state, updates. |
| `App/Services/AppState` | Loads the ledger at start-up; shows a friendly screen if storage is unavailable. |

## IndexedDB schema

Database `sofies-univers`, IndexedDB version **1**. One object store per record type, all with
`keyPath: "id"`:

| Store | Content |
|---|---|
| `sessions` | `Session` records |
| `payments` | `Payment` records (reference `sessionId`) |
| `elementTypes`, `expenseCategories`, `paymentMethods` | `LookupItem` records |
| `expenses` | `Expense` records (attachment metadata inline) |
| `trips` | `Trip` records |
| `settings` | three records: `{id:"app"}` = `AppSettings`, `{id:"backup"}` = `BackupStatus`, `{id:"meta", dataVersion}` |
| `attachments` | `{id, fileName, contentType, data: Uint8Array}` – receipt bytes |

Records are the JSON produced by `AppJsonContext` (camelCase, enums as strings, dates as
`yyyy-MM-dd`, timestamps ISO 8601 UTC). See [data-model.md](data-model.md).

Writes:

* `commit(ops, attachmentMeta, bytes)` – put/delete operations across any stores plus at most one
  attachment, in **one** `readwrite` transaction.
* `replaceAll(allStores, keepAttachmentIds)` – restore: one transaction over **all** stores that
  clears and refills every record store and deletes attachments not referenced by the new data.
  Incoming receipts are written first with `putAttachment` ("staging"); unreferenced attachments are
  harmless and are removed at the next start-up (`Ledger.TryCleanUpOrphanAttachmentsAsync`).
  If the replacement transaction aborts, IndexedDB rolls everything back. This is covered by
  `BackupTests.Failed_restore_leaves_current_data_intact` and the browser test
  "IndexedDB replacement is atomic".

Business data is never stored in the service worker cache or in `localStorage`. `localStorage`
holds only: the chosen theme and the last used session elements, duration and payment method.

## Schema versions and migrations

There are two independent version numbers:

* **IndexedDB version** (`storage.js`, `DB_VERSION`): the set of object stores. Bump only when
  adding/removing stores; extend `onupgradeneeded`.
* **Data version** (`AppData.CurrentVersion`, stored in `settings/meta` and in every backup): the
  JSON shape of records. To change it, add a step to `DataMigrator.Steps` that edits the JSON in
  place, bump `CurrentVersion`, and add a test. The same step upgrades data in IndexedDB at
  start-up **and** old backup files during restore.

On start-up, data from a newer app version is refused (never overwritten), and storage with records
but without settings is refused rather than re-seeded. A migrated dataset is written back with the
atomic `replaceAll`. Recommended practice before releasing a migration: ask Sofie to make a backup
(the old app version can always restore its own backup; the new version can restore old backups).

## JavaScript interop

Kept deliberately small (about 400 lines in total):

* `storage.js` – IndexedDB open/upgrade, `loadAll`, `commit`, attachments, `replaceAll`,
  storage estimate/persistence, and a self-test on a separate database.
* `files.js` – `navigator.share` with files, download fallback, object URLs for previews, opening a
  receipt, and photo down-scaling with a canvas (longest side 2400 px, JPEG quality 0.85).
* `app.js` – theme attribute and `theme-color`, small preferences, standalone/iOS detection,
  `confirm()` for unsaved changes, service worker update notification.

## Share sheet and user gestures

Safari only opens the share sheet as a direct result of a tap. Excel exports are generated
synchronously inside the tap handler; if the browser still reports `NotAllowedError`, the app shows
a second button "Filen er klar – tryk for at dele eller gemme". Backups are always two taps
("Lav backup nu", then "Gem eller send backup-filen") because collecting receipts is asynchronous.

## Error handling

* Storage failures surface as `StorageException` → toast "Det kunne ikke gemmes. Prøv igen." The
  in-memory data is unchanged.
* If IndexedDB is unavailable at start-up (e.g. some private modes), the app shows a clear message
  instead of a broken UI.
* Restore errors list exactly what is wrong and state that current data was not changed.
* The global Blazor error bar says data is not affected and offers a reload.

## Themes

`wwwroot/css/app.css` defines all colours, decoration and the display font as CSS custom
properties on `:root[data-theme=…]`. Components never hard-code colours, so layout and behaviour
are identical in all five universes. The theme is applied before Blazor starts (inline script in
`index.html`) to avoid a flash, and changing it is a single attribute change – instant, no saving.
Reduced-motion preferences disable transitions. Status is never shown by colour alone (badges
contain text such as "Betalt" / "Mangler 300,00 kr.").
