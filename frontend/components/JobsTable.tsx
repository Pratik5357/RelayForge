import Link from "next/link";
import type { JobStatus } from "@/lib/types";
import { isJobFinished } from "@/lib/types";
import { StateBadge } from "./StateBadge";

export function JobsTable({ jobs }: { jobs: JobStatus[] }) {
  return (
    <div className="overflow-x-auto rounded-lg border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
      <table className="w-full text-left text-sm">
        <thead className="border-b border-zinc-200 text-xs uppercase tracking-wide text-zinc-500 dark:border-zinc-800">
          <tr>
            <th className="px-4 py-2 font-medium">Job</th>
            <th className="px-4 py-2 font-medium">State</th>
            <th className="px-4 py-2 font-medium">Steps done</th>
            <th className="px-4 py-2 font-medium">Started</th>
            <th className="px-4 py-2 font-medium">Took</th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <tr key={job.id} className="border-b border-zinc-100 last:border-0 dark:border-zinc-800">
              <td className="px-4 py-2">
                <Link href={`/jobs/${job.id}`} className="font-medium hover:underline">
                  {job.name}
                </Link>
              </td>
              <td className="px-4 py-2">
                <StateBadge state={job.state} />
              </td>
              <td className="px-4 py-2 text-zinc-500">
                {job.tasks.filter((task) => task.state === "Succeeded").length} of {job.tasks.length}
              </td>
              <td className="px-4 py-2 text-zinc-500">
                {new Date(job.createdAt).toLocaleTimeString()}
              </td>
              <td className="px-4 py-2 text-zinc-500">
                {isJobFinished(job.state) && job.completedAt
                  ? duration(job.createdAt, job.completedAt)
                  : "running…"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function duration(from: string, to: string): string {
  const ms = new Date(to).getTime() - new Date(from).getTime();
  return ms < 1000 ? `${ms}ms` : `${(ms / 1000).toFixed(1)}s`;
}
