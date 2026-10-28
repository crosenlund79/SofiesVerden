# Changelog

## 1.0.0 – first version (2026-10-28)

### Added
* Blazor WebAssembly PWA (.NET 10) with Danish UI, installable from Safari, works offline.
* Five personality universes (Boheme, Victorianer, Hippie, Muse, Diva) – one tap, instant, remembered.
* Sessions: date, 1–3 elements (inline creation), duration (quick choices or custom), free price,
  optional nickname, full payment proposed by default; edit, delete with undo.
* Payments: partial, multiple, different methods, later payments; outstanding overview; overpayment prevented.
* Driving: automatic clinic round trip on the first session of a date, manual trips, tax-report flag,
  configurable route (distance unknown until entered).
* Expenses: categories (inline creation), one optional photo/PDF receipt with preview, replace, remove.
* Overblik: annual figures by the correct dates, monthly payments, categories, methods; history with filters and search.
* Excel: complete private workbook and annual "Regnskabsoversigt" without client nicknames.
* Backup: ZIP with workbook, receipts, data file and checksums; generated/shared/confirmed status;
  weekly reminder card. Restore: full validation, summary, safety backup, atomic replacement.
* Restore from the app's own unmodified workbook (without receipts).
* Systemtjek page for verifying browser capabilities on the device.
* 132 unit/integration tests, 18 browser tests, CI and GitHub Pages deployment.
