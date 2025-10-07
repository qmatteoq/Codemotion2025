using Aspire.Hosting;


var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.ReportScenario_Api>("reportscenario-api");

var agent = builder.AddProject<Projects.ReportScenarioAgent>("reportscenario-agent")
.WithReference(api)
.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");



var tsAgent = builder.AddNpmApp("tsAgent", "../ReportScenario-TS", "dev:teamsfx")
.WithReference(api)
.WithHttpEndpoint(port: 3978, targetPort: 3978, isProxied: false);

// var authToken = builder
//     .AddParameter("ngrok-auth-token", builder.Configuration["ngrok-auth-token"], secret: true);

// builder.AddNgrok("ngrok") // omit endpointPort to use random port
//     .WithAuthToken(authToken)
// //     .WithTunnelEndpoint(agent, "http")
//     .WithTunnelEndpoint(tsAgent, "http");

builder.AddDevTunnel("agent")
       .WithReference(agent)
       .WithReference(tsAgent)
       .WithAnonymousAccess();


builder.Build().Run();
