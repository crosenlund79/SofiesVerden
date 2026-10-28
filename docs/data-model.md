# Data model and business rules

All ids are random GUIDs. Money is `decimal` (DKK, at most two decimals). Dates are `DateOnly`
(the business date chosen by Sofie); timestamps are `DateTimeOffset` in UTC. Source:
`src/SofiesUnivers.Core/Model`.

## Records

### Session
| Field | Notes |
|---|---|
| `id` | |
| `date` | Session date. Drives which year/month the session value belongs to. |
| `elementIds` | 1–3 ids of session element types, in the order selected. |
| `durationMinutes` | One total duration (1–1440). No duration per element. |
| `price` | Freely entered total price. Never calculated from duration or elements. |
| `clientNickname` | Optional free text (max 80). Treated as personal data. |
| `createdAt`, `modifiedAt` | |

### Payment
| Field | Notes |
|---|---|
| `id`, `sessionId` | A session has zero or more payments. |
| `date` | Actual payment date – drives cash-flow reporting. |
| `amount` | > 0 |
| `methodId` | Payment method lookup. Payments of one session can use different methods. |
| `createdAt`, `modifiedAt` | |

### LookupItem (session element types, expense categories, payment methods)
`id`, `name`, `isArchived`, `sortOrder`, `createdAt`. Records reference lookups by id, so renaming a
value shows the new name everywhere (it is a correction). Values are archived ("Skjul"), never
deleted, so history stays complete.

### Expense
`id`, `date`, `amount` (> 0), `categoryId`, optional `description` (max 500), optional `attachment`
(`id`, `fileName`, `contentType`, `sizeBytes`, `sha256`), `createdAt`, `modifiedAt`. The receipt
bytes are in the `attachments` store under `attachment.id`.

### Trip
| Field | Notes |
|---|---|
| `id`, `date` | |
| `purpose` | Free text with suggestions (Klinik / sessioner, Rengøring, Indkøb, Administration, Andet). |
| `fromLabel`, `toLabel` | Copied onto the record – later route changes never rewrite history. |
| `distanceKm` | Total km for this record (round trip). `null` when unknown – never invented. |
| `includeInTaxReport` | "Medtag i skatteoversigt" – Sofie's own classification. |
| `origin` | `Automatic` (created by the app, untouched) or `Manual` (registered or edited by Sofie). |
| `isClinicTrip` | This is the single clinic round trip of the date. |

### AppSettings (`settings/app`)
`locations` (home and clinic: name, address, kind), `clinicRoute` (`fromLocationId`, `toLocationId`,
`roundTripKm` – `null` until verified, `autoRegisterTrips`, `includeInTaxReportByDefault`),
`vatExempt` (informational; no VAT is ever calculated).

### BackupStatus (`settings/backup`)
`trackingStartedAt`, `lastDataChangeAt`, `lastGeneratedAt`, `lastSharedAt`, `lastConfirmedAt`,
`reminderSnoozedUntil`. Not part of backups; updating it does not count as a data change.

## Rules

### Sessions
* 1–3 distinct, existing elements; duration 1–1440 minutes; price 0–10.000.000 with ≤ 2 decimals.
* New element names typed in the form are added with "+ Tilføj". Names that differ only by case or
  whitespace are treated as the same (an archived match is restored instead of duplicated).
* Default form values: today's date, last used elements and duration, and **one proposed payment
  for the full price** with the last used method. Sofie can change the amount, remove it or add more.
* Saving an unchanged session writes nothing.

### Payments and outstanding
* Outstanding = price − sum of payments, never negative.
* Overpayment is **prevented**: the form, "Registrér betaling" and price changes are rejected with a
  message that states the amounts. (Lower the price below what is paid → rejected.)
* Editing a session never changes its payments implicitly. A payment disappears only when Sofie
  removes it in the form (explicitly) or deletes it.
* **Deleting a session deletes its payments** (a payment cannot exist without its session). The
  confirmation dialog states the number and total of payments that go with it, and the deletion can
  be undone from the toast ("Fortryd").

### Driving
* When a session is saved on a date that had **no other sessions**, and there is no clinic trip on
  that date (manual or automatic), one automatic clinic round trip is created with the configured
  route labels and distance (possibly `null`).
* A second session the same day creates nothing. If Sofie deleted that day's trip herself, adding
  another session that day does not bring it back.
* A manual trip to the clinic (standard route) on the date counts as the clinic trip. Only one clinic
  trip per date is allowed; other trips (other routes) are unlimited.
* When a date loses its last session (deletion or date change), an **automatic** trip that Sofie
  never edited is removed and the toast says so. Manual or edited trips are never removed.
* Changing the configured distance affects only new trips. Trips registered while the distance was
  unknown can be filled in explicitly ("Udfyld dem med … km").
* `includeInTaxReport` is independent of the trip existing; the tax report contains only trips with
  the flag on, the private workbook contains all trips.

### Expenses and receipts
* At most one receipt: JPEG, PNG, HEIC/HEIF, WebP, GIF or PDF – detected from the file content.
* Picked files up to 40 MB are accepted; photos are scaled down in the browser (longest side
  2400 px, JPEG 85 %), and the stored file must be ≤ 15 MB.
* Replacing or removing a receipt deletes the old bytes in the same transaction.

### Reports (per year)
* Session value and count by **session date**; payments received by **payment date** (including
  payments for sessions from other years); expenses by **expense date**; driving by **trip date**.
* "Udestående" for a year = what is still unpaid today on sessions dated in that year.
* Report references (`S-2026-001`, `U-2026-001`) are numbered by date within a year and contain no
  client information.
