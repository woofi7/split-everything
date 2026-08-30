# Split Everything

A self-hosted expense-sharing app: shared groups, real splits, offline-first, with
a Settle Up importer and a bank-statement importer that never uploads the
statement.

Built to the spec in [docs/spec.md](docs/spec.md).

## What it does

- **Splits** equally, by percentage, by shares, by exact amount, or itemized
  line-by-line ("who had the appetizer"), with rounding that always sums to the
  total.
- **Settles up** with a greedy reduction to the fewest transfers, or shows the raw
  who-owes-whom view if you prefer it.
- **Works offline.** Every write lands locally and queues; conflicts from two
  devices editing at once are flagged for a person, never silently overwritten.
- **Imports** a Settle Up CSV export (upload, map, preview, commit) and a bank or
  credit-card statement parsed entirely in the browser.
- **Groups** can be archived, merged, split apart, and individual expenses moved
  between them carrying their full history.
- **Recurring expenses**, receipts, threaded comments, multi-currency
  with frozen rates, push notifications, and a stats dashboard.

## Layout

```
backend/     ASP.NET Core 10 API: Domain, Application, Infrastructure, Api, Tests
frontend/    Vue 3 + Tailwind PWA, wrapped by Capacitor for iOS and Android
infra/       Traefik notes, backup and restore scripts
scripts/     Coverage gate
docs/        The spec this was built from
```

The backend is layered so the rules can be tested without a web server or a
browser: `Domain` holds the money and sync algorithms with no dependencies,
`Application` holds the contracts, `Infrastructure` holds Postgres and the outbound
adapters, and `Api` is only transport.

## Running it locally

You need .NET 10, Node 24 and Docker.

```bash
# Postgres for development
docker run -d --name split-dev-postgres \
  -e POSTGRES_DB=split_everything \
  -e POSTGRES_USER=split \
  -e POSTGRES_PASSWORD=split \
  -p 5432:5432 postgres:17-alpine

# API on http://localhost:5080. It migrates and seeds on start.
cd backend
ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/SplitEverything.Api

# App on http://localhost:5173, proxying /api and /hubs to the API
cd frontend
npm install
npm run dev
```

If port 5432 is already taken by another project, publish Postgres somewhere else
and point the API at it:

```bash
docker run -d --name split-dev-postgres ... -p 5433:5432 postgres:17-alpine

ConnectionStrings__Postgres='Host=localhost;Port=5433;Database=split_everything;Username=split;Password=split' \
  ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/SplitEverything.Api
```

### Testing on a phone

`npm run dev` binds every interface, so a phone on the same network can open the
app at the machine's LAN address, for example `http://192.168.2.48:5173`. Vite
prints the address on start.

Only that port needs to be reachable: the API stays on localhost and the dev
server proxies `/api` and `/hubs` to it, so requests from the phone are
same-origin and no CORS setup is involved.

The `--host` flag lives on the dev script rather than in `vite.config.ts` because
Vite 8.2.2 ignores `server.host` from the config file.

Invite links and their QR codes are built from `Auth:AppBaseUrl`. In Development
a loopback host there is replaced at startup with an address other devices can
reach, so a scanned QR code lands on this machine rather than on the phone. The
API prints the address it chose.

A development box has many addresses, so the choice prefers an interface with a
gateway, which is what separates a real network from the Docker bridges, and
wireless first. Set `Auth__AppBaseUrl` to a non-loopback host to override it; a
host set deliberately is never rewritten.

Two things do not work over a plain LAN address, both because the browser reserves
them for secure contexts: the service worker, so no PWA install or offline shell,
and Web Push. Sign in with the development form; a Google OAuth client cannot list
an IP address as an authorised origin.

Everything else does work there, including the statement importer. It hashes each
row with SHA-256 to ask the server whether the transaction is already recorded,
and `crypto.subtle` is secure-context only, so the app carries its own SHA-256 for
that case. It is the same algorithm, tested against the published vectors and
against `crypto.subtle` itself, because the hashes are compared with the server's.

### Filling a development database

`scripts/seed-demo-data.py` creates a couple of groups that look like they have
been used: several months of expenses, more than one payer
so the charts have something to show, a settlement and a comment.

```bash
python3 scripts/seed-demo-data.py --email you@example.com --name You
```

Everything goes through the API rather than into the tables, so the result is
indistinguishable from a group people actually used: activity entries, sync log
rows, vector clocks and balances all come out of the same code paths the app runs.
Inserting rows directly produces a database that looks right and an activity feed
that is empty, which this project has already been caught by once.

It only sees the groups of the account it signs in as, so a group of the same name
owned by someone else will not stop it creating another. It never deletes anything.

### Signing in locally

`appsettings.Development.json` sets `Auth:AllowDevelopmentSignIn`, so the sign-in
page offers a form that takes an email address and nothing else. Use a second
address in another browser profile to act as another person and test sharing.

That is an authentication bypass, so it is guarded twice: the service refuses
unless the flag is on, and startup forces the flag off whenever the environment is
not Development. Both guards have tests, including one that sets the environment
variable and asserts the endpoint still answers 403.

For real Google sign-in, create an OAuth client in the Google Cloud console with
`http://localhost:5173` as an authorised origin, then set `Auth:GoogleClientId`
for the API and `VITE_GOOGLE_CLIENT_ID` for the app.

## Divergences from the spec

Categories were removed from the whole application after the spec was written:
there is no category on an expense, no category tables, no spending-by-category
breakdown, and no auto-categorisation ruleset in the statement importer. The
migration that removed them drops the column and both tables, so the categories
that had been set are gone.

