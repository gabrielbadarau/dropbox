import { useState } from "react";
import type { FileSummary, SharedFileSummary } from "../lib/types";
import { formatBytes, formatRelativeTime } from "../lib/format";
import StatusBadge from "./StatusBadge";
import { DownloadIcon, FileIcon, ShareIcon, SpinnerIcon, TrashIcon } from "./icons";

type ListedFile =
  | (FileSummary & { kind: "mine" })
  | (SharedFileSummary & { kind: "shared"; id: string });

interface FileListProps {
  files: ListedFile[];
  onDownload: (fileId: string, name: string) => Promise<void>;
  onDelete?: (fileId: string) => void;
  onShare?: (fileId: string) => void;
  emptyLabel: string;
}

export default function FileList({
  files,
  onDownload,
  onDelete,
  onShare,
  emptyLabel,
}: FileListProps) {
  const [pendingAction, setPendingAction] = useState<string | null>(null);

  if (files.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-neutral-200 py-20 text-center dark:border-neutral-800">
        <div className="mb-3 flex h-12 w-12 items-center justify-center rounded-xl bg-neutral-100 text-neutral-400 dark:bg-neutral-900">
          <FileIcon className="h-6 w-6" />
        </div>
        <p className="text-sm text-neutral-500 dark:text-neutral-400">
          {emptyLabel}
        </p>
      </div>
    );
  }

  async function withPending(key: string, action: () => Promise<void>) {
    setPendingAction(key);
    try {
      await action();
    } finally {
      setPendingAction(null);
    }
  }

  return (
    <div className="overflow-hidden rounded-2xl border border-neutral-200 dark:border-neutral-800">
      {files.map((file, i) => {
        const status = file.kind === "mine" ? file.status : "Uploaded";
        const downloadDisabled = status !== "Uploaded";
        const pendingKey = `${file.kind}-${file.id}`;

        return (
          <div
            key={pendingKey}
            className={`flex items-center gap-3 px-4 py-3 ${
              i !== files.length - 1
                ? "border-b border-neutral-200 dark:border-neutral-800"
                : ""
            } bg-white transition hover:bg-neutral-50 dark:bg-neutral-900 dark:hover:bg-neutral-800/60`}
          >
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-neutral-100 text-neutral-500 dark:bg-neutral-800 dark:text-neutral-400">
              <FileIcon className="h-4.5 w-4.5" />
            </div>

            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium text-neutral-900 dark:text-neutral-100">
                {file.name}
              </p>
              <p className="mt-0.5 flex items-center gap-2 text-xs text-neutral-500 dark:text-neutral-400">
                <span>{formatBytes(file.size)}</span>
                <span aria-hidden>·</span>
                <span>
                  {formatRelativeTime(
                    file.kind === "mine" ? file.createdAt : file.sharedAt,
                  )}
                </span>
              </p>
            </div>

            <StatusBadge status={status} />

            <div className="flex items-center gap-1">
              {onShare && file.kind === "mine" && (
                <button
                  onClick={() => onShare(file.id)}
                  disabled={downloadDisabled}
                  title="Share"
                  className="flex h-8 w-8 items-center justify-center rounded-lg text-neutral-500 transition hover:bg-neutral-100 hover:text-neutral-900 disabled:cursor-not-allowed disabled:opacity-30 dark:text-neutral-400 dark:hover:bg-neutral-800 dark:hover:text-neutral-100"
                >
                  <ShareIcon className="h-4 w-4" />
                </button>
              )}

              <button
                onClick={() =>
                  withPending(`dl-${pendingKey}`, () =>
                    onDownload(file.id, file.name),
                  )
                }
                disabled={downloadDisabled}
                title="Download"
                className="flex h-8 w-8 items-center justify-center rounded-lg text-neutral-500 transition hover:bg-neutral-100 hover:text-neutral-900 disabled:cursor-not-allowed disabled:opacity-30 dark:text-neutral-400 dark:hover:bg-neutral-800 dark:hover:text-neutral-100"
              >
                {pendingAction === `dl-${pendingKey}` ? (
                  <SpinnerIcon className="h-4 w-4 animate-spin" />
                ) : (
                  <DownloadIcon className="h-4 w-4" />
                )}
              </button>

              {onDelete && file.kind === "mine" && (
                <button
                  onClick={() => onDelete(file.id)}
                  title="Delete"
                  className="flex h-8 w-8 items-center justify-center rounded-lg text-neutral-500 transition hover:bg-red-50 hover:text-red-600 dark:text-neutral-400 dark:hover:bg-red-950/40 dark:hover:text-red-400"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
