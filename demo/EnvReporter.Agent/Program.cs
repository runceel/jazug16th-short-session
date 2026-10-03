#pragma warning disable GHCP001
using Azure.Core;
using Azure.Identity;
using EnvReporter.Agent;
using GitHub.Copilot;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.Options;

var builder = AgentHost.CreateBuilder(args);

builder.Services.AddOptions<FoundryModelOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ローカルでは azd の認証情報を使い、Hosted Agent では system-assigned managed identity を使う。
var isFoundryHosted = !string.IsNullOrEmpty(builder.Configuration["FOUNDRY_HOSTING_ENVIRONMENT"]);
builder.Services.AddSingleton<TokenCredential>(_ => isFoundryHosted
    ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
    : new AzureDeveloperCliCredential(new AzureDeveloperCliCredentialOptions
    {
        // AppHost から渡された Foundry のテナント ID を指定する。
        TenantId = builder.Configuration["AZURE_TENANT_ID"],
    }));
builder.Services.AddSingleton<ShellCommandPolicy>();

// DI コンテナーの終了時にクライアントを破棄し、Copilot runtime を停止する。
builder.Services.AddSingleton(sp => new CopilotClient(new CopilotClientOptions
{
    // BYOK で Foundry を使うため、GitHub サインインへのフォールバックを無効にする。
    UseLoggedInUser = false,
    Logger = sp.GetRequiredService<ILogger<CopilotClient>>(),
}));

// AddFoundryResponses() が DI から取得する、名前付きではない AIAgent を登録する。
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
        // シェルツールだけを公開し、実行前フックで許可リストを検査する。
        AvailableTools = [ShellCommandPolicy.ShellToolName],
        Hooks = new SessionHooks { OnPreToolUse = shellPolicy.OnPreToolUseAsync },
        OnPermissionRequest = PermissionHandler.ApproveAll,
        WorkingDirectory = Directory.CreateTempSubdirectory("env-reporter-").FullName,
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Append,
            Content = $"""
                ## 作業内容
                あなたは実行環境レポーターです。ユーザーに実行環境を聞かれたら、{ShellCommandPolicy.ShellToolName} ツールで次のコマンドだけを 1 つずつ実行して情報を集めてください。
                {string.Join(Environment.NewLine, ShellCommandPolicy.AllowedCommands.Select(c => $"- {c}"))}
                コマンドは上記と完全に一致する文字列で実行し、パイプや追加の引数は付けないでください。
                Microsoft Foundry 上での実行判定は、`printenv FOUNDRY_HOSTING_ENVIRONMENT`（Windows では `$env:FOUNDRY_HOSTING_ENVIRONMENT`）の標準出力だけを使ってください。出力が `1` なら「Microsoft Foundry 上で実行されています」と判定し、出力が空なら「環境変数が設定されていないため判定できません」としてください。それ以外の値も推測せず、値をそのまま示して判定できないと伝えてください。ツールの終了コードと標準出力の値を混同しないでください。
                結果をもとに、OS、CPU アーキテクチャ、ホスト名、.NET ランタイム、Microsoft Foundry 上で実行されているかどうかを日本語で簡潔にまとめてください。
                環境変数の値や資格情報は、上記コマンドで得たもの以外は推測しないでください。

                ## あなたのキャラ付け
                あなたは猫型エージェントです。
                猫らしく振舞うために語尾は「にゃん」にしてください。
                """,
        },
    };

    // CopilotClient から AIAgent を作成し、DI に登録
    return sp.GetRequiredService<CopilotClient>().AsAIAgent(
        sessionConfig,
        name: "env-reporter",
        description: "GitHub Copilot SDK agent");
});

builder.Services.AddFoundryResponses();
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();

// リクエスト受付前に runtime を起動・確認し、初回応答の遅延と起動後の継続失敗を防ぐ。
var copilotClient = app.App.Services.GetRequiredService<CopilotClient>();
await copilotClient.StartAsync();
var ping = await copilotClient.PingAsync("warmup");
app.App.Logger.LogInformation(
    "Copilot runtime is ready. Ping message: {Message}, server timestamp: {Timestamp}",
    ping.Message, ping.Timestamp);

app.Run();
