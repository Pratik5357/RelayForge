import Link from "next/link";
import { JobList } from "@/components/JobList";
import { apiBaseUrl } from "@/lib/api";

export default function JobsPage() {
  return (
    <section className="space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-xl font-semibold">Runs</h1>
          <p className="text-sm text-zinc-500">
            Every job ever submitted to this instance, newest first. Click one to watch or
            replay what happened.
          </p>
        </div>
        <Link
          href="/jobs/new"
          className="rounded-md bg-zinc-900 px-3 py-1.5 text-sm font-medium text-white hover:bg-zinc-700 dark:bg-white dark:text-zinc-900 dark:hover:bg-zinc-200"
        >
          New job
        </Link>
      </div>

      <JobList />

      <p className="text-xs text-zinc-500">
        Refreshed every 3 seconds from <code className="font-mono">{apiBaseUrl}/api/jobs</code>.
      </p>
    </section>
  );
}
