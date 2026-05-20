if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "https://localhost:17047;http://localhost:15134");
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "https://localhost:21021");
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_MCP_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_MCP_ENDPOINT_URL", "https://localhost:23078");
}

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", "https://localhost:22143");
}

var builder = DistributedApplication.CreateBuilder(args);

var foundry = builder.AddFoundry("foundry")
    .RunAsFoundryLocal();

var chat = foundry.AddDeployment("chat", "phi-3.5-mini", "1", "Microsoft");

// Durable Task Scheduler emulator — port 8080 is the scheduler endpoint,
// port 8082 is the web dashboard (http://localhost:8082 when running).
var dts = builder.AddContainer("dts", "mcr.microsoft.com/dts/dts-emulator", "latest")
    .WithEndpoint(port: 8080, targetPort: 8080, name: "scheduler", scheme: "http")
    .WithEndpoint(port: 8082, targetPort: 8082, name: "dashboard");

var schedulerEndpoint = dts.GetEndpoint("scheduler");

builder.AddProject<Projects.Squad_AgentFramework_Demo>("squad-agent-framework-demo")
    .WithEnvironment("SQUAD_AF_PROVIDER", "foundry-local")
    .WithEnvironment("SQUAD_AF_EXAMPLE", Environment.GetEnvironmentVariable("SQUAD_AF_EXAMPLE") ?? string.Empty)
    .WithEnvironment("SQUAD_AF_TRACE", Environment.GetEnvironmentVariable("SQUAD_AF_TRACE") ?? "1")
    .WithEnvironment("DTS_ENDPOINT", schedulerEndpoint)
    // Allow Node.js (Copilot CLI's OTLP exporter) to accept Aspire's self-signed
    // development cert. NEVER set this in production — only for local Aspire dev.
    .WithEnvironment("NODE_TLS_REJECT_UNAUTHORIZED", "0")
    // Force the Node.js OTLP exporter to use gRPC (HTTP/2), matching Aspire's
    // gRPC-only OTLP endpoint on port 21021. Without this, Node.js defaults to
    // HTTP/protobuf (HTTP/1.1), which Kestrel rejects with "HTTP/2 over TLS was
    // not negotiated on an HTTP/2-only endpoint". NEVER set in production.
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
    .WithReference(chat)
    .WaitFor(chat)
    .WaitFor(dts);

builder.Build().Run();
