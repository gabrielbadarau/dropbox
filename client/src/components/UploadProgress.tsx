import { formatBytes } from "../lib/format";
import { CheckCircleIcon, FileIcon, XCircleIcon } from "./icons";

export interface UploadItem {
  id: string;
  name: string;
  size: number;
  progress: number; // 0-1
  status: "uploading" | "done" | "error";
  error?: string;
}

export default function UploadProgress({
  items,
  onDismiss,
}: {
  items: UploadItem[];
  onDismiss: (id: string) => void;
}) {
  if (items.length === 0) return null;

  return (
    <div className="fixed bottom-5 right-5 z-40 w-80 space-y-2">
      {items.map((item) => (
        <div
          key={item.id}
          className="overflow-hidden rounded-xl border border-neutral-200 bg-white p-3 shadow-lg dark:border-neutral-800 dark:bg-neutral-900"
        >
          <div className="flex items-center gap-2.5">
            <div
              className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${
                item.status === "error"
                  ? "bg-red-50 text-red-600 dark:bg-red-950/40 dark:text-red-400"
                  : item.status === "done"
                    ? "bg-emerald-50 text-emerald-600 dark:bg-emerald-950/40 dark:text-emerald-400"
                    : "bg-accent-50 text-accent-600 dark:bg-accent-950/40 dark:text-accent-400"
              }`}
            >
              {item.status === "done" ? (
                <CheckCircleIcon className="h-4.5 w-4.5" />
              ) : item.status === "error" ? (
                <XCircleIcon className="h-4.5 w-4.5" />
              ) : (
                <FileIcon className="h-4.5 w-4.5" />
              )}
            </div>

            <div className="min-w-0 flex-1">
              <p className="truncate text-xs font-medium text-neutral-900 dark:text-neutral-100">
                {item.name}
              </p>
              <p className="text-[11px] text-neutral-500 dark:text-neutral-400">
                {item.status === "error"
                  ? (item.error ?? "Upload failed")
                  : item.status === "done"
                    ? formatBytes(item.size)
                    : `${Math.round(item.progress * 100)}% of ${formatBytes(item.size)}`}
              </p>
            </div>

            {(item.status === "done" || item.status === "error") && (
              <button
                onClick={() => onDismiss(item.id)}
                className="text-neutral-400 hover:text-neutral-600 dark:hover:text-neutral-200"
              >
                <svg
                  viewBox="0 0 24 24"
                  className="h-3.5 w-3.5"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth={2}
                  strokeLinecap="round"
                >
                  <path d="M6 6l12 12M18 6L6 18" />
                </svg>
              </button>
            )}
          </div>

          {item.status === "uploading" && (
            <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-neutral-100 dark:bg-neutral-800">
              <div
                className="h-full rounded-full bg-accent-500 transition-all duration-200"
                style={{ width: `${Math.max(item.progress * 100, 3)}%` }}
              />
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
