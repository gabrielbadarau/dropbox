import { useState } from "react";
import { shareFile } from "../lib/files";
import type { ShareResult } from "../lib/types";
import { CheckCircleIcon, XCircleIcon } from "./icons";

export default function ShareDialog({
  fileId,
  fileName,
  onClose,
}: {
  fileId: string;
  fileName: string;
  onClose: () => void;
}) {
  const [emailsInput, setEmailsInput] = useState("");
  const [results, setResults] = useState<ShareResult[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    const emails = emailsInput
      .split(/[,\n]/)
      .map((e) => e.trim())
      .filter(Boolean);
    if (emails.length === 0) return;

    setLoading(true);
    setError(null);
    try {
      const response = await shareFile(fileId, emails);
      setResults(response.results);
      setEmailsInput("");
    } catch {
      setError("Could not share this file. Try again.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-sm rounded-2xl border border-neutral-200 bg-white p-5 shadow-xl dark:border-neutral-800 dark:bg-neutral-900"
      >
        <h2 className="text-sm font-semibold text-neutral-900 dark:text-neutral-100">
          Share "{fileName}"
        </h2>
        <p className="mt-1 text-sm text-neutral-500 dark:text-neutral-400">
          Enter one or more emails, separated by commas.
        </p>

        <form onSubmit={handleSubmit} className="mt-4">
          <textarea
            value={emailsInput}
            onChange={(e) => setEmailsInput(e.target.value)}
            placeholder="charlie@example.com, dave@example.com"
            rows={2}
            className="w-full resize-none rounded-lg border border-neutral-300 bg-white px-3 py-2 text-sm outline-none focus:border-accent-500 focus:ring-2 focus:ring-accent-500/20 dark:border-neutral-700 dark:bg-neutral-950"
          />

          {error && (
            <p className="mt-2 text-xs text-red-600 dark:text-red-400">
              {error}
            </p>
          )}

          {results && (
            <ul className="mt-3 space-y-1.5">
              {results.map((r) => (
                <li
                  key={r.email}
                  className="flex items-center gap-2 text-xs text-neutral-600 dark:text-neutral-300"
                >
                  {r.success ? (
                    <CheckCircleIcon className="h-3.5 w-3.5 shrink-0 text-emerald-500" />
                  ) : (
                    <XCircleIcon className="h-3.5 w-3.5 shrink-0 text-red-500" />
                  )}
                  <span className="font-medium">{r.email}</span>
                  <span className="text-neutral-400">— {r.reason}</span>
                </li>
              ))}
            </ul>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg border border-neutral-300 px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 dark:border-neutral-700 dark:text-neutral-300 dark:hover:bg-neutral-800"
            >
              Done
            </button>
            <button
              type="submit"
              disabled={loading || !emailsInput.trim()}
              className="rounded-lg bg-accent-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-accent-700 disabled:opacity-50"
            >
              {loading ? "Sharing…" : "Share"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
