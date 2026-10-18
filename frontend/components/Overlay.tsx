"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { EASE_DRAWER, EASE_OUT } from "@/lib/motion";

type OverlayProps = {
  open: boolean;
  onClose: () => void;
  /** "dialog" stays centred; "sheet" comes from the right edge and leaves the same way. */
  variant: "dialog" | "sheet";
  labelledBy: string;
  describedBy?: string;
  role?: "dialog" | "alertdialog";
  children: ReactNode;
};

/**
 * Overlays are a native <dialog> opened with showModal(), so focus trapping, Escape and the
 * inert page behind come from the browser. Motion animates the scrim and panel in, and the
 * dialog only closes once the exit animation has finished.
 */
export function Overlay({ open, onClose, variant, labelledBy, describedBy, role = "dialog", children }: OverlayProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const reduce = useReducedMotion();

  useEffect(() => {
    const dialog = ref.current;
    if (open && dialog && !dialog.open) dialog.showModal();
  }, [open]);

  const sheet = variant === "sheet";
  const enter = sheet ? { duration: 0.5, ease: EASE_DRAWER } : { duration: 0.25, ease: EASE_OUT };
  const leave = sheet ? { duration: 0.3, ease: EASE_DRAWER } : { duration: 0.15, ease: EASE_OUT };

  const hidden = reduce
    ? { opacity: 0 }
    : sheet
      ? { transform: "translateX(100%)" }
      : { opacity: 0, transform: "scale(0.96)" };
  const shown = reduce
    ? { opacity: 1 }
    : sheet
      ? { transform: "translateX(0%)" }
      : { opacity: 1, transform: "scale(1)" };

  return (
    <dialog
      ref={ref}
      className="overlay"
      role={role}
      aria-labelledby={labelledBy}
      aria-describedby={describedBy}
      // Escape runs our exit animation instead of the browser's instant close. Handling the key
      // on the dialog itself is the reliable path; `cancel` covers other close requests.
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          e.preventDefault();
          onClose();
        }
      }}
      onCancel={(e) => {
        e.preventDefault();
        onClose();
      }}
    >
      <AnimatePresence onExitComplete={() => ref.current?.close()}>
        {open && (
          <motion.div
            key="scrim"
            aria-hidden
            className="absolute inset-0 bg-[rgb(18_20_18/0.5)]"
            onClick={onClose}
            initial={{ opacity: 0 }}
            animate={{ opacity: 1, transition: enter }}
            exit={{ opacity: 0, transition: leave }}
          />
        )}
        {open && (
          <motion.div
            key="panel"
            initial={hidden}
            animate={{ ...shown, transition: enter }}
            exit={{ ...hidden, transition: leave }}
            className={
              sheet
                ? "on-board absolute inset-y-0 right-0 flex w-full max-w-[460px] flex-col overflow-y-auto bg-board text-board-text shadow-[-30px_0_60px_-30px_rgb(0_0_0/0.6)]"
                : "absolute left-1/2 top-1/2 w-[min(calc(100%-32px),480px)] rounded-2xl bg-paper p-7 shadow-[0_30px_60px_-24px_rgb(18_20_18/0.55)]"
            }
            style={sheet ? undefined : { translate: "-50% -50%" }}
          >
            {children}
          </motion.div>
        )}
      </AnimatePresence>
    </dialog>
  );
}

export function CloseButton({ onClick, label }: { onClick: () => void; label: string }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      className="press grid size-11 shrink-0 place-items-center rounded-md text-board-text hover:bg-board-control"
    >
      <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden>
        <path d="M4 4l8 8M12 4l-8 8" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      </svg>
    </button>
  );
}
