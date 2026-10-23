using System.Diagnostics;
using System.Net.Http.Json;

// Submits --jobs jobs of --tasks tasks each (a fan-out/fan-in DAG of simulated tasks lasting
// --duration ms) to a running API, waits for all of them to finish, and prints throughput.
//
//   dotnet run --project tools/RelayForge.LoadTest -- --api http://localhost:8080 --jobs 1000 --tasks 10 --duration 10
//
// Throughput is measured end to end: tasks finished / (last job finished - first submit).

var options = Args.Parse(args);
using var http = new HttpClient { BaseAddress = new Uri(options.Api), Timeout = TimeSpan.FromSeconds(60) };

Console.WriteLine($"Submitting {options.Jobs} jobs x {options.Tasks} tasks ({options.DurationMs} ms each) to {options.Api}");

var started = Stopwatch.StartNew();
var submitted = new System.Collections.Concurrent.ConcurrentDictionary<Guid, TimeSpan>();
var runId = Guid.NewGuid().ToString("N")[..6];

await Parallel.ForEachAsync(Enumerable.Range(0, options.Jobs), new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (i, ct) =>
{
    var response = await http.PostAsJsonAsync("/api/jobs", BuildJob($"load-{runId}-{i}", options), ct);
    response.EnsureSuccessStatusCode();
    var job = (await response.Content.ReadFromJsonAsync<JobSummary>(ct))!;
    submitted[job.Id] = started.Elapsed;
});

var submitTime = started.Elapsed;
Console.WriteLine($"Submitted in {submitTime.TotalSeconds:0.0} s; waiting for completion...");

var finished = new Dictionary<Guid, TimeSpan>();
var states = new Dictionary<string, int>();

while (finished.Count < submitted.Count)
{
    await Task.Delay(500);

    // Newest first; this run's jobs are the newest ones.
    for (var skip = 0; skip < submitted.Count + 200; skip += 200)
    {
        var page = await http.GetFromJsonAsync<List<JobSummary>>($"/api/jobs?take=200&skip={skip}") ?? [];

        foreach (var job in page.Where(j => submitted.ContainsKey(j.Id) && !finished.ContainsKey(j.Id)))
        {
            if (job.State is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled")
            {
                finished[job.Id] = started.Elapsed;
                states[job.State] = states.GetValueOrDefault(job.State) + 1;
            }
        }

        if (page.Count < 200)
        {
            break;
        }
    }

    Console.Write($"\r  {finished.Count}/{submitted.Count} finished after {started.Elapsed.TotalSeconds:0.0} s   ");
}

var total = started.Elapsed;
var latencies = finished.Select(f => (f.Value - submitted[f.Key]).TotalMilliseconds).Order().ToList();
var tasks = options.Jobs * options.Tasks;

Console.WriteLine();
Console.WriteLine($"States: {string.Join(", ", states.Select(s => $"{s.Key} {s.Value}"))}");
Console.WriteLine($"Wall time: {total.TotalSeconds:0.00} s");
Console.WriteLine($"Throughput: {tasks / total.TotalSeconds:0.0} tasks/s");
Console.WriteLine($"Job latency (submit to finish): p50 {Percentile(latencies, 0.5):0} ms, p95 {Percentile(latencies, 0.95):0} ms, p99 {Percentile(latencies, 0.99):0} ms");
Console.WriteLine($"RESULT tasks_per_second={tasks / total.TotalSeconds:0.0} wall_s={total.TotalSeconds:0.00} p50_ms={Percentile(latencies, 0.5):0} p95_ms={Percentile(latencies, 0.95):0}");

static double Percentile(List<double> sorted, double p) =>
    sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

// root -> (tasks - 2) parallel middles -> sink
static object BuildJob(string name, Args options)
{
    var middles = Enumerable.Range(1, Math.Max(0, options.Tasks - 2)).Select(i => $"m{i}").ToList();
    var payload = new { durationMs = options.DurationMs };
    var tasks = new List<object> { new { key = "root", name = "root", type = "simulated", payload, dependsOn = Array.Empty<string>() } };
    tasks.AddRange(middles.Select(m => new { key = m, name = m, type = "simulated", payload, dependsOn = new[] { "root" } }));

    if (options.Tasks > 1)
    {
        tasks.Add(new { key = "sink", name = "sink", type = "simulated", payload, dependsOn = middles.Count > 0 ? middles.ToArray() : ["root"] });
    }

    return new { name, tasks };
}

record JobSummary(Guid Id, string State);

record Args(string Api, int Jobs, int Tasks, int DurationMs)
{
    public static Args Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var i = Array.IndexOf(args, $"--{name}");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        return new Args(Get("api", "http://localhost:8080"), int.Parse(Get("jobs", "1000")), int.Parse(Get("tasks", "10")), int.Parse(Get("duration", "10")));
    }
}
