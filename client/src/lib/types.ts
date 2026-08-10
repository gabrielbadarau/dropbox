export type FileStatus = "Uploading" | "Uploaded";

export interface FileSummary {
  id: string;
  name: string;
  size: number;
  mimeType: string | null;
  status: FileStatus;
  createdAt: string;
  updatedAt: string;
}

export interface DownloadUrlResponse {
  downloadUrl: string;
  expiresAt: string;
  name: string;
  mimeType: string | null;
}

export interface PresignedUrlResponse {
  fileId: string;
  uploadUrl: string;
  expiresAt: string;
}

export interface PartUploadInfo {
  partNumber: number;
  url: string | null;
  alreadyUploaded: boolean;
}

export interface MultipartUploadResponse {
  fileId: string;
  uploadId: string;
  parts: PartUploadInfo[];
}

export interface ShareResult {
  email: string;
  success: boolean;
  reason: string;
}

export interface ShareFileResponse {
  results: ShareResult[];
}

export interface SharedFileSummary {
  fileId: string;
  name: string;
  size: number;
  mimeType: string | null;
  ownerId: string;
  sharedAt: string;
}

export type ChangeType = "Created" | "Uploaded" | "Shared" | "Deleted";

export interface ChangeEventSummary {
  fileId: string;
  fileName: string;
  type: ChangeType;
  occurredAt: string;
}

export interface AuthResponse {
  token: string;
  userId: string;
  email: string;
}