`docs/spec.md` is kept as it was written rather than edited to match, so the
difference between what was asked for and what exists stays visible.

## Tests

The backend suite runs against a real Postgres in a throwaway container, because
the schema leans on jsonb columns, partial unique indexes and identity sequences
that the in-memory provider does not enforce.

```bash
cd backend
dotnet test                                    # 901 tests
dotnet test --settings coverlet.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory TestResults
python3 ../scripts/check-coverage.py 'TestResults/*/coverage.cobertura.xml'

cd frontend
npm test                                       # 1311 tests
npm run typecheck
npm run lint                                   # unused code and template mistakes
npm run test:coverage                          # enforces per-layer thresholds
```

Prefer `waitFor(condition)` from the view harness over `settle(n)` when a test
waits on asynchronous work. A fixed number of ticks passes on a quiet machine and
fails under load, which has cost real time here twice.

Coverage floors are enforced in CI: 95% of lines on the backend (100% on the
application layer), and per-layer thresholds on the frontend, with 95% on the
domain logic, 90% on the views and 95% on the components.

What is left uncovered is code whose only untested path needs something a test
cannot provide: signing a real Google token, completing a real Web Push
handshake, exchanging a real Firebase service account, or a worker scope jsdom
does not implement. The logic those wrappers call is tested directly.

### The contracts worth knowing about

Two pieces of logic exist on both sides and must agree exactly. Both are pinned by
tests in both suites, so a drift fails a build instead of corrupting data:

- **Split rounding.** The client shows a preview and stores the amounts offline, so
  it runs the same largest-remainder distribution with the same member-id
  tie-break. `SplitCalculatorTests.The_leftover_minor_unit_goes_to_the_lowest_member_id`
  and its frontend twin pin which member receives a leftover cent.
- **The duplicate fingerprint.** The statement never leaves the device, so
  duplicate detection rests entirely on a hash the browser computes.
  `ExpenseFingerprintTests.A_known_transaction_hashes_to_a_pinned_value` and its
  frontend twin assert the same constant.

## Deploying

A release is a version tag. CI runs both suites and the lint on every push; a tag also builds the two images and pushes them to Docker Hub as
`latest` plus three semver tags, the same release model as the other stack on that
server.

```bash
git tag v1.0.0
git push origin v1.0.0
```

On the host:

```bash
cp docker-compose.example.yml docker-compose.yml
cp .env.example .env              # fill it in; nothing has a default
docker compose pull && docker compose up -d
```

`APP_TAG` decides what runs: `latest`, or a pinned `v1.0.0` when a rollback needs
to be exact. The images publish their own health endpoints and the compose file
checks them, so a wedged API is restarted rather than left serving nothing.

Two containers, one hostname: point the reverse proxy at `:8090` for the app and
send `/api` and `/hubs` to `:8091` for the API (`/hubs` is a WebSocket). One
hostname means no cross-origin requests, a first-party refresh cookie, and nothing
to configure in `Cors__AllowedOrigins`.

Backups run in their own container: a gzipped `pg_dump` on start and then daily,
pruned to `BACKUP_RETENTION_DAYS`. Restore with `infra/backup/restore.sh`, which
asks for the dump explicitly and makes you type the database name.

## The mobile shells

The spec settled on Capacitor rather than a pure PWA or two native apps: iOS
suspends background web apps aggressively enough to break reliable sync and make
Web Push unreliable, while two native apps double the maintenance of a
solo-maintained app for no benefit at this scale.

```bash
cd frontend
npm run build
npx cap add android          # or ios
npx cap sync
npx cap open android
```

The native projects are generated, not committed: they are fully derivable from
`capacitor.config.ts` and the built bundle. `.github/workflows/mobile.yml` builds
a debug APK on demand; the iOS job builds unsigned, since an archive for TestFlight
needs signing material that is a decision to make once rather than a default.

Push registration is one function for all three channels (`src/native/push.ts`):
APNs and FCM in a shell, Web Push in a browser, all registering the same shape
with the API.

## How offline actually works

Every write goes to IndexedDB and an outbox, then returns. Nothing waits on the
network.

- The outbox drains in the order the changes were made, so a create is never sent
  after the edit that depends on it.
- A failed push leaves the queue untouched and retries. A change the server will
  never accept is parked rather than retried forever, so it cannot block
  everything behind it.
- A pull never overwrites a local edit that has not been sent yet.
- Each entity carries a vector clock. An incoming revision that causally follows
  the stored one wins; one that is already contained is dropped; two concurrent
  ones become a conflict record holding both versions for a person to resolve.
- Per-group cursors, not the connection, are what guarantee nothing is missed. A
  dropped WebSocket is therefore harmless: SignalR is an optimisation over the
  same delta pull.
- Settled history older than a year is collapsed into a self-contained snapshot
  and trimmed, so the log does not grow without bound. A device further behind
  than the cutoff bootstraps from the snapshot.

## The statement importer, and why it is different

The Settle Up importer runs on the server: it is our own structured export.

A bank statement does not. It is parsed entirely in the browser, in a Web Worker,
with PDF.js for the text layer and Tesseract for scans. The only thing that ever
reaches the API is the list of expense records the user confirmed. There is no
endpoint that accepts a statement file, and the staged parsing data is cleared
when the review session commits or is cancelled, whichever comes first.

Automated table extraction from a PDF is unreliable, so the design assumes it will
be wrong sometimes: every row carries its problems, the review step is mandatory,
and nothing commits until confirmed.
