# Split Everything — Project Spec

## Overview
**Split Everything** — a full-featured, self-hosted expense-sharing application (Settle Up clone), mobile-first, private use (owner + invited people only), deployed on existing homelab infrastructure.

---

## Tech Stack
- **Backend:** ASP.NET Core 10 Web API
- **Frontend:** Vue 3 + Tailwind CSS, wrapped in Capacitor for native iOS/Android builds (single codebase, native shell)
- **Database:** PostgreSQL
- **Real-time sync:** SignalR (WebSocket), with delta-batch reconnect fallback
- **Deployment:** Docker Compose → GHCR → GitHub Actions → Portainer webhook, behind Traefik reverse proxy

### Native app strategy (Capacitor)
- Pure PWA rejected: iOS aggressively suspends background web apps, breaking reliable SignalR sync and making iOS Web Push fragile (16.4+ only, manual "Add to Home Screen" required, unreliable delivery)
- Fully native (separate Swift + Kotlin apps) rejected: doubles maintenance for a solo-maintained, privately-used app with no real benefit at this scale
- Capacitor wraps the existing Vue 3 app in a thin native shell — one codebase, but gains:
  - Real push via APNs/FCM (Capacitor Push Notifications plugin) instead of relying solely on iOS Web Push
  - Proper background execution hooks for the offline sync engine
  - Installable via TestFlight / a private APK now, with the option to publish to the App Store / Play Store later
- Browser/desktop access remains a PWA (installable, offline-capable) using the same codebase

---

## Authentication & Access
- Google OAuth only (no password storage) — verify Google ID token server-side, upsert user by Google `sub`, issue own short-lived JWT + refresh token
- **Invites:** magic link (emailed, tied to a group + pending membership) → recipient signs in with Google → auto-joins group. Magic link is the invite mechanism; Google is still the only auth method.
- **QR code invite:** alternate presentation of the same magic-link invite flow
- **Scope:** private — owner + invited people only, no multi-tenant isolation needed
- **Network exposure:** public internet via Traefik + existing domain, gated entirely by Google auth (no Tailscale-only restriction)

---

## Core Data Model
- `Users` — Google sub id, email, display name, avatar
- `Groups` — name, currency, members, archived flag, lineage (for merge/split history)
- `GroupMembers` — user↔group, role
- `Expenses` — payer, amount, currency, description, date, category, receipt reference, group, recurrence rule (nullable), full edit history
- `ExpenseSplits` — expense↔user, split type (equal / percentage / shares / exact amount / itemized), amount owed
- `ExpenseItems` — optional line items for itemized splitting (who had what)
- `Settlements` — payer→payee, amount, date, group
- `ActivityLog` — audit trail for activity feed (expenses, settlements, edits, comments)
- `ExpenseComments` — threaded comments on a specific expense
- `SyncLog` — per-entity vector clock / operation log entries, used for offline conflict resolution and group merge/split reconciliation. Compacted yearly: settled/reconciled history older than one year is collapsed into a snapshot and trimmed from the live log, to keep it from growing unbounded

**Design note:** the `SyncLog` schema must be designed up front to support group merge and split operations (reconciling or partitioning independent operation logs without breaking causality), not retrofitted later.

---

## Feature List

### Expenses
- Splits: equal, percentage, shares, exact amount, itemized (line-by-line, e.g. "who had the appetizer")
- Recurring expenses — scheduled auto-creation (rent, subscriptions, etc.)
- Receipt photo attachment
- Threaded comments per expense
- Categories with icons + spending-by-category breakdown

### Debts & Settlements
- Simplified debts — greedy graph-reduction algorithm to minimize number of transactions needed to settle a group
- Record settlements, auto-adjust balances
- Per-group and overall net balance across all groups
- Debt nudges — push reminder to someone who owes money

### Groups
- Create/invite via magic link or QR code
- Multiple currencies per group, with conversion to group base currency
- Archive a group (freeze without deleting)
- Merge two groups (reconciles independent sync logs into one)
- Split a group (partitions history, preserves causality)
- Transfer a transaction between groups — moves full history (edits, comments, receipt, audit trail, vector-clock lineage) intact, not a fresh recreation

### Import
- Settle Up CSV import wizard:
  1. Upload the per-group CSV export (Android built-in export, or forwarded email export)
  2. Parse header row, let user confirm/remap columns (layout varies by app version/locale) — use a permissive CSV parser (e.g. CsvHelper with lenient config)
  3. Preview parsed expenses in a table before committing; flag unparseable rows
  4. Match/create group members by display name (Settle Up exports use names, not emails), with manual override for typos
  5. Commit: bulk-insert into `Expenses` + `ExpenseSplits`, preserving original dates

### Statement Import (Client-Side Only)
- User uploads a bank/credit-card statement (PDF or CSV) directly in the browser
- **Privacy constraint: the original statement file never leaves the device.** All parsing happens client-side — no upload to the backend, no third-party API calls with the file
- Parsing: PapaParse for CSV; PDF.js (text extraction) for PDF statements, run entirely in the frontend
  - Scanned/image-based PDFs: Tesseract.js (client-side OCR) fallback when text extraction yields nothing usable
  - Bank statement layouts vary widely — support bank-specific parsing profiles where feasible, and always fall back to the same manual column-mapping step used in the Settle Up CSV import, since fully automated table extraction from PDFs is inherently unreliable
  - Run parsing/OCR in a Web Worker so a multi-page statement doesn't freeze the UI thread, especially important on mobile via Capacitor
