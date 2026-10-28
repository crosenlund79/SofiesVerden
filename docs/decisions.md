# Decisions and known limitations

A short log so future iterations do not accidentally contradict what was agreed. Newest last.

## Decisions

| # | Decision | Reason |
|---|---|---|
| D1 | Blazor WebAssembly PWA on **.NET 10 (LTS)**, static hosting, no backend. | As specified; verified in the Phase 0 spike. |
| D2 | Two projects (`Core`, `App`) plus tests; no repositories/mediators. | Small and understandable; all rules testable without a browser. |
| D3 | Whole dataset in memory; every change is one IndexedDB transaction; memory is updated only after a successful write. | Data is small; gives simple, consistent, testable behaviour. |
| D4 | **Own XLSX writer/reader** instead of ClosedXML/Open XML SDK in the app. Output validated with the Open XML SDK in tests. | Zero dependencies and small download on the phone; proven valid against the official schema. |
| D5 | `InvariantGlobalization` + explicit Danish formatting (`Dk`). | No ICU download; identical results in tests and browser. |
| D6 | In the ZIP backup, **`Data/sofies-univers-data.json` is authoritative**; the workbook is complete and human-readable. | Exact round-trip, same migrations as IndexedDB. |
| D7 | Restore from the app's own workbook is supported only if its **checksum matches** (unmodified data). Receipts are not included. | "Never silently interpret a modified spreadsheet as a safe backup." |
| D8 | **Deleting a session deletes its payments**, after a confirmation that names them; undo is offered. | A payment without a session would be meaningless and break balances. |
| D9 | **Overpayment is prevented** (not stored as credit). | Simplest correct rule; the message tells Sofie what to fix. |
| D10 | One clinic trip per date. Automatic trip only for the **first** session on a date; removed automatically only if it is still untouched and the date has no sessions left. Editing a trip makes it manual. | Avoid duplicates and never delete Sofie's own records silently. |
| D11 | Route distance is **unknown until entered**; trips without km are flagged; filling them later is an explicit action. | Never invent a distance; never rewrite history implicitly. |
| D12 | "Medtag i skatteoversigt" defaults to **on** for new clinic trips (setting on the route page) and manual trips, and the report states it is Sofie's own classification, not a deduction assessment. | Practical default; Sofie/accountant decide. *Owner may want the default off – one switch.* |
| D13 | Lookups (elements, categories, methods) are referenced by id, renamed globally and archived instead of deleted. | Consistent history; no orphaned references. |
| D14 | Theme and "recent choices" in `localStorage`; not part of the backup. | Non-critical preferences, instant theme on start. |
| D15 | Backup status tracks *generated*, *shared/downloaded* and *confirmed by Sofie* separately; only confirmation counts for the reminder. | The share sheet cannot prove a file was saved. |
| D16 | Reminder: changes after the last confirmed backup and that backup ≥ 7 days old (first time: 1 day after the first registration); snooze 3 days. Shown as a home-screen card. | Gentle, roughly weekly. |
| D17 | Excel exports open the share sheet directly from the tap; if Safari refuses, a second button appears. Backups are always two taps. | Safari's user-gesture rule. |
| D18 | Receipt photos are scaled to max 2400 px (JPEG 85 %) in the browser when large/HEIC/PNG. | Saves storage and backup size; receipts stay readable. |
| D19 | Report references `S-YYYY-NNN` / `U-YYYY-NNN` instead of ids in the authority report. | Useful for the accountant, contain no client information. |
| D20 | **PDF report not implemented in V1.** | Excel is the priority; a PDF must be a real report and is better done after feedback. |
| D21 | GitHub Pages under `/<repository>/` with `tools/prepare-pages.js` (base href, service worker hash, 404.html, .nojekyll). | Free static HTTPS hosting; verified by browser tests incl. offline start. |

## Known limitations

* **Single device, browser storage.** Uninstalling the home-screen app, clearing Safari website data,
  a new phone, or (rarely) browser storage pressure can delete data. `navigator.storage.persist()` is
  requested for the installed app but is not a guarantee. Backups are the mitigation.
* Safari and the home-screen app have separate storage. Use only the home-screen app.
* **No app-level encryption or login.** Anyone with access to the unlocked phone can see the data.
* Several tabs/windows with the app open at the same time are not coordinated (last write wins per
  record). Normal use on an iPhone has one window.
* The share sheet cannot confirm that a file was saved; Sofie confirms manually.
* Unsaved form input is protected against navigating away inside the app (a question is asked), but
  not against iOS closing the app in the background while a form is half filled in.
* Restore needs the whole ZIP in memory; very large backups (hundreds of MB of receipts) may be slow
  or fail on older iPhones. Receipts are compressed to keep backups small. Failure leaves data intact.
* Expense descriptions and trip purposes are free text and appear in the tax report. Sofie should not
  write client names there.
* HEIC files that Safari cannot convert are stored as HEIC; they may not preview on Windows.
* Browser tests run in Chromium only. Safari/iOS behaviour must be verified manually
  ([iphone-test-checklist.md](iphone-test-checklist.md)).
* Hippie/Diva/Victorian display fonts are iOS system fonts; other platforms fall back to similar fonts.

## Danish tax/accounting – outside V1 (not decided by the app)

* The legal basis for VAT exemption is not established; the app has a business-level "momsfritaget"
  setting and **never** calculates output VAT or deducts purchase VAT.
* Possible payroll tax (lønsumsafgift), digital bookkeeping requirements under the Danish Bookkeeping
  Act, retention periods for receipts, and the deductibility of home–clinic driving are **not**
  determined. The app is not a registered digital bookkeeping system.
* No km rate (kørselsfradrag/kørselsgodtgørelse) is calculated – only km are reported.
* Reports are labelled "Regnskabsoversigt – materiale til skatteopgørelse" and say they are not
  approved by the authorities.

## Open questions for the project owner

1. Verified round-trip km and the full home address (enter in ⚙️ → Rute til klinikken).
2. Should new clinic trips be included in the tax report by default (currently yes, D12)?
3. Hosting: public repository with GitHub Pages, paid plan for private Pages, or another static host?
4. Should payments made in advance of the session date be allowed (currently yes)?
5. Is a PDF version of the annual report wanted after the Excel version has been tried?
