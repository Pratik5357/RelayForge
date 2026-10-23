using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Telemetry;

// RelayForge.Worker: claims and runs tasks, reclaims expired leases and fires schedules. No
// HTTP. Run as many as you like next to an API started with WorkerPool:RunInApi=false; they
// coordinate only through Postgres (SKIP LOCKED claims, advisory-lock leaders, NOTIFY).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddRelayForgeCore(builder.Configuration);
builder.Services.AddRelayForgeWorkers(builder.Configuration);
builder.Services.AddRelayForgeTelemetry(builder.Configuration, "relayforge-worker");

builder.Build().Run();
