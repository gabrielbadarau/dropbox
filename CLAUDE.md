# dropbox - project notes

A learning project: a Dropbox-like file storage/sync system, built to learn
.NET 10 / C# and system design fundamentals together.

Reference design: https://www.hellointerview.com/learn/system-design/problem-breakdowns/dropbox
(fetched and read in full before starting; functional/non-functional requirements,
data model, API design, and all three deep dives below are drawn from it, with
deliberate deviations called out explicitly in the Decisions Log.)

Working style: step-by-step, one confirmed step (or labeled sub-part of a step)
at a time, each verified against the real running system before being called
done, committed and pushed individually. See root-level ground rules in the
conversation this file originated from for the full process.

## Step plan

- [x] **Step 1 - Repo & solution scaffolding**
  - [x] Part 1: git init, local git identity, `.gitignore`, public GitHub repo via `gh`
  - [x] Part 2: `.NET` solution skeleton - `Dropbox.Api` (controller-based Web API, net10.0), `/health` endpoint
  - [x] Part 3: `docker-compose.yml` (Postgres 18 + MinIO), `CLAUDE.md` (this file)
- [x] **Step 2 - Data model & database**
  - [x] Part 1: EF Core + Npgsql wiring, connection string via User Secrets, `/health` extended to check real DB connectivity
  - [x] Part 2: Normalized entity schema (`User`, `FileMetadata`, `Chunk`, `SharedFile`), initial migration applied to dockerized Postgres
- [ ] **Step 3 - Auth basics**
  - Minimal JWT-based current-user concept, just enough for ownership/sharing to mean something
- [ ] **Step 4 - Upload flow (small files)**
  - Presigned-URL pattern against MinIO, status transition uploading -> uploaded via MinIO event notification
- [ ] **Step 5 - Download flow**
  - Presigned GET from MinIO
- [ ] **Step 6 - Large file support (deep dive: chunked multipart upload)**
  - Client-side chunking, fingerprinting, resumability, S3-style multipart upload against MinIO
- [ ] **Step 7 - File sharing**
  - `SharedFiles` join, share/list-shared-with-me endpoints
- [ ] **Step 8 - Sync (change feed + real-time push)**
  - `GET /files/changes?since=` polling fallback, SignalR for push
- [ ] **Step 9 - Security deep dive**
  - HTTPS end to end, presigned URL expiry review, encryption-at-rest note
- [ ] **Step 10 - Performance deep dive (stretch)**
  - Compression, content-defined chunking / delta sync discussion (implementation depth TBD)
- [ ] **Step 11 - Minimal React web client**
  - Once core backend flows are solid; visualizes upload/download/share/sync end to end

Status legend: unchecked = not started. This list is updated as we go; if a
step's scope changes mid-flight, that's a Decisions Log entry, not a silent edit.

## Decisions log

Each entry: what we chose, why, and (if applicable) what we reversed and why.

1. **Reference spec fetched live, not recalled from memory** - read in full
   (functional/non-functional requirements, data model, API design, all 3 deep
   dives) before any planning, per explicit instruction to ground the project
   in the actual document rather than either party's memory of it.

2. **MinIO instead of S3, Postgres instead of DynamoDB, no CDN layer.**
   Why: zero-cost, local-only constraint rules out real AWS services. MinIO is
   S3-API-compatible (presigned URLs, multipart upload, event notifications),
   so it preserves the *mechanics* of the reference design rather than faking
   them. Postgres was picked over local DynamoDB because it's more idiomatic
   with EF Core / .NET generally, and a relational store is a better vehicle
   for learning schema design tradeoffs. No CDN substitute: there's no
   meaningful "edge" on one laptop, so downloads go directly from MinIO
   presigned URLs. We talk through what a CDN would add when we hit that deep
   dive, instead of pretending a single Docker container is one.

3. **Normalized relational schema, not the spec's nested/denormarlized
   FileMetadata+chunks document.** The reference data model nests a chunks
   array inside FileMetadata - natural for DynamoDB. Since we're on
   Postgres/EF Core, chunks become their own table with a foreign key back to
   the file. Direct consequence of decision #2, not an independent choice.

4. **SignalR for the real-time push side of sync**, rather than a generic
   WebSocket/SSE implementation. This *is* the .NET-idiomatic way to do what
   the spec asks for, so not really a deviation - just the concrete choice.

5. **50GB max file size stays the NFR / design target**, but we will not
   literally test at that scale locally. Real verification will happen at a
   few hundred MB. Also see Known Limitations.

6. **Client scope decided incrementally, not up front.** Backend-first:
   every step through Step 8 (sync) is verified with curl / psql / mc, no UI.
   A minimal React web client comes later (Step 11) once core flows are
   solid, rather than a full desktop sync client (FileSystemWatcher-based)
   matching the spec's literal "Uploader Client." Also see Known Limitations.

7. **Repo-local git identity**, separate from the machine's global (work)
   config: `Gabriel Badarau <badaraugabriel95@gmail.com>`, set via
   `git config --local` in this repo only. Global `~/.gitconfig` untouched.

8. **Public GitHub repo**, created via `gh repo create --source=. --remote=origin`.
   `gh` was already authenticated over HTTPS as `gabrielbadarau`, so no SSH
   key mismatch to work around.

9. **Controllers, not Minimal APIs, for the Web API project** - despite
   `.NET`'s newer templates leaning toward minimal APIs by default. Reasoning:
   (a) our endpoint surface groups naturally into a handful of resource
   controllers (Files, Chunks, Shares) rather than a flat list of routes, and
   (b) controllers are the more common pattern in real-world enterprise .NET
   codebases, which has learning value beyond this project. Kept OpenAPI
   (Swagger) generation enabled - free, and useful to eyeball the API as it
   grows.

