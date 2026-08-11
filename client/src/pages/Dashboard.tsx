import { useCallback, useEffect, useState } from "react";
import Layout from "../components/Layout";
import FileList from "../components/FileList";
import ConfirmDialog from "../components/ConfirmDialog";
import {
  deleteFile,
  getDownloadUrl,
  listMyFiles,
  listSharedWithMe,
} from "../lib/files";
import type { FileSummary, SharedFileSummary } from "../lib/types";
import { SpinnerIcon, UsersIcon } from "../components/icons";
import { FileIcon } from "../components/icons";

type Tab = "mine" | "shared";

export default function Dashboard() {
  const [tab, setTab] = useState<Tab>("mine");
  const [myFiles, setMyFiles] = useState<FileSummary[] | null>(null);
  const [sharedFiles, setSharedFiles] = useState<SharedFileSummary[] | null>(
    null,
  );
  const [error, setError] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  const refresh = useCallback(async () => {
    setError(null);
    try {
      const [mine, shared] = await Promise.all([
        listMyFiles(),
        listSharedWithMe(),
      ]);
      setMyFiles(mine);
      setSharedFiles(shared);
    } catch {
      setError("Could not load your files.");
    }
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  async function handleDownload(fileId: string, name: string) {
    const { downloadUrl } = await getDownloadUrl(fileId);
    const link = document.createElement("a");
    link.href = downloadUrl;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
  }

  async function confirmDelete() {
    if (!confirmDeleteId) return;
    const fileId = confirmDeleteId;
    setDeleting(true);
    try {
      await deleteFile(fileId);
      setMyFiles((files) => files?.filter((f) => f.id !== fileId) ?? null);
      setConfirmDeleteId(null);
    } catch {
      setError("Could not delete that file.");
    } finally {
      setDeleting(false);
    }
  }

  const loading = myFiles === null || sharedFiles === null;

  return (
    <Layout>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-lg font-semibold tracking-tight">Your files</h1>
      </div>

      <div className="mb-5 flex gap-1 rounded-lg bg-neutral-100 p-1 text-sm dark:bg-neutral-900">
        <button
          onClick={() => setTab("mine")}
          className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 font-medium transition ${
            tab === "mine"
              ? "bg-white text-neutral-900 shadow-sm dark:bg-neutral-800 dark:text-neutral-100"
              : "text-neutral-500 hover:text-neutral-900 dark:text-neutral-400 dark:hover:text-neutral-100"
          }`}
        >
          <FileIcon className="h-4 w-4" />
          My files
          {myFiles && (
            <span className="text-xs text-neutral-400">{myFiles.length}</span>
          )}
        </button>
        <button
          onClick={() => setTab("shared")}
          className={`flex items-center gap-1.5 rounded-md px-3 py-1.5 font-medium transition ${
            tab === "shared"
              ? "bg-white text-neutral-900 shadow-sm dark:bg-neutral-800 dark:text-neutral-100"
              : "text-neutral-500 hover:text-neutral-900 dark:text-neutral-400 dark:hover:text-neutral-100"
          }`}
        >
          <UsersIcon className="h-4 w-4" />
          Shared with me
          {sharedFiles && (
            <span className="text-xs text-neutral-400">
              {sharedFiles.length}
            </span>
          )}
        </button>
      </div>

      {error && (
        <p className="mb-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-950/40 dark:text-red-400">
          {error}
        </p>
      )}

      {loading ? (
        <div className="flex justify-center py-20 text-neutral-400">
          <SpinnerIcon className="h-6 w-6 animate-spin" />
        </div>
      ) : tab === "mine" ? (
        <FileList
          files={myFiles!.map((f) => ({ ...f, kind: "mine" as const }))}
          onDownload={handleDownload}
          onDelete={setConfirmDeleteId}
          emptyLabel="No files yet. Upload something to get started."
        />
      ) : (
        <FileList
          files={sharedFiles!.map((f) => ({
            ...f,
            id: f.fileId,
            kind: "shared" as const,
          }))}
          onDownload={handleDownload}
          emptyLabel="Nothing has been shared with you yet."
        />
      )}

      {confirmDeleteId && (
        <ConfirmDialog
          title="Delete file?"
          message="This cannot be undone. The file will be permanently removed from storage."
          confirmLabel={deleting ? "Deleting…" : "Delete"}
          danger
          onConfirm={confirmDelete}
          onCancel={() => setConfirmDeleteId(null)}
        />
      )}
    </Layout>
  );
}
