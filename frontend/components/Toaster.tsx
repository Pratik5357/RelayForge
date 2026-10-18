"use client";

import Link from "next/link";
import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import type { JobState, TaskState } from "@/lib/types";
import { Remark } from "./Remark";
import { CloseButton } from "./Overlay";

type Toast = {
  id: number;
  title: string;
  state: JobState | TaskState;
  href?: string;
  linkLabel?: string;
};

type ToastInput = Omit<Toast, "id">;

const ToastContext = createContext<(toast: ToastInput) => void>(() => {});

const VISIBLE = 3;
const LIFETIME_MS = 5000;

/** Sonner-style stack in the bottom-right corner: newest in front, older ones step back. */
export function Toaster({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [paused, setPaused] = useState(false);
  const nextId = useRef(1);
  const reduce = useReducedMotion();

  const push = useCallback((toast: ToastInput) => {
    const id = nextId.current++;
    setToasts((prev) => [...prev, { ...toast, id }]);
  }, []);

  const dismiss = useCallback((id: number) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  // The front toast expires on its own, but never while hovered or while the tab is hidden.
  useEffect(() => {
    if (paused || toasts.length === 0) return;
    const front = toasts[toasts.length - 1];
    let timer: ReturnType<typeof setTimeout> | undefined;

    function arm() {
      clearTimeout(timer);
      if (document.visibilityState === "visible") timer = setTimeout(() => dismiss(front.id), LIFETIME_MS);
    }

    arm();
    document.addEventListener("visibilitychange", arm);
    return () => {
      clearTimeout(timer);
      document.removeEventListener("visibilitychange", arm);
    };
  }, [toasts, paused, dismiss]);

  const shown = toasts.slice(-VISIBLE);

  return (
    <ToastContext.Provider value={push}>
      {children}
      <section
        aria-label="Notifications"
        aria-live="polite"
        className="pointer-events-none fixed bottom-6 right-6 z-40 h-0 w-[min(calc(100%-48px),400px)]"
        onPointerEnter={() => setPaused(true)}
        onPointerLeave={() => setPaused(false)}
      >
        <AnimatePresence initial={false}>
          {shown.map((toast, i) => {
            const depth = shown.length - 1 - i;
            return (
              <motion.div
                key={toast.id}
                role="status"
                className="on-board pointer-events-auto absolute bottom-0 right-0 flex w-full items-center gap-3.5 rounded-xl bg-board py-3.5 pl-[18px] pr-2.5 text-board-text shadow-[0_18px_40px_-16px_rgb(18_20_18/0.6)]"
                style={{ zIndex: 10 - depth, transformOrigin: "50% 100%" }}
                initial={reduce ? { opacity: 0 } : { opacity: 0, transform: "translateY(calc(100% + 32px)) scale(1)" }}
                animate={{
                  opacity: depth === 0 ? 1 : 0.85,
                  transform: reduce ? "none" : `translateY(${depth * -14}px) scale(${1 - depth * 0.05})`,
                }}
                exit={reduce ? { opacity: 0 } : { opacity: 0, transform: "translateY(calc(100% + 32px)) scale(1)" }}
                // Plain CSS `ease`, a touch slower than UI motion: toasts read calmer than controls.
                transition={{ duration: 0.4, ease: [0.25, 0.1, 0.25, 1] }}
              >
                <Remark state={toast.state} />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-[15px] font-bold">{toast.title}</span>
                  {toast.href && (
                    <Link href={toast.href} className="self-start text-sm font-bold text-amber" transitionTypes={["nav-forward"]}>
                      {toast.linkLabel ?? "View"}
                    </Link>
                  )}
                </span>
                <CloseButton onClick={() => dismiss(toast.id)} label="Dismiss" />
              </motion.div>
            );
          })}
        </AnimatePresence>
      </section>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}
