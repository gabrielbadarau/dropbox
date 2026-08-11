import { api } from "./api";
import type {
  DownloadUrlResponse,
  FileSummary,
  ShareFileResponse,
  SharedFileSummary,
} from "./types";

export async function listMyFiles(): Promise<FileSummary[]> {
  const { data } = await api.get<FileSummary[]>("/files/mine");
  return data;
}

export async function listSharedWithMe(): Promise<SharedFileSummary[]> {
  const { data } = await api.get<SharedFileSummary[]>("/files/shared-with-me");
  return data;
}

export async function getDownloadUrl(
  fileId: string,
): Promise<DownloadUrlResponse> {
  const { data } = await api.get<DownloadUrlResponse>(
    `/files/${fileId}/presigned-url`,
  );
  return data;
}

export async function deleteFile(fileId: string): Promise<void> {
  await api.delete(`/files/${fileId}`);
}

export async function shareFile(
  fileId: string,
  emails: string[],
): Promise<ShareFileResponse> {
  const { data } = await api.post<ShareFileResponse>(`/files/${fileId}/share`, {
    emails,
  });
  return data;
}
