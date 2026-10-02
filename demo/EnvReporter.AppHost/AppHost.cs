var builder = DistributedApplication.CreateBuilder(args);

var foundry = builder.AddFoundry("foundry");
var project = foundry.AddProject("env-reporter-project");

// gpt-6-luna is not in the generated FoundryModel descriptors yet, so specify name, version, and format explicitly.
// The default capacity (1K TPM) is too small for an agent that makes tool calls.
var chat = project.AddModelDeployment("chat", "gpt-6-luna", "2026-09-22", "OpenAI")
    .WithProperties(deployment =>
    {
        deployment.SkuName = "GlobalStandard";
        deployment.SkuCapacity = 50;
    });

var agent = builder.AddProject<Projects.EnvReporter_Agent>("env-reporter")
    .WithReference(chat).WaitFor(chat)
    .AsHostedAgent(project);

// Locally, DefaultAzureCredential may pick a developer sign-in (e.g. Visual Studio) from another tenant.
// Pin it to the tenant that Aspire provisions the Foundry resources into.
if (builder.ExecutionContext.IsRunMode && builder.Configuration["Azure:TenantId"] is { Length: > 0 } tenantId)
{
    agent.WithEnvironment("AZURE_TENANT_ID", tenantId);
}

builder.Build().Run();
