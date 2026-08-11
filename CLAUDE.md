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
- [x] **Step 3 - Auth basics**
  - [x] Part 1: `User.PasswordHash` added via migration
  - [x] Part 2: `POST /auth/register`, `POST /auth/login` - password hashing, JWT issuance
  - [x] Part 3: JWT Bearer middleware enforced, `[Authorize] GET /auth/me`
- [x] **Step 4 - Upload flow (small files)**
  - [x] Part 1: `AWSSDK.S3` wired against MinIO, bucket bootstrap, `/health` extended with S3 connectivity check
  - [x] Part 2: `POST /files/presigned-url` - creates `FileMetadata` (Status=Uploading), returns a presigned PUT URL
  - [x] Part 3: MinIO webhook notification flips Status to Uploaded, with a shared-secret check against forged calls
- [x] **Step 5 - Download flow**
  - `GET /files/{id}/presigned-url` - presigned GET from MinIO, owner-only for now, 409 if not yet uploaded
- [x] **Step 6 - Large file support (deep dive: chunked multipart upload)**
  - [x] Part 1: `POST /files/multipart-upload` - initiate/resume via fingerprint match, `FileMetadata.UploadId`
  - [x] Part 2: `PATCH /files/{id}/chunks/{index}` - trust-but-verify against S3's real `ListParts`
  - [x] Part 3: `POST /files/{id}/complete` - `CompleteMultipartUpload`, Status flips only after S3 confirms
- [x] **Step 7 - File sharing**
  - [x] Part 1: `POST /files/{id}/share` (per-email results), download access extended to shared users
  - [x] Part 2: `GET /files/shared-with-me`
- [x] **Step 8 - Sync (change feed + real-time push)**
  - [x] Part 1: `ChangeEvent` append-only log, `DELETE /files/{id}`, wired into every mutation point
  - [x] Part 2: `GET /files/changes?since=` polling endpoint
  - [x] Part 3: SignalR `ChangesHub` for real-time push, alongside polling not replacing it
- [ ] **Step 9 - Security deep dive**
  - HTTPS end to end, presigned URL expiry review, encryption-at-rest note
- [ ] **Step 10 - Performance deep dive (stretch)**
  - Compression, content-defined chunking / delta sync discussion (implementation depth TBD)