10. **Removed the scaffolded `WeatherForecastController`/`WeatherForecast.cs`
    sample immediately** rather than leaving dead sample code in the repo.

11. **No test project for now**, reversing the original Step 1 Part 2 plan
    (which included an xUnit project with a `WebApplicationFactory`-based
    `/health` integration test). Decided against it directly. See Known
    Limitations for the revisit trigger.

12. **Pinned a direct `Microsoft.OpenApi` reference to `2.11.0`**, overriding
    a vulnerable transitive `2.0.0` pulled in by `Microsoft.AspNetCore.OpenApi
    10.0.10`. This was a real high-severity DoS advisory
    ([GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)),
    not a false positive - confirmed via `dotnet restore` warning, checked
    NuGet for the patched version line (2.7.5+), pinned within the same major
    version to avoid an unnecessary breaking 3.x jump.

13. **.NET 10 scaffolds `.slnx` (new XML solution format) instead of the
    classic `.sln` by default.** Not a deliberate choice - just documenting
    the tooling reality since it surprised both of us. Works identically with
    `dotnet sln add`/`build`/etc.

14. **Postgres 18 Docker image requires the volume mounted at
    `/var/lib/postgresql`, not `/var/lib/postgresql/data`** (their 18+ images
    changed to a `pg_ctlcluster`-style, version-namespaced data directory
    layout). Hit this as a real crash loop on first `docker compose up`,
    diagnosed from container logs, fixed, verified healthy + a live `psql`
    connection afterward. Documented so the mistake isn't repeated if this
    compose file is ever copied elsewhere.

15. **MinIO pinned to a specific dated release tag**
    (`RELEASE.2025-09-07T16-13-09Z`), not `latest` - reproducibility; `latest`
    can silently change under us on a future `docker compose pull`.

16. **Local dev credentials live in a gitignored `.env`**, with `.env.example`
    committed as the template. Never hardcoded directly into
    `docker-compose.yml`.

17. **`Dropbox.Api`'s Postgres connection string lives in .NET User Secrets**,
    not `appsettings.json` and not `.env`. Unlike a gitignored file, User
    Secrets store the value entirely outside the repo directory
    (`~/.microsoft/usersecrets/<id>/secrets.json`), so there's no file in
    this repo that could ever leak it, structurally. The `<UserSecretsId>`
    GUID in `Dropbox.Api.csproj` is just a pointer, safe to commit.

18. **`dotnet-ef` installed as a repo-local pinned tool** (`dotnet-tools.json`),
    not a global tool - same reproducibility reasoning as pinning NuGet
    packages and the MinIO image tag.

19. **`User` deliberately excludes auth fields (e.g. password hash) in Step 2.**
    The schema needed a real FK target for file ownership/sharing now, but
    guessing at auth-specific columns before Step 3 decides the actual auth
    mechanism would be speculative. Step 3 adds whatever fields it needs via
    a new migration - normal incremental schema evolution, not a redo.

20. **Chunk.Index added beyond the reference spec's literal chunk fields**
    (which only lists id/status/eTag). Needed for any usable chunk
    reassembly/ordering ahead of Step 6's multipart upload deep dive - a
    deliberate, minimal addition, not scope creep.

21. **Entity IDs are client-generated GUIDs** (`Guid.NewGuid()` as a property
    default), not server-generated (e.g. Postgres `gen_random_uuid()`).
    Simpler - usable immediately after `new()`, no DB round-trip needed to
    get an ID back. Known tradeoff not worth solving at this scale: random
    (v4) GUIDs fragment B-tree index locality on insert at high volume;
    sequential/UUIDv7-style IDs are the real-world fix, irrelevant here.

## Known limitations

Deliberate, documented gaps - not oversights.

- **No automated tests.** Trigger to revisit: once there's real business
  logic worth protecting with regression tests (e.g. chunk fingerprint
  verification, sync conflict resolution) - not just for its own sake.
- **No CDN.** Permanent scope decision for a local-only project, not a gap
  we intend to close. Discussed as a tradeoff when we build the download flow.
- **50GB file size is a design target, not a tested one.** Real verification
  happens at a few hundred MB locally. Don't mistake "the design supports it"
  for "we've proven it at that scale."
- **No desktop/background sync client (FileSystemWatcher-based) yet.**
  Backend-first; a minimal React web client is planned for Step 11. Revisit
  trigger: if we want to more faithfully replicate the spec's "Uploader
  Client" concept for the sync deep dive.
- **Explicitly out of scope, per the reference spec itself:** file editing,
  in-browser file preview, file versioning, per-user storage quotas,
  virus/malware scanning.
- **`Failed to determine the https port for redirect` warning** when running
  `Dropbox.Api` via the `http`-only launch profile. Not a bug - artifact of
  that profile having no HTTPS binding, used deliberately for fast curl-based
  smoke tests. Will be addressed properly once we decide on a TLS termination
  strategy for Docker (Step 9).

## Local environment

- `.NET 10` SDK (`10.0.302` at time of writing)
- Docker via Rancher Desktop
- `docker compose up -d` starts Postgres (`localhost:5432`) and MinIO
  (S3 API on `localhost:9000`, console on `localhost:9001`)
- Copy `.env.example` to `.env` before running docker compose (gitignored,
  local dev credentials only)
