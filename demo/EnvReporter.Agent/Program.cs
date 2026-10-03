#pragma warning disable GHCP001
using Azure.Core;
using Azure.Identity;
using EnvReporter.Agent;
using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.Options;

var builder = AgentHost.CreateBuilder(args);

builder.Services.AddOptions<FoundryModelOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Keyless (Microsoft Entra ID) authentication to the Foundry model endpoint.
// Foundry sets FOUNDRY_HOSTING_ENVIRONMENT in the hosted container, where the agent identity is
// available through the system-assigned managed identity endpoint. Locally, use the azd sign-in.
var isFoundryHosted = !string.IsNullOrEmpty(builder.Configuration["FOUNDRY_HOSTING_ENVIRONMENT"]);
builder.Services.AddSingleton<TokenCredential>(_ => isFoundryHosted
    ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
    : new AzureDeveloperCliCredential(new AzureDeveloperCliCredentialOptions
    {
        // AppHost passes the tenant that Aspire provisions the Foundry resources into.
        TenantId = builder.Configuration["AZURE_TENANT_ID"],
    }));
builder.Services.AddSingleton<ShellCommandPolicy>();

// The container owns the client and disposes it (stopping the Copilot runtime) on shutdown.
builder.Services.AddSingleton(sp => new CopilotClient(new CopilotClientOptions
{
    // BYOK: inference goes to Foundry, so the runtime must not fall back to a GitHub sign-in.
    UseLoggedInUser = false,
    Logger = sp.GetRequiredService<ILogger<CopilotClient>>(),
}));

// AddFoundryResponses() without an agent instance resolves this non-keyed AIAgent from DI.
builder.Services.AddSingleton(sp =>
{
    var model = sp.GetRequiredService<IOptions<FoundryModelOptions>>().Value;
    var credential = sp.GetRequiredService<TokenCredential>();
    var shellPolicy = sp.GetRequiredService<ShellCommandPolicy>();
    var tokenRequest = new TokenRequestContext(["https://ai.azure.com/.default"]);

    var sessionConfig = new SessionConfig
    {
        Model = model.DeploymentName,
        Provider = new()
        {
            Type = "openai",
            BaseUrl = new Uri(model.Endpoint!, "openai/v1/").ToString(),
            WireApi = "responses",
            BearerTokenProvider = async _ => 
                (await credential.GetTokenAsync(tokenRequest, CancellationToken.None)).Token,
        },
        // Only the shell tool is exposed, and every call is checked against the allowlist by the pre-tool hook.
        AvailableTools = [ShellCommandPolicy.ShellToolName],
        Hooks = new SessionHooks { OnPreToolUse = shellPolicy.OnPreToolUseAsync },
        OnPermissionRequest = PermissionHandler.ApproveAll,
        WorkingDirectory = Directory.CreateTempSubdirectory("env-reporter-").FullName,
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Append,
            Content = $"""
                あなたは実行環境レポーターです。ユーザーに実行環境を聞かれたら、{ShellCommandPolicy.ShellToolName} ツールで次のコマンドだけを 1 つずつ実行して情報を集めてください。
                {string.Join(Environment.NewLine, ShellCommandPolicy.AllowedCommands.Select(c => $"- {c}"))}
                コマンドは上記と完全に一致する文字列で実行し、パイプや追加の引数は付けないでください。
                Microsoft Foundry 上での実行判定は、`printenv FOUNDRY_HOSTING_ENVIRONMENT`（Windows では `$env:FOUNDRY_HOSTING_ENVIRONMENT`）の標準出力だけを使ってください。出力が `1` なら「Microsoft Foundry 上で実行されています」と判定し、出力が空なら「環境変数が設定されていないため判定できません」としてください。それ以外の値も推測せず、値をそのまま示して判定できないと伝えてください。ツールの終了コードと標準出力の値を混同しないでください。
                結果をもとに、OS、CPU アーキテクチャ、ホスト名、.NET ランタイム、Microsoft Foundry 上で実行されているかどうかを日本語で簡潔にまとめてください。
                環境変数の値や資格情報は、上記コマンドで得たもの以外は推測しないでください。
                """,
        },
    };

    return sp.GetRequiredService<CopilotClient>().AsAIAgent(
        sessionConfig,
        name: "env-reporter",
        description: "GitHub Copilot SDK agent that reports the runtime environment using a Microsoft Foundry model deployment.");
});

builder.Services.AddFoundryResponses();
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();

// Warm up before listening: the first request no longer pays the Copilot runtime startup cost,
// and a startup failure stops the process instead of being cached and failing every request.
var copilotClient = app.App.Services.GetRequiredService<CopilotClient>();
await copilotClient.StartAsync();
var ping = await copilotClient.PingAsync("warmup");
app.App.Logger.LogInformation(
    "Copilot runtime is ready. Ping message: {Message}, server timestamp: {Timestamp}",
    ping.Message, ping.Timestamp);

app.Run();
