import { JobDetailClient } from "./JobDetailClient";

export default async function JobDetailPage(props: PageProps<"/jobs/[id]">) {
  const { id } = await props.params;
  return <JobDetailClient key={id} jobId={id} />;
}
