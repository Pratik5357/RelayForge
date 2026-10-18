"use client";

import { useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "motion/react";
import { EASE_OUT } from "@/lib/motion";

export type Term = { name: string; definition: string; seeIn: string };

function idOf(name: string) {
  return `term-${name.toLowerCase().replace(/[^a-z0-9]+/g, "-")}`;
}

export function GlossaryList({ terms }: { terms: Term[] }) {
  const [query, setQuery] = useState("");
  const reduce = useReducedMotion();
  const q = query.trim().toLowerCase();
  const shown = terms.filter((t) => !q || `${t.name} ${t.definition}`.toLowerCase().includes(q));

  return (
    <>
      <div className="flex flex-wrap items-end justify-between gap-x-10 gap-y-5">
        <div className="flex flex-col gap-2.5">
          <h1 className="text-[clamp(52px,6vw,72px)] leading-[0.95] tracking-[-0.045em]">Glossary</h1>
          <p className="max-w-[52ch] text-[17px] text-muted">
            The ideas behind RelayForge in plain language, and where to see each one happen.
          </p>
        </div>
        <div className="flex flex-[0_1_360px] flex-col gap-1.5">
          <label htmlFor="find-term" className="label text-label">
            Find a term
          </label>
          <input
            id="find-term"
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="e.g. backoff"
            className="h-12 w-full rounded-md border-0 bg-paper px-3.5 text-[17px] shadow-[inset_0_0_0_1.5px_var(--ink)] focus:outline-none focus:shadow-[inset_0_0_0_2px_var(--ink),0_0_0_4px_var(--amber)]"
          />
        </div>
      </div>

      <nav aria-label="Terms" className="flex flex-wrap gap-1.5">
        {shown.map((term) => (
          <a
            key={term.name}
            href={`#${idOf(term.name)}`}
            className="press inline-flex min-h-9 items-center rounded bg-paper px-3 text-sm font-bold text-ink no-underline shadow-[inset_0_0_0_1px_var(--hairline)] hover:bg-ink hover:text-white"
          >
            {term.name}
          </a>
        ))}
      </nav>

      {shown.length === 0 && (
        <p role="status" className="fade-up py-8 text-[17px] text-muted">
          No term matches &ldquo;{query}&rdquo;. Try a shorter word, like retry or step.
        </p>
      )}

      <dl className="flex flex-col border-t-2 border-ink">
        <AnimatePresence>
          {shown.map((term) => (
            <motion.div
              key={term.name}
              id={idOf(term.name)}
              layout="position"
              // Rows rise in as they scroll into view; a row the search brings back is already in
              // view, so it fades straight in.
              initial={reduce ? { opacity: 0 } : { opacity: 0, transform: "translateY(16px)" }}
              whileInView={reduce ? { opacity: 1 } : { opacity: 1, transform: "translateY(0px)" }}
              viewport={{ once: true, margin: "0px 0px -10% 0px" }}
              exit={{ opacity: 0, transition: { duration: 0.12 } }}
              transition={{ duration: 0.45, ease: EASE_OUT }}
              className="flex scroll-mt-6 flex-wrap gap-x-12 gap-y-2 border-b border-hairline py-[26px]"
            >
              <dt className="flex-[1_1_280px] text-[28px] font-extrabold leading-[1.1] tracking-[-0.025em]">{term.name}</dt>
              <dd className="flex flex-[2_1_480px] flex-col gap-3.5">
                <p className="max-w-[64ch] text-[17px] leading-relaxed">{term.definition}</p>
                <p className="flex max-w-[64ch] items-start gap-3 text-[15px] text-muted">
                  <span className="inline-flex h-6 shrink-0 items-center gap-1.5 rounded bg-board px-2 text-xs font-bold uppercase tracking-[0.07em] text-amber">
                    <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden>
                      <path d="M1 5h7M5.5 2 8.5 5 5.5 8" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                    See it
                  </span>
                  <span>{term.seeIn}</span>
                </p>
              </dd>
            </motion.div>
          ))}
        </AnimatePresence>
      </dl>
    </>
  );
}
