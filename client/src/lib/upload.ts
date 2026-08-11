import { api } from "./api";
import type { MultipartUploadResponse, PresignedUrlResponse } from "./types";

// Files at or below this size use the single-PUT flow (Step 4). Larger
// files use chunked multipart upload (Step 6), matching the same 8MB
// chunk size - both must stay >= 5MB per S3/MinIO's real rule that only
// the last part of a multipart upload may be smaller.
const CHUNK_SIZE = 8 * 1024 * 1024;

export interface UploadHandle {
  promise: Promise<void>;
  abort: () => void;
}

function putWithProgress(
  url: string,
  body: Blob,
  onLoaded: (loaded: number) => void,
  signal: AbortSignal,
): Promise<string> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("PUT", url);
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onLoaded(e.loaded);
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(xhr.getResponseHeader("ETag") ?? "");
      } else {
        reject(new Error(`Upload failed with status ${xhr.status}`));
      }
    };
    xhr.onerror = () => reject(new Error("Network error during upload"));
    xhr.onabort = () => reject(new DOMException("Aborted", "AbortError"));
    signal.addEventListener("abort", () => xhr.abort());
    xhr.send(body);
  });
}

async function sha256Hex(file: File): Promise<string> {
  const buffer = await file.arrayBuffer();
  const digest = await crypto.subtle.digest("SHA-256", buffer);
  return [...new Uint8Array(digest)]
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

async function uploadSmallFile(
  file: File,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<void> {
  const { data } = await api.post<PresignedUrlResponse>("/files/presigned-url", {
    name: file.name,
    size: file.size,
    mimeType: file.type || null,
  });

  await putWithProgress(
    data.uploadUrl,
    file,
    (loaded) => onProgress(loaded / file.size),
    signal,
  );
  onProgress(1);
}

async function uploadLargeFile(
  file: File,
  onProgress: (fraction: number) => void,
  signal: AbortSignal,
): Promise<void> {
  const fingerprint = await sha256Hex(file);
  const chunkCount = Math.ceil(file.size / CHUNK_SIZE);

  const { data } = await api.post<MultipartUploadResponse>(
    "/files/multipart-upload",
    { name: file.name, size: file.size, mimeType: file.type || null, fingerprint, chunkCount },
  );

  // Sequential, not parallel: keeps aggregate progress trivial to compute
  // correctly (sum of bytes actually sent so far) rather than reconciling
  // several concurrent XHR progress streams into one percentage.
  let bytesDone = 0;
  for (const part of data.parts) {
    const start = (part.partNumber - 1) * CHUNK_SIZE;
    const end = Math.min(start + CHUNK_SIZE, file.size);
    const chunkSize = end - start;

    if (part.alreadyUploaded) {
      bytesDone += chunkSize;
      onProgress(bytesDone / file.size);
      continue;
    }

    const blob = file.slice(start, end);
    const partBytesDoneBase = bytesDone;
    const etag = await putWithProgress(
      part.url!,
      blob,
      (loaded) => onProgress((partBytesDoneBase + loaded) / file.size),
      signal,
    );

    await api.patch(`/files/${data.fileId}/chunks/${part.partNumber}`, { eTag: etag });

    bytesDone += chunkSize;
    onProgress(bytesDone / file.size);
  }

  await api.post(`/files/${data.fileId}/complete`);
}

export function uploadFile(
  file: File,
  onProgress: (fraction: number) => void,
): UploadHandle {
  const controller = new AbortController();
  const run = file.size > CHUNK_SIZE ? uploadLargeFile : uploadSmallFile;
  return {
    promise: run(file, onProgress, controller.signal),
    abort: () => controller.abort(),
  };
}
