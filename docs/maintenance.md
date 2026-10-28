# Maintenance

## Setting the clinic route and distance

Everything is configuration data, not code:

* **In the app:** ⚙️ → *Rute til klinikken*. Names and addresses of home and clinic, round-trip km,
  whether clinic trips are registered automatically, and whether new clinic trips are included in the
  tax report by default.
* The distance starts as **not set**. Measure the verified driving distance (round trip) and enter
  it. Trips registered before that have no km; the page then offers "Udfyld dem med … km" – an
  explicit action, because changing the standard never rewrites history by itself.
* The full street address of the home (currently "Soluret, Odense SØ") is entered on the same page.
* Defaults for a brand-new installation are in `src/SofiesUnivers.Core/Model/DefaultData.cs`
  (no distance on purpose).

The model already supports more locations (`AppSettings.Locations`) and stores route labels and km
on each trip. A second clinic would need: a way to choose the destination in the session form, and a
rule for which route an automatic trip uses. No data migration is needed for that.

## Session elements, expense categories and payment methods

* New values can be added directly in the session/expense forms ("+ Ny type", "+ Ny kategori") or
  under ⚙️ → *Sessionstyper, kategorier og betalingsmetoder*.
* Renaming changes the name everywhere (records store the id). Use it for corrections.
* "Skjul" archives a value: it disappears from the pickers but stays on old records and in reports.
* Initial values for new installations: `DefaultData.cs`.

## Extending the app safely

1. **Rules go in Core.** Put validation and business logic in `SofiesUnivers.Core` (usually
   `Ledger`/`Rules`) with tests. Pages only collect input and display results.
2. **Changing stored data shape.** Add a migration step in `DataMigrator`, bump
   `AppData.CurrentVersion`, and add a test with a JSON sample of the old shape. The migration then
   applies to IndexedDB and to old backups. Never reuse a property name with a new meaning.
3. **New record type.** Add a store name in `StoreNames` and in `storage.js` (`RECORD_STORES`),
   bump `DB_VERSION` in `storage.js` and create the store in `onupgradeneeded`; add it to `AppData`,
   `DataSerializer`, `Ledger.Apply`, `DataValidator`, the private workbook, the importer and the backup
   counts. The backup round-trip tests will tell you if something is missing.
4. **Workbook columns.** The importer reads columns by header name, and the restore checksum covers
   the whole dataset, so adding columns is safe; renaming headers used by `WorkbookImporter` is not
   (bump `PrivateWorkbook.FormatVersion` and keep reading the old name).
5. **Privacy.** Anything that may identify a client must not reach `TaxReportWorkbook`. Extend
   `ExcelTests.Tax_report_excludes_nicknames_and_ids_everywhere` when adding fields.
6. **Themes.** Use the CSS custom properties only. A new colour token must be defined for all five
   themes; check contrast (text ≥ 4.5:1).
7. **Run everything** before merging: `dotnet test`, publish, and `npm test` in `tests/e2e`.
   CI does the same.
