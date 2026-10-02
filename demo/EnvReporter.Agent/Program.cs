using Azure.AI.AgentServer.Core;
using Azure.Core;
using Azure.Identity;
using EnvReporter.Agent;
using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.GitHub.Copilot;
using Microsoft.Extensions.Options;

var builder = AgentHost.CreateBuilder(args);

builder.Services.AddOptions<FoundryModelOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Keyless (Microsoft Entra ID) authentication to the Foundry model endpoint.
// Locally this is the developer's Azure sign-in; in Foundry it is the identity available to the hosted container.
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton<ShellCommandPolicy>();

// The container owns the client and disposes it (stopping the Copilot runtime) on shutdown.
builder.Services.AddSingleton(sp => new CopilotClient(new CopilotClientOptions
{
    // BYOK: inference goes to Foundry, so the runtime must not fall back to a GitHub sign-in.
    UseLoggedInUser = false,
    Logger = sp.GetRequiredService<ILogger<CopilotClient>>(),
}));

// AddFoundryResponses() without an agent instance resolves this non-keyed AIAgent from DI.
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var model = sp.GetRequiredService<IOptions<FoundryModelOptions>>().Value;
    var credential = sp.GetRequiredService<TokenCredential>();
    var shellPolicy = sp.GetRequiredService<ShellCommandPolicy>();
    var tokenRequest = new TokenRequestContext(["https://ai.azure.com/.default"]);

    var sessionConfig = new SessionConfig
    {
        Model = model.DeploymentName,
        Provider = new ProviderConfig
        {
            Type = "openai",
            BaseUrl = new Uri(model.Endpoint!, "openai/v1/").ToString(),
            WireApi = "responses",
            // BearerTokenProvider is marked experimental (GHCP001) in GitHub.Copilot.SDK 1.0.16.
#pragma warning disable GHCP001
            BearerTokenProvider = async _ => (await credential.GetTokenAsync(tokenRequest, CancellationToken.None)).Token,
#pragma warning restore GHCP001
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
                結果をもとに、OS、CPU アーキテクチャ、ホスト名、.NET ランタイム、Microsoft Foundry 上で実行されているかどうかを日本語で簡潔にまとめてください。
                環境変数の値や資格情報は、上記コマンドで得たもの以外は推測しないでください。
                """,
        },
    };

    return new GitHubCopilotAgent(
        sp.GetRequiredService<CopilotClient>(),
        sessionConfig,
        ownsClient: false,
        name: "env-reporter",
        description: "GitHub Copilot SDK agent that reports the runtime environment using a Microsoft Foundry model deployment.",
        loggerFactory: sp.GetRequiredService<ILoggerFactory>());
});

builder.Services.AddFoundryResponses();
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
