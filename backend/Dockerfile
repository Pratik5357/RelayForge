# One build, three images:
#   --target runner  RelayForge.Runner alone: the image Container isolation starts per task
#   --target worker  RelayForge.Worker: claims and runs tasks, sweep, scheduler (no HTTP)
#   (default) api    RelayForge.Api: HTTP, SignalR, outbox dispatcher; also runs workers
#                    unless WorkerPool__RunInApi=false. Railway builds this one.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/RelayForge.Api/RelayForge.Api.csproj -c Release -o /out/api \
 && dotnet publish src/RelayForge.Worker/RelayForge.Worker.csproj -c Release -o /out/worker \
 && dotnet publish src/RelayForge.Runner/RelayForge.Runner.csproj -c Release -o /out/runner

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runner
WORKDIR /app
COPY --from=build /out/runner .
USER app
ENTRYPOINT ["dotnet", "RelayForge.Runner.dll"]

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER app
ENTRYPOINT ["dotnet", "RelayForge.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS api
WORKDIR /app
COPY --from=build /out/api .
# Railway injects PORT; fall back to 8080 elsewhere.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet RelayForge.Api.dll"]
