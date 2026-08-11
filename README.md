# dropbox

A learning project: a Dropbox-like file storage and sync system, built from
scratch to learn .NET 10 / C# and system design fundamentals together. It
follows the reference design at
[hellointerview.com's Dropbox breakdown](https://www.hellointerview.com/learn/system-design/problem-breakdowns/dropbox)
step by step, with deviations made deliberately and logged as they happen
rather than silently.

See [CLAUDE.md](./CLAUDE.md) for the full step-by-step build log: the step
plan, a numbered decisions log explaining *why* behind every non-obvious
choice, and a known-limitations list of gaps that were left in on purpose.

## What it does

- **Accounts** - register/login with a hashed password, JWT-based auth.
- **Upload** - drag-and-drop or file picker, with a real byte-accurate
  progress bar. Small files go through a single presigned `PUT`; large files
  (>8MB) are split into chunks and uploaded via multipart upload, resumable
  by content fingerprint if interrupted.
- **Download** - presigned, time-limited URLs straight from object storage.
- **Sharing** - share a file with one or more people by email; see what
  others have shared with you.
- **Delete** - owner-only, cleans up both the database row and the
  underlying stored object (or aborts an in-progress multipart upload).
- **Live sync** - an append-only change feed per user, exposed both as a
  polling endpoint and as real-time push over WebSockets, so a file list
  updates the moment something changes, without a manual refresh.

## What we leveraged

**Backend**
- .NET 10 / ASP.NET Core, Controllers (not Minimal APIs)
- EF Core + Npgsql, against PostgreSQL 18
- ASP.NET Core Identity's `PasswordHasher<T>` for password hashing (without
  the rest of the Identity framework)
- JWT Bearer authentication
- `AWSSDK.S3` (the real AWS SDK) pointed at MinIO, an S3-API-compatible
  object store - presigned URLs, multipart upload, and bucket event
  notifications all work exactly as they would against real S3
- SignalR for the real-time push side of sync
- Swagger/OpenAPI for interactive API docs

**Frontend**
- React + TypeScript, scaffolded with Vite
- Tailwind CSS v4 (CSS-first `@theme`, no config file)
- React Router, Axios
- `@microsoft/signalr` for the live-sync client
- `XMLHttpRequest` for real upload progress (`fetch` has no equivalent to
  `xhr.upload.onprogress`), and the Web Crypto API for client-side SHA-256
  file fingerprinting

**Infrastructure**
- Docker Compose runs the entire stack - API, Postgres, and MinIO - as one
  `docker compose up -d`, no local .NET/Postgres/MinIO install required
- MinIO webhook notifications tell the API when an upload actually finishes,
  rather than trusting the client's word for it

No AWS, no cloud costs - MinIO's API compatibility means the object-storage
mechanics (presigned URLs, multipart upload, event notifications) are the
real thing, just self-hosted for a zero-cost local project.

## Architecture

![Architecture diagram: a browser talks to Dropbox.Api over REST and SignalR for metadata, auth, and live sync, while uploads and downloads go directly between the browser and MinIO via presigned URLs, bypassing the API entirely. Dropbox.Api talks to PostgreSQL over EF Core for all metadata, and to MinIO server-to-server for presigning, multipart completion, and receiving upload-complete webhooks. The API, Postgres, and MinIO all run inside one Docker Compose network.](./docs/architecture.svg)

The detail worth calling out: **file bytes never pass through the API.**
The browser asks the API for a presigned URL, then uploads or downloads
directly against MinIO. The API's job is metadata, authorization, and
orchestration (who owns what, who it's shared with, whether an upload
actually completed) - not proxying gigabytes of file data through itself.
This is the same shape real Dropbox/S3-backed systems use, and MinIO's S3
compatibility means the mechanics (SigV4-signed URLs, multipart upload,
bucket event notifications) are the genuine article, not a simulation.

## Running it

```bash
cp .env.example .env
docker compose up -d postgres minio        # datastores first

dotnet tool restore                         # installs the pinned dotnet-ef
dotnet ef database update --project src/Dropbox.Api \
  --connection "Host=localhost;Port=5432;Database=dropbox;Username=dropbox;Password=dropbox_dev_password"

docker compose up -d --build api            # now bring up the API
```

The migration step only needs to run once - after that, `docker compose up -d`
alone starts (or restarts) the full stack, since the schema lives in the
`postgres-data` Docker volume. Then, for the web client:

```bash
cp client/.env.example client/.env
npm --prefix client install
npm --prefix client run dev
```

| Service        | URL                              |
| -------------- | --------------------------------- |
| Web client     | http://localhost:5173            |
| API + Swagger  | http://localhost:5261/swagger    |
| MinIO console  | http://localhost:9001            |

## Project layout

```
src/Dropbox.Api/    ASP.NET Core Web API (controllers, EF Core, S3, SignalR)
client/              React + TypeScript web client
docker-compose.yml   Postgres + MinIO + the containerized API
CLAUDE.md            Step plan, decisions log, known limitations
```
