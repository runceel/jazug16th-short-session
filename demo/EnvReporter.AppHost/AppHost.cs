#pragma warning disable ASPIRECOMPUTE003
var builder = DistributedApplication.CreateBuilder(args);

var foundry = builder.AddFoundry("foundry");
var project = foundry.AddProject("env-reporter-project");

// FoundryModel に gpt-6-luna の記述子がないため、モデル名・バージョン・形式を明示する。
// tool call を使う agent 向けに、既定値より大きい容量を設定する。
var chat = project.AddModelDeployment("chat", "gpt-6-luna", "2026-09-22", "OpenAI")
    .WithProperties(deployment =>
    {
        deployment.SkuName = "GlobalStandard";
        deployment.SkuCapacity = 50;
    });

var agent = builder.AddProject<Projects.EnvReporter_Agent>("env-reporter")
    .WithReference(chat).WaitFor(chat)
    .AsHostedAgent(project);

// Agent の endpoint を共有し、Agent の起動後に Web 画面を開始する。
var web = builder.AddProject<Projects.EnvReporter_Web>("env-reporter-web")
    .WithReference(agent)
    .WaitFor(agent);

if (!builder.ExecutionContext.IsRunMode)
{
    var registryName = builder.AddParameter("existingAcrName");
    var registryResourceGroup = builder.AddParameter("existingAcrResourceGroup");
    var registry = builder.AddAzureContainerRegistry("env-reporter-web-acr")
        .PublishAsExisting(registryName, registryResourceGroup);
    var containerApps = builder.AddAzureContainerAppEnvironment("env-reporter-web-env")
        .WithAzureContainerRegistry(registry);

    web.WithContainerRegistry(registry);
    var cloudWeb = web.WithComputeEnvironment(containerApps)
        .PublishAsAzureContainerApp((_, _) => { })
        .WithEnvironment("Foundry__UseEntraAuthentication", "true");

    if (bool.TryParse(builder.Configuration["Foundry:ExposeWebIngress"], out var exposeWebIngress) && exposeWebIngress)
    {
        cloudWeb.WithExternalHttpEndpoints();
    }
}

// ローカル実行では AzureDeveloperCliCredential に Aspire のテナント ID を明示する。
if (builder.ExecutionContext.IsRunMode && builder.Configuration["Azure:TenantId"] is { Length: > 0 } tenantId)
{
    agent.WithEnvironment("AZURE_TENANT_ID", tenantId);
}

builder.Build().Run();