- Transaction extraction: regex/heuristic parsing of date, description, amount per line
- **Auto-categorization:** local keyword/merchant-to-category ruleset (e.g. "UBER EATS" → Food), starting from a built-in default set and improving from the user's manual corrections over time. Ruleset is user preference data — stored and synced via the existing vector-clock sync log, not the statement content
- **Split detection:** heuristic match against the user's own expense history — if a merchant/amount pattern was previously split with a specific group, suggest the same split; otherwise default to "personal, not split" for manual review
- **Duplicate detection:** before commit, fingerprint each parsed row (date + amount + description) against existing expenses to catch overlapping statement periods or transactions already entered manually
- **Foreign currency detection:** flag lines in a different currency than the statement's base (e.g. a EUR purchase on a CAD statement) and route them through the existing Frankfurter conversion
- Review wizard (same pattern as Settle Up import), extended with:
  - Per-row group assignment (a personal card statement will span multiple groups, not just one)
  - Editable category + split assignment per row
  - Bulk actions: ignore, mark as already recorded, select-multiple-then-categorize
  - Nothing commits until confirmed
- Only the final, user-confirmed structured expense records — never the source file — are sent to the API on commit
- **Data hygiene:** raw statement content (parsed text, OCR output, in-memory/IndexedDB staging data) is cleared immediately after commit or cancel — it should not persist on-device beyond the review session

### Offline Support
- Full offline-first
- Conflict resolution via vector clocks — each client keeps a per-device logical clock; syncs deltas with causality tracking
- True conflicts (same field edited concurrently on two devices) are flagged for manual resolution, never silently overwritten
- Sync transport: SignalR (WebSocket) for live sync when online; falls back to delta-batch sync on reconnect when offline clients come back online

### Notifications
- Primary: native push via Capacitor (APNs for iOS, FCM for Android)
- Fallback: Web Push via VAPID keys for anyone using the app in a plain browser
- Notify on: new expenses, settlements, debt nudges

### Stats Dashboard
- Spending over time
- Spending by category
- Who-owes-whom trends over time

### Theming & UX
- Mobile-first: bottom tab nav (Groups / Activity / Add Expense FAB / Profile), add-expense as full-screen modal/sheet
- PWA: installable, manifest + service worker
- Dark mode by default; light mode toggle in settings

### Data & Backups
- Receipts: stored on local disk, behind a storage abstraction interface (so swapping to S3/MinIO later is a config change, not a rewrite)
- Backups: daily PostgreSQL dumps, 30-day retention
- Own-data export/delete (GDPR-style, good practice for personal data app)

---

## Locked Decisions Table

| Area | Decision |
|---|---|
| Backend | ASP.NET Core 10 Web API |
| Frontend | Vue 3 + Tailwind, wrapped in Capacitor for native iOS/Android + PWA for browser |
| DB | PostgreSQL |
| Auth | Google OAuth only; magic-link invites → Google sign-in → auto-join |
| Receipts | Local disk, behind storage abstraction (swappable later) |
| Currency | Frankfurter API, daily cache |
| Offline | Full offline-first, vector clocks for conflict resolution |
| Sync | WebSocket (SignalR) live sync + delta-batch reconnect fallback |
| Notifications | Native push via Capacitor (APNs/FCM), Web Push (VAPID) fallback for browser |
| Scope | Private — owner + invited people only |
| Access | Public internet via Traefik, gated by Google auth |
| Backups | Daily Postgres dumps, 30-day retention |
| Sync log compaction | Yearly — collapse settled history into a snapshot, trim live log |
| Import | Settle Up CSV import wizard (upload → map → preview → commit) |
| Statement import | Client-side only (PDF.js/PapaParse + Tesseract.js OCR fallback), Web Worker parsing, dedupe + FX detection, no file leaves device |
| Deploy | Docker Compose → GHCR → GitHub Actions → Portainer webhook |
| Recurring expenses | Scheduled auto-creation |
| Group lifecycle | Archive, merge, split (sync-log-aware history reconciliation) |
| Transaction transfer | Moves full history/audit trail between groups |
| Invites | Magic link + QR code |
| Stats | Full dashboard — spending over time, categories, who-owes-whom |
| Theming | Dark mode default, light mode toggle |

---

## Suggested Build Order
1. Data model + EF Core migrations, including `SyncLog` schema designed for merge/split/transfer from the start
2. Auth: Google OAuth + JWT issuance + magic-link invite flow
3. Core CRUD: groups, members, expenses, splits, settlements, debt-simplification algorithm
4. Offline sync engine: vector clocks, SignalR live sync, delta-batch reconnect fallback
5. Settle Up CSV import wizard
6. Client-side statement import (PDF.js/PapaParse parsing, categorization ruleset, split-detection heuristics)
7. Recurring expenses, group archive/merge/split, transaction transfer
8. Currency conversion (Frankfurter integration)
9. Web Push notifications (VAPID)
10. Stats dashboard
11. PWA polish: manifest, service worker, dark/light theming, QR invite UI
12. Capacitor wrapper: iOS/Android native shells, APNs/FCM push integration, TestFlight/private APK builds
13. Deployment: Dockerfile(s), GitHub Actions → GHCR → Portainer webhook, Traefik config, backup cron
