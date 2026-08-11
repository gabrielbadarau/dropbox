import { BellIcon } from "./icons";
import type { ChangeEventSummary } from "../lib/types";

const LABELS: Record<ChangeEventSummary["type"], string> = {
  Created: "started uploading",
  Uploaded: "finished uploading",
  Shared: "was shared with you",
  Deleted: "was deleted",
};

export default function SyncNotifications({
  items,
}: {
  items: (ChangeEventSummary & { id: string })[];
}) {
  if (items.length === 0) return null;

  return (
    <div className="fixed right-5 top-16 z-40 w-72 space-y-2">
      {items.map((item) => (
        <div
          key={item.id}
          className="flex animate-fadein items-center gap-2.5 rounded-xl border border-neutral-200 bg-white px-3 py-2.5 shadow-lg dark:border-neutral-800 dark:bg-neutral-900"
        >
          <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-accent-50 text-accent-600 dark:bg-accent-950/40 dark:text-accent-400">
            <BellIcon className="h-3.5 w-3.5" />
          </div>
          <p className="text-xs text-neutral-600 dark:text-neutral-300">
            <span className="font-medium text-neutral-900 dark:text-neutral-100">
              {item.fileName}
            </span>{" "}
            {LABELS[item.type]}
          </p>
        </div>
      ))}
    </div>
  );
}
