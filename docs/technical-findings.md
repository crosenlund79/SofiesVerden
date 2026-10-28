# Technical findings (Phase 0 feasibility spike)

Date: 2026-10-28. Toolchain: .NET SDK 10.0.113 (.NET 10 LTS, runtime 10.0.13), Blazor WebAssembly.

The spike was kept in the product as the **Systemtjek** page (`/systemtjek`, reachable from
Indstillinger → Systemtjek). It runs the same checks on any device, which is how the real iPhone
is verified (see `docs/iphone-test-checklist.md`).

| Capability | How | Desktop Chromium (headless, automated) | iPhone Safari / installed PWA |
|---|---|---|---|
| Blazor WebAssembly as PWA | Template service worker, rewritten to work under a sub path | ✅ builds, publishes, runs | ⏳ manual test |
| IndexedDB write/read | `wwwroot/js/storage.js`, one transaction per change | ✅ 256 KB binary + text round trip; app data survives reload | ⏳ manual test |
| SHA-256 in WebAssembly | `System.Security.Cryptography.SHA256` (managed implementation in .NET 8+) | ✅ | ⏳ (same code as desktop) |
| Real `.xlsx` generation | Own small writer (`Core/Excel/XlsxWriter.cs`) using `ZipArchive` + `XmlWriter` | ✅ generated in browser; validated against the official Open XML schema in tests | ⏳ open in Excel/Numbers |
| ZIP with XLSX + other files | `System.IO.Compression.ZipArchive` | ✅ generated and re-read in the browser | ⏳ |
| Share / download | `navigator.share({ files })` when `canShare` allows, else `<a download>` | ✅ download fallback (Chromium on Linux has no file sharing) | ⏳ share sheet → Mail / Gem i Filer |
| Import of an uploaded file | Blazor `InputFile` → bytes → `BackupReader` / `WorkbookImporter` | ✅ | ⏳ choose from Files / iCloud Drive |
| Production build | `dotnet publish -c Release` | ✅ ~14 MB uncompressed framework, Brotli/Gzip precompressed | – |

## Decisions taken in the spike

* **Excel library.** ClosedXML depends on fonts/graphics libraries and is heavy for a phone download;
  DocumentFormat.OpenXml works in WebAssembly but adds several MB. The app only needs to *write*
  well-formed workbooks with numbers, dates, styles, frozen headers and filters, and to *read back
  its own* workbook. A ~500-line writer/reader in `SofiesUnivers.Core/Excel` does exactly that with
  zero dependencies. To make sure the output is genuinely valid, the test project uses
  DocumentFormat.OpenXml (MIT, test-only) and its `OpenXmlValidator` against the Office 2019 schema.
  The reader understands both inline strings (our files) and shared strings (files re-saved by Excel).
* **Globalization.** `InvariantGlobalization=true` and explicit Danish formatting in `Core/Text/Dk.cs`.
  This avoids downloading ICU data (Danish is not in the default ICU shard) and makes formatting
  identical in tests and in the browser.
* **JS interop is minimal**: `storage.js` (IndexedDB), `files.js` (share/download/preview/image
  compression), `app.js` (theme, small preferences, install state, update notice). Everything else is C#.
* **No backend.** Nothing in the spike required one.

## Known browser caveats (documented, not hidden)

* `navigator.share` must be called directly from a tap. The app therefore prepares a file first and
  then shows a separate **Del / gem** button. If the share sheet is unavailable or refused, the file is downloaded.
* The share sheet closing does **not** prove the file was saved. The app records "lavet", "delt/hentet"
  and "bekræftet af Sofie" separately.
* Safari may evict website data for sites not used for a while; home-screen web apps are treated
  better. The app asks for `navigator.storage.persist()` but does not rely on it.
