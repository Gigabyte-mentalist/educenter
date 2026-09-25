"use client";

import { useEffect, useSyncExternalStore } from "react";
import { createPortal } from "react-dom";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/Button";

const noopSubscribe = () => () => {};

/**
 * Confirmation before something irreversible. Deletes in this product cannot be undone — there is no bin to
 * restore from — so the dialog names the thing being deleted rather than asking "are you sure?", and the
 * server's refusal (a branch that still has groups, a user with a student card) is shown here as `error`
 * instead of as a toast that disappears before it has been read.
 */
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "O'chirish",
  cancelLabel = "Bekor qilish",
  loading,
  error,
  onConfirm,
  onClose,
}: {
  open: boolean;
  title: string;
  description?: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  loading?: boolean;
  error?: string | null;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const mounted = useSyncExternalStore(noopSubscribe, () => true, () => false);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape" && !loading) onClose();
    }
    if (open) window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose, loading]);

  if (!mounted || !open) return null;

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-end justify-center p-4 sm:items-center" role="dialog" aria-modal>
      <div className="absolute inset-0 bg-ink-900/40" onClick={() => !loading && onClose()} />
      <div className="relative w-full max-w-md rounded-2xl border border-border bg-surface p-5 shadow-xl">
        <div className="flex gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-danger-50 text-danger-600">
            <AlertTriangle className="size-5" aria-hidden />
          </span>
          <div className="min-w-0 flex-1">
            <h2 className="text-[15px] font-semibold text-ink-900">{title}</h2>
            {description && <div className="mt-1 text-[13px] leading-relaxed text-ink-600">{description}</div>}
          </div>
        </div>

        {error && (
          <p className="mt-4 rounded-lg bg-danger-50 px-3 py-2 text-[13px] leading-relaxed text-danger-600">{error}</p>
        )}

        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="secondary" onClick={onClose} disabled={loading}>
            {cancelLabel}
          </Button>
          <Button variant="danger" onClick={onConfirm} loading={loading}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
