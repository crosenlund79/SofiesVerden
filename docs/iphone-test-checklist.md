# iPhone test checklist (manual)

Automated tests run in desktop Chromium. These checks must be done on a **real iPhone** with a
current iOS, in the installed home-screen app, before Sofie relies on the app. Record the iOS
version, date and result for each item.

## A. Install and start
- [ ] Open the deployed URL in Safari. The app loads; the theme is Boheme.
- [ ] Del → Føj til hjemmeskærm. Icon and name "Sofies Univers" look right.
- [ ] Open from the icon: full screen (no Safari bars), status bar readable, nothing hidden behind the notch or home indicator.
- [ ] ⚙️ → Systemtjek → **Kør systemtjek**: all six checks ✅. Note what "Deling af filer" and "Vedvarende lager" say.
- [ ] Airplane mode on → close the app completely → open it again: it starts (offline). Airplane mode off.

## B. Themes
- [ ] Each of the five personalities changes colours instantly with one tap; no dialog.
- [ ] Close and reopen the app: the last personality is still active and there is no flash of another theme.
- [ ] Muse (dark): all text and inputs are readable, including the date picker.
- [ ] Settings → Accessibility → Display & Text Size → larger text: screens still usable (no overlapping, nothing cut off).

## C. Sessions and payments
- [ ] Ny session: the number keyboard appears for the price; "800" and "800,50" are accepted.
- [ ] The native date picker works; choose an earlier date.
- [ ] Select 1, 2 and 3 elements; a 4th cannot be selected. Add a new type with "+ Ny type".
- [ ] Save a fully paid session: success message incl. clinic trip note.
- [ ] Save a session with 300 of 800 paid → Udestående shows 500 → Registrér betaling 500 → "Alt er betalt".
- [ ] Two payments with different methods on one session.
- [ ] Start a new session, type a price, tap a tab in the bottom bar → a "forlade siden" question appears.
- [ ] Edit and delete a session; "Fortryd" brings it back.

## D. Expenses and receipts
- [ ] Ny udgift → "Tag billede eller vælg fil" offers **Tag billede**, **Fotobibliotek** and **Vælg fil**.
- [ ] Take a photo of a receipt: preview appears; saved; "Vis" shows a readable receipt.
- [ ] Choose a PDF from Filer: accepted; "Vis" opens it.
- [ ] Choose a large photo (> 5 MB) from the library: accepted and stored smaller; text still readable.
- [ ] Replace and remove a receipt.

## E. Driving
- [ ] ⚙️ → Rute til klinikken: enter the verified km and the full home address. Save.
- [ ] "Udfyld dem med … km" fills earlier clinic trips that had no km.
- [ ] Register a manual trip (Indkøb, Anden rute). Toggle "I skatteoversigt".

## F. Excel
- [ ] Overblik → Skatteoversigt: the **share sheet** opens. "Gem i Filer" → file saved in iCloud Drive.
- [ ] Open the file on a **Windows PC in Microsoft Excel**: opens without repair warning; dates are dates; amounts are numbers (sum works); filters on the header rows; no nicknames anywhere (also File → Info → Properties).
- [ ] Komplet regneark (privat): opens in Excel and in Numbers on the iPhone; nicknames present.
- [ ] Mail an export to yourself from the share sheet.

## G. Backup and restore
- [ ] Backup → Lav backup nu → Gem eller send backup-filen → Gem i Filer (iCloud Drive). Then "Ja, backuppen er gemt". Status shows the date.
- [ ] Cancel the share sheet once: the app does **not** claim the backup was saved.
- [ ] Unzip the backup on a computer: Sofies-Regnskab.xlsx, Kvitteringer/, Data/, Backup-info.json, LÆS MIG.txt.
- [ ] Restore that backup on a **second device or after deleting the home-screen app and reinstalling**: summary shows correct counts; after restore all sessions, payments, receipts (photos visible), trips and settings are back.
- [ ] Try to restore a random ZIP or a photo: refused with a clear message; data unchanged.
- [ ] Restore from the private .xlsx: works, with the warning that receipts are missing.
- [ ] Edit a cell in the private .xlsx in Excel, save, try to restore it: refused ("ændret").

## H. Persistence
- [ ] Register data, force-quit the app, restart the phone: data still there.
- [ ] Leave the app unused for 8+ days, then open it: data still there (home-screen apps should be exempt from Safari's 7-day eviction).
- [ ] With data newer than the last confirmed backup and the backup older than 7 days, the reminder card appears on the home screen; "Påmind mig senere" hides it.

## I. Update
- [ ] Deploy a new version. Open the app (maybe twice): "En ny version af appen er klar" → Opdatér → app reloads, data intact.
