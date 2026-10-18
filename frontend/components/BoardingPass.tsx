"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, submitJob } from "@/lib/api";
import type { Scenario } from "@/lib/scenarios";
import { Arrow } from "./ui/Button";
import { Spinner } from "./Spinner";
import { Perforation } from "./Perforation";
import { RevealItem } from "./Reveal";

/** Titles carry their tour number ("3. ..."); the pass prints the number on its stub instead. */
function splitTitle(title: string) {
  const match = /^(\d+)\.\s+(.*)$/.exec(title);
  return match ? { n: match[1], text: match[2] } : { n: "", text: title };
}

/** One tour scenario as a boarding pass: what it shows on the left, the stub to run it on the right. */
export function BoardingPass({ scenario, primary = false }: { scenario: Scenario; primary?: boolean }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const { n, text } = splitTitle(scenario.title);

  async function run() {
    setBusy(true);
    setError(null);
    try {
      const job = await submitJob(scenario.job);
      router.push(`/jobs/${job.id}?scenario=${scenario.id}`, { transitionTypes: ["nav-forward"] });
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not start this scenario.");
      setBusy(false);
    }
  }

  return (
    // The list item rises in on scroll (Motion owns its transform); the pass inside owns the
    // hover lift, so the two transforms never fight.
    <RevealItem distance={28} className="flex">
      <div className="pass @container flex w-full flex-wrap overflow-hidden rounded-2xl bg-paper shadow-[0_1px_0_rgb(18_20_18/0.06),0_10px_24px_-18px_rgb(18_20_18/0.35)]">
        <div className="flex min-w-0 flex-[999_1_300px] flex-col gap-4 px-6 py-[22px]">
          <div className="flex flex-col gap-1.5">
            <h3 className="text-[23px] leading-[1.12] tracking-[-0.02em]">{text}</h3>
            <p className="text-[15px] leading-normal text-muted">{scenario.summary}</p>
            {error && (
              <p role="alert" className="text-sm font-semibold text-[var(--error)]">
                {error}
              </p>
            )}
          </div>
          <dl className="mt-auto flex flex-wrap border-t border-[var(--hairline-soft)]">
            <div className="flex flex-[3_1_200px] flex-col gap-0.5 pr-4 pt-2.5">
              <dt className="label text-label">Shows</dt>
              <dd className="text-[15px] font-semibold">{scenario.concept}</dd>
            </div>
            <div className="flex flex-[1_1_110px] flex-col gap-0.5 pl-4 pt-2.5 shadow-[inset_1px_0_0_var(--hairline-soft)]">
              <dt className="label text-label">Takes</dt>
              <dd className="font-mono text-[15px]">{scenario.runtime}</dd>
            </div>
          </dl>
        </div>

        <div
          className={`stub relative flex flex-[1_0_150px] flex-col justify-between gap-4 py-[22px] pl-[26px] pr-[22px] ${
            primary ? "on-board bg-board text-board-text" : "bg-[#f1f2ef] text-ink"
          }`}
        >
          <Perforation tone={primary ? "board" : "paper"} at="pass" />
          <div className="flex flex-col gap-0.5">
            <span aria-hidden className="font-mono text-[56px] font-semibold leading-[0.95] tracking-[-0.04em]">
              {n}
            </span>
            <span className="font-mono text-xs uppercase tracking-[0.12em] opacity-75">{scenario.id}</span>
          </div>
          <button
            type="button"
            onClick={run}
            disabled={busy}
            aria-label={`Run it: ${scenario.title}`}
            className={`press inline-flex min-h-[46px] items-center justify-center gap-2 rounded-md px-[18px] text-[15px] font-bold disabled:cursor-wait ${
              primary ? "bg-amber text-ink hover:bg-[var(--amber-hover)]" : "bg-ink text-white hover:bg-[var(--ink-hover)]"
            }`}
          >
            {busy ? (
              <>
                <Spinner /> Starting…
              </>
            ) : (
              <>
                Run it <Arrow />
              </>
            )}
          </button>
        </div>
      </div>
    </RevealItem>
  );
}