- [x] **Step 11 - Minimal React web client** (built out of order, ahead of
      Steps 9-10 - see decision #47)
  - [x] Part A: Containerize `Dropbox.Api`, consolidate into one `docker-compose.yml`
  - [x] Part B: React client scaffold (Vite+React+TS+Tailwind v4, auth, routing)
  - [x] Part C: `GET /files/mine`, real file browser UI (list/download/delete)
  - [x] Part D: Upload with real progress (single-PUT and chunked multipart)
  - [x] Part E: Share dialog
  - [x] Part F: Live sync via SignalR in the UI
  - [x] Part G: Final visual polish pass

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

22. **Passwords hashed with the built-in `PasswordHasher<T>`**
    (`Microsoft.AspNetCore.Identity`), not the full ASP.NET Core Identity
    system (`UserManager`/`SignInManager`/`IdentityDbContext`). It's part of
    the shared framework already referenced by `Microsoft.NET.Sdk.Web` - no
    extra package needed, confirmed by a clean build. Gets real, current
    password hashing without the heavier Identity machinery we don't need.

23. **No refresh tokens.** Access tokens are short-lived (60 min) JWTs only.
    Refresh tokens add real complexity (rotation, revocation, storage) that
    doesn't serve "just enough for ownership/sharing to mean something" -
    deliberate scope cut, not an oversight. See Known Limitations.

24. **`options.MapInboundClaims = false` on the JWT Bearer handler.** Real
    bug, not a style choice: `JwtSecurityTokenHandler` silently remaps short
    claim names (`sub`, `email`) to legacy `ClaimTypes.*` URIs by default
    when building the `ClaimsPrincipal`. First working version of
    `GET /auth/me` returned `200` with `userId`/`email` both `null` - auth
    succeeded, claim lookup silently failed. Diagnosed and fixed rather than
    working around it with `ClaimTypes.NameIdentifier` lookups, since we
    control both the issuer and the consumer of these tokens and there's no
    reason to want the remapping.

25. **`AWSSDK.S3` (the real AWS SDK), not a MinIO-specific client**, pointed
    at MinIO's endpoint with `ForcePathStyle = true`. Direct consequence of
    the Step 1 decision to keep real S3 mechanics via MinIO's API
    compatibility, not an independent choice.

26. **Object storage key = `FileMetadata.Id`**, not derived from the
    user-supplied filename. Opaque, collision-proof by construction (it's
    already a unique GUID), and keeps the human-readable name entirely out
    of the storage layer - it only ever lives in Postgres.

27. **`StorageOptions.FixPresignedUrlScheme` works around a confirmed
    `AWSSDK.S3` v4 bug**: `GetPreSignedURLAsync` always returns an `https://`
    URL, even with `AmazonS3Config.UseHttp = true` and a `http://`
    `ServiceURL` - verified by inspecting `Config` at runtime and seeing both
    values were correct, meaning the SDK's presigned-URL builder itself
    ignores them. Safe to rewrite the scheme after the fact because SigV4
    presigned URLs only sign the `Host` header (`X-Amz-SignedHeaders=host`),
    not the scheme - confirmed by a real `PUT` succeeding against the
    rewritten URL.

28. **MinIO's webhook target is configured server-side via docker-compose
    env vars, but the bucket's event *subscription* to it is done in app
    startup code** (`PutBucketNotificationAsync`, idempotent, same pattern as
    the bucket bootstrap), not via a manual `mc event add` step. Nothing
    about this setup depends on a command someone has to remember to rerun.

29. **The storage webhook endpoint is not `[Authorize]`** - MinIO doesn't
    present a JWT, it presents its own configured auth token. Checked
    manually against `Storage:WebhookSecret`. This is a correctness
    requirement, not speculative hardening: without it, anyone who can reach
    the API could spoof an "upload complete" notification without uploading
    real bytes, making the `Status` field meaningless. Verified directly -
    a forged call with no/wrong auth is rejected and leaves the target file's
    status unchanged.

30. **Removed an `extra_hosts: host.docker.internal:host-gateway` override**
    added to the `minio` service under the assumption it would make
    `host.docker.internal` resolution more portable across Docker setups. It
    actually broke delivery on this Rancher Desktop setup - MinIO's own logs
    showed it dialing a raw IP (`172.17.0.1`) and getting connection refused,
    overriding Rancher Desktop's own working built-in resolution. Removed
    entirely rather than chasing the "more correct" version of the override.

31. **MinIO sends its configured webhook auth token as `Bearer <token>`** in
    the `Authorization` header, not the raw token value. Confirmed via
    temporary runtime debug logging of the actual received header, not
    assumed from memory of how the feature "should" work.

32. **Download authorization returns `404` for both "file does not exist"
    and "file exists but belongs to someone else."** Deliberate - returning
    a distinct "403 Forbidden" would confirm to a non-owner that a given
    file ID is real, leaking information the API has no reason to expose.

33. **Separate expiry windows for upload vs. download presigned URLs**
    (`PresignedUploadUrlExpiryMinutes` = 15, `PresignedDownloadUrlExpiryMinutes`
    = 5), not one shared value. Follows the reference spec's own security
    guidance directly: presigned URLs are bearer tokens usable by anyone
    holding them until expiry, and downloads are meant to be consumed
    immediately after being requested, so there's no reason to give a
    download URL the same generous window an upload needs.

34. **Resumability via fingerprint, not full content-addressable
    deduplication.** The reference spec's fingerprinting section mentions
    both. Built: re-initiating a multipart upload with the same fingerprint
    resumes the same in-progress upload, skipping already-uploaded chunks.
    Not built: recognizing that a *fully completed* upload with this
    fingerprint already exists (by anyone) and skipping the upload
    entirely. That would require changing the storage key scheme from a
    random GUID (decision #26) to a content hash - a bigger architectural
    change than this step needs. Deliberate scope cut, not an oversight.

35. **Two different mechanisms flip `FileMetadata.Status` to `Uploaded`,
    for two different reasons.** The Step 4 small-file flow relies on
    MinIO's webhook, because the client's direct `PUT` to storage has no
    synchronous hook back into our backend. The Step 6 multipart flow does
    not use the webhook at all - `POST /files/{id}/complete` calls S3's
    `CompleteMultipartUpload` *from our own backend*, giving a direct,
    synchronous answer, so `Status` is set in that same request. (Also
    technically true because MinIO fires a distinct event name,
    `s3:ObjectCreated:CompleteMultipartUpload`, which our bucket
    notification config never subscribed to - it only subscribes to
    `s3:ObjectCreated:Put`.)

36. **`PATCH /files/{id}/chunks/{index}` verifies every reported chunk
    against S3's real `ListParts` response** before trusting it - not just
    "does a part exist for this number," but an exact `ETag` match.
    Confirmed this is real verification, not decorative, by reporting a
    chunk that was never actually uploaded (rejected) and reporting a real
    uploaded chunk with a deliberately wrong `ETag` (also rejected).

37. **`POST /files/{id}/complete` is idempotent** - calling it again after
    the upload already completed returns `200` without re-calling S3,
    since a multipart upload's `UploadId` becomes invalid once completed
    and a second real completion call would fail.

38. **`POST /files/{id}/share` reports per-email results rather than
    succeeding or failing as a whole.** Sharing with three people where
    one email has no account is a realistic case, not an edge case - the
    caller needs to know *which* emails worked, not get one opaque
    failure for the whole request. Sharing with the owner's own email,
    and re-sharing with someone already shared, are both reported as
    named outcomes rather than silently ignored or erroring.

39. **`GET /files/{id}/presigned-url`'s access check now allows the owner
    or anyone with a `SharedFiles` row** - this was flagged as planned
    back in Step 5 (decision #32), not a new decision made here. Fixed a
    real nullable-reference compiler warning (`CS8602`) this introduced by
    restructuring the null check and the access check into two separate
    steps, rather than suppressing the warning.

40. **`ChangeEvent`: a dedicated append-only event log, not inferred from
    existing timestamps** (`FileMetadata.UpdatedAt`, `SharedFile.SharedAt`).
    The timestamp-inference approach breaks down for deletion specifically -
    with a real hard delete, the row a sync client would need to see "this
    was deleted" is exactly the row that is now gone. An explicit journal,
    written to at the moment of each mutation, sidesteps this entirely and
    is genuinely how real delta-sync systems (Dropbox included) work.
    Deviates from the reference spec's 3 generic change types
    (created/updated/deleted) in favor of 4 specific ones
    (Created/Uploaded/Shared/Deleted) matching our actual mutation points -
    a shared file is a distinct kind of event from an upload finishing, not
    a generic "update." One row is written per *affected user*, not per
    file mutation - a file shared with two people produces two rows.

41. **`ChangeEvent.FileId` is deliberately not a foreign key to
    `FileMetadata`, but `ChangeEvent.UserId` is a real FK with cascade.**
    Asymmetric on purpose: a `Deleted` event must remain readable after the
    file row it describes is gone (no FK, plus a denormalized `FileName`
    snapshot since there is nowhere left to join to), whereas it is correct
    for a user's events to disappear if the user themselves is deleted (a
    real FK is the right tool there).

42. **`DELETE /files/{id}` was added in Step 8, not planned as its own
    step.** It became necessary infrastructure rather than a scope
    decision on its own: the `Deleted` change type has no real event
    source without something to delete. Owner-only, best-effort S3
    cleanup (`AbortMultipartUploadAsync` for a still-in-progress multipart
    upload, `DeleteObjectAsync` otherwise - safe even when no object was
    ever actually uploaded, since S3 `DELETE` is idempotent) that
    prioritizes the database row actually disappearing over a perfectly
    clean bucket; a storage-cleanup failure is swallowed rather than
    blocking the delete. See Known Limitations.

43. **`ChangeEventRecorder` is a two-step API: `Record()` then
    `PublishPendingAsync()`, called separately by the caller.** `Record()`
    only stages a row (no `SaveChangesAsync` of its own), so it commits
    atomically with whatever mutation triggered it. `PublishPendingAsync()`
    is called only after the caller's own `SaveChangesAsync` has already
    succeeded, so a SignalR client is never pushed a notification for a
    change that failed to actually persist.

44. **SignalR JWT auth reads the token from an `access_token` query
    parameter, but only for requests to the hub path specifically** - the
    documented ASP.NET Core pattern, necessary because browsers/WebSocket
    clients cannot always set a custom `Authorization` header on the
    connection the way an ordinary HTTP API call can.

45. **SignalR's Hub Protocol needed its own separate
    `AddJsonProtocol(...).PayloadSerializerOptions` enum-as-string
    configuration** - confirmed as a real, distinct bug, not an assumption:
    a live push arrived with `"type":0` instead of `"Created"` before this
    was added, proving `AddControllers().AddJsonOptions(...)`'s converter
    (decision for Step 8 Part 2) does not apply to SignalR at all; the two
    serialize independently.

46. **Verification for Part 3 used a throwaway `.cs` file-based app
    (`dotnet run script.cs`), not a project added to the solution** -
    consistent with the Step 1 decision against a permanent test project.
    Two real bugs surfaced during that verification were in the throwaway
    script's own JSON handling (file-based apps default to
    reflection-disabled JSON), not in `Dropbox.Api` - confirmed by verbose
    client-side logging showing a push genuinely arriving over the
    WebSocket before failing to deserialize, which is what justified
    fixing the script rather than suspecting the server.

47. **Step 11 (React web client) built ahead of Steps 9-10, on explicit
    request** - "let's go and build a minimal react web client where we can
    test this... I guess you can also link to docker, so that we run one
    big docker compose." A deliberate reordering, logged here rather than a
    silent skip: Steps 9 (security deep dive) and 10 (performance deep dive)
    remain planned, not abandoned - see the step plan checkboxes above.

48. **`Dropbox.Api` containerized and folded into the same `docker-compose.yml`**
    as Postgres/MinIO, rather than left as a host-run process. Multi-stage
    `Dockerfile` (SDK image to build/publish, smaller ASP.NET runtime image
    to run), `HEALTHCHECK` against the existing `/health` endpoint. One
    `docker compose up -d` now starts the entire stack. `JWT_SIGNING_KEY`
    moved from .NET User Secrets-only into `.env` as well (decision #17's
    reasoning still holds for the host-run dev path; the containerized path
    has no User Secrets store to read from, so it needs the value some other
    way, and `.env` is already the established pattern for local secrets -
    decision #16).

49. **A second, differently-configured `IAmazonS3` client added via keyed DI**
    (`AddKeyedSingleton<IAmazonS3>("public", ...)`), used only for the three
    presigned-URL-generating call sites. Real bug forced this, not a
    preference: presigned URLs generated with the API's internal
    `http://minio:9000` container-network address are meaningless to a
    browser on the host, which can only reach `http://localhost:9000`.
    Tried the cheap fix first (string-swap the hostname after signing, same
    trick as decision #27's scheme workaround) - it does NOT work, and
    produces a real `SignatureDoesNotMatch`, because SigV4's
    `X-Amz-SignedHeaders=host` means the `Host` header value itself is part
    of what's cryptographically signed, unlike the scheme. Fixed properly
    with a distinct `StorageOptions.PublicServiceUrl` and a second client
    scoped to just those three call sites; the original client (internal
    `http://minio:9000`) is still what the API uses for every direct S3
    operation (`HeadObject`, `CompleteMultipartUpload`, etc.), since those
    calls happen server-to-server and never leave the Docker network.

50. **CORS added to `Dropbox.Api`** (`Cors:AllowedOrigins` config,
    `AddCors`/`UseCors`), required the moment a browser-based client on a
    different origin (`localhost:5173`) exists at all - every prior client
    was `curl`, which does not enforce CORS. Initially added *without*
    `AllowCredentials()`; this was a real, confirmed bug, not caution -
    the SignalR JS client's negotiate request is sent with
    `credentials: include`, and the browser rejected the preflight outright
    with `Access-Control-Allow-Credentials` missing, breaking Part F's live
    push entirely until fixed. `AllowCredentials()` requires explicit
    `WithOrigins` (already the case) rather than a wildcard origin.

51. **MinIO needs zero explicit CORS configuration for the client's direct
    presigned-URL `PUT`/`GET` calls.** Tried adding a `PutCORSConfigurationAsync`
    bucket-CORS bootstrap first, assuming MinIO would need it like real S3 -
    this crashed the API on every startup (`AmazonS3Exception: ... not
    implemented`), confirmed independently via `mc cors set` hitting the same
    error. A direct curl `OPTIONS` preflight against a real object key from
    the actual client origin proved MinIO already answers correctly with no
    configuration. Removed the bootstrap entirely.

52. **React client stack: Vite + React + TypeScript + Tailwind CSS v4 (CSS-first
    `@theme`, no `tailwind.config.js`) + React Router + Axios.** Chosen for
    being the current idiomatic, low-ceremony combination for a small SPA -
    no meta-framework (Next.js etc.) needed for a client this size with no
    server-rendering requirement.

53. **JWT stored in `localStorage`, not an httpOnly cookie or in-memory only.**
    Simplest option that survives a page refresh without a silent-refresh
    flow to fall back on - and there's no refresh token to silently redeem
    anyway (decision #23). Known XSS-exposure tradeoff of `localStorage`
    accepted at this project's scale; see Known Limitations.

54. **Upload progress uses `XMLHttpRequest`, not `fetch`.** Concrete
    technical reason, not a style preference: `fetch`'s request body has no
    upload progress event equivalent to `XMLHttpRequest.upload.onprogress` -
    without it, showing a real percentage-based progress bar (the user's
    explicit ask) is not possible for the `PUT` calls to presigned URLs.

55. **Client picks single-PUT vs. chunked multipart upload by the same 8MB
    threshold the chunk size itself uses**, mirroring the backend's existing
    Step 4 vs. Step 6 split. Large-file chunks upload sequentially, not in
    parallel - deliberate: aggregate progress is then just "bytes sent so
    far / total," rather than reconciling several concurrent `XMLHttpRequest`
    progress streams into one number. Parallel chunk upload would be faster
    but was cut as unnecessary complexity for a local learning project: see
    Known Limitations.

56. **Client-side SHA-256 fingerprinting via the Web Crypto API**
    (`crypto.subtle.digest`), reading the whole file into memory first via
    `file.arrayBuffer()`. Matches the backend's existing fingerprint-based
    resumability (decision #34) - a resumed upload only skips chunks if the
    client can prove it's resuming the *same* file, not just a same-sized one.

57. **`GET /files/mine` added as new backend surface for Part C**, not
    originally planned in Step 7 (whose scope was specifically sharing) -
    closes the "no way to list your own files" gap called out in Known
    Limitations since Step 7. Owner-only by construction (filters on the
    caller's own ID from the JWT), same pattern as every other
    per-user query in the API.

58. **`ShareDialog` and `ConfirmDialog` are custom components, not the
    browser's native `prompt()`/`confirm()`.** Originally used `confirm()`
    for delete; discovered during testing that it gets silently
    auto-dismissed with zero network request in the automated browser tool
    used for verification (confirmed via console log:
    `Page dialog suppressed (confirm): ... confirm() returned false`).
    Replaced rather than worked around, since a styled, in-DOM dialog is a
    strict improvement anyway - consistent with the rest of the UI, and
    actually testable going forward.

59. **`useChangesHub` React hook wraps the `@microsoft/signalr` client**,
    connecting to the same `ChangesHub` built in Step 8 Part 3.
    `accessTokenFactory` reads the JWT out of the same `localStorage` key the
    REST API calls use (decision #53), so the hub authenticates the same way
    every other request does. Deliberately fails silently on connection
    error (`.catch(() => {})`) and auto-reconnects
    (`.withAutomaticReconnect()`) - real-time push is additive on top of the
    existing `GET /files/changes` polling endpoint from Step 8 Part 2, so a
    hub that never connects should degrade to "just polling," not break the
    app.

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
- **No refresh tokens.** 60 minute access tokens only; once expired, the
  client just has to log in again. Fine for curl-based local testing.
- **Registration has a benign race condition.** `POST /auth/register` checks
  for an existing email before inserting, but two concurrent registrations
  for the same email could both pass that check before either inserts. The
  database's unique index on `Email` (Step 2) is still the real safety net -
  the second insert would fail - but the caller would get an unhandled
  `DbUpdateException` instead of a clean `409`. Not fixed: very low odds at
  this project's scale, not worth the extra handling yet.
- **No reconciliation between declared and actual upload size.**
  `POST /files/presigned-url` accepts a client-declared `Size`, but nothing
  checks that the object actually uploaded to MinIO matches it (e.g. via a
  `HeadObject` call in the webhook handler). A client could declare one size
  and upload different bytes. Trigger to revisit: before this data is used
  for anything that assumes it's trustworthy (quotas, integrity checks).
- **Webhook secret comparison is a plain string `==`, not constant-time.**
  Theoretical timing side-channel, not a practical concern on a local single-
  user project. Trigger to revisit: Step 9 security deep dive.
- **`FileMetadata.Fingerprint` now used for resumability (Step 6)**, but not
  for deduplication - see decision #34.
- **No cleanup for abandoned multipart uploads.** If a client initiates a
  multipart upload and never finishes or resumes it, the `FileMetadata`
  row, `Chunk` rows, and the underlying S3/MinIO multipart upload (and any
  parts already sent) all sit around indefinitely. Real S3 has bucket
  lifecycle rules for auto-aborting stale multipart uploads; nothing
  equivalent is configured here. Trigger to revisit: if disk usage from
  abandoned uploads ever becomes a real problem at this project's scale.
- **No unshare/revoke endpoint.** A `SharedFile` row, once created, can
  only be removed directly in the database. Not in the original Step 7
  scope; straightforward to add later (`DELETE /files/{id}/share/{userId}`)
  when actually needed.
- **No "list my own files" endpoint.** We've only ever interacted with
  files by the exact ID returned at creation/initiation time - there is
  no way to ask "what files do I own" through the API. Not in the
  original Step 7 scope (which was specifically about sharing); a real
  gap once a client (Step 11) needs to render something like a file
  browser.
- **No pagination on `GET /files/changes?since=`.** Returns every matching
  row unbounded. Fine at this project's scale; a real gap if the event log
  grows large or `since` is omitted on an account with a long history.
- **No retention/cleanup policy on `ChangeEvents`.** The table grows
  forever - every mutation adds rows that are never pruned. Trigger to
  revisit: if disk usage becomes a real concern, or when old events are
  clearly no longer useful to any client.
- **SignalR has no backplane (e.g. Redis) configured.** Group membership is
  tracked in-memory on a single server instance, which is exactly right
  for this project's scope (one process, local-only) but would not
  broadcast correctly across multiple server instances in a real
  horizontally-scaled deployment.
- **No documented client reconnection/catch-up pattern.** The React client's
  `useChangesHub` (Step 11 Part F) auto-reconnects the SignalR connection
  itself, but does not call `GET /files/changes?since=` on reconnect to
  backfill whatever was missed while disconnected - it relies on the
  initial full `refresh()` on page load plus whatever live events arrive
  after reconnecting. A real gap if a client is offline for a while and
  then reconnects without a full page reload.
- **React client's JWT lives in `localStorage`, with no refresh token
  (decision #53, inherited from #23).** Session ends after 60 minutes with
  no silent renewal - the user is bounced to `/login` on the next 401.
  `localStorage` is also readable by any script on the page, a real XSS
  exposure surface accepted at this project's scale; an httpOnly cookie
  would close it but needs CSRF handling in exchange. Trigger to revisit:
  if this client ever needs to be more than a local learning-project demo.
- **Large-file chunk upload is sequential, not parallel** (decision #55).
  Correct and simple, but slower than it could be for very large files on
  a fast connection - each chunk waits for the previous one's `PUT` and
  `PATCH` chunk-report round trip before starting. Trigger to revisit: if
  upload speed on large files actually becomes annoying at this project's
  local-network scale.
- **No client-side retry on a failed upload chunk.** An aborted or
  network-failed `PUT` fails the whole upload; the user has to re-initiate,
  which resumes via fingerprint matching (decision #34) rather than
  restarting from zero, but there's no automatic in-flight retry.

## Local environment

- `.NET 10` SDK (`10.0.302` at time of writing)
- Docker via Rancher Desktop
- `docker compose up -d` starts Postgres (`localhost:5432`) and MinIO
  (S3 API on `localhost:9000`, console on `localhost:9001`)
- Copy `.env.example` to `.env` before running docker compose (gitignored,
  local dev credentials only)
