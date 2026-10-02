# 実行環境レポーター デモ

GitHub Copilot SDK で作った agent を、Microsoft Foundry の Hosted Agent として動かすデモです。モデル推論は Copilot SDK の BYOK 設定で、Foundry の model deployment（`gpt-6-luna`）に直接送ります。

「実行環境を教えて」と聞くと、agent は許可リストに入っているシェルコマンドだけを実行して、OS・CPU アーキテクチャ・.NET ランタイム・Foundry 上で動いているかどうかを答えます。ローカル（Windows）と Hosted Agent（Linux コンテナー）で回答が変わるので、同じコードがクラウドで動いていることを示せます。

## 構成

| プロジェクト | 役割 |
|---|---|
| [EnvReporter.AppHost](./EnvReporter.AppHost/AppHost.cs) | Aspire AppHost。Foundry アカウント、project、`gpt-6-luna` の deployment、Hosted Agent を定義 |
| [EnvReporter.Agent](./EnvReporter.Agent/Program.cs) | Copilot SDK + Microsoft Agent Framework の agent。DI に登録し、Foundry Hosted Agent の Responses プロトコルで公開 |
| [EnvReporter.Web](./EnvReporter.Web/Components/Pages/Home.razor) | Agent に質問する Blazor Server 画面。ローカルは HTTP、クラウドは注入された HTTPS Hosted Agent endpoint に接続 |
| [ServiceDefaults](./ServiceDefaults/Extensions.cs) | Web のヘルスチェック、サービス検出、HTTP resilience、OpenTelemetry を共通構成 |
| [EnvReporter.Agent/FoundryModelOptions.cs](./EnvReporter.Agent/FoundryModelOptions.cs) | Aspire が注入する `CHAT_URI` / `CHAT_MODELNAME` を Options としてバインドし、起動時に検証 |
| [EnvReporter.Agent/ShellCommandPolicy.cs](./EnvReporter.Agent/ShellCommandPolicy.cs) | `SessionHooks.OnPreToolUse` フックで、シェルツールを固定の読み取り専用コマンドに制限 |

Agent は Aspire の ServiceDefaults を使いません。`AgentHost.CreateBuilder` が `Microsoft.OpenTelemetry` で OpenTelemetry を自動構成し、`OTEL_EXPORTER_OTLP_ENDPOINT`（Aspire ダッシュボード）や `APPLICATIONINSIGHTS_CONNECTION_STRING`（Foundry）があれば送信先も自動で設定します。ServiceDefaults の `UseOtlpExporter` を併用すると、OTLP exporter の二重登録で起動時に例外になります。

Web 画面から送信した要求は、次の経路で Aspire Dashboard に表示されます。

```text
Blazor Web (ServiceDefaults)
  └─ POST /responses（ローカル）または Hosted Agent Responses endpoint（クラウド）
       └─ env-reporter（AgentHost の OpenTelemetry）
            └─ Foundry model deployment
```

Web 画面内にも `agent.responses` のカスタム span を作成しているため、Dashboard の Traces で画面側の処理時間と Agent への HTTP 呼び出しを確認できます。プロンプトや応答本文はトレース属性に記録しません。

```text
ブラウザー
  │ Entra ID（Azure Container Apps Easy Auth）
  ▼
Blazor Web（Azure Container Apps）
  │ HTTPS + Web の managed identity
  │ POST .../endpoint/protocols/openai/responses?api-version=v1
  ▼
Foundry Hosted Agent（EnvReporter.Agent コンテナー）
  └─ Microsoft Agent Framework: GitHubCopilotAgent
       └─ GitHub Copilot SDK（同梱の Copilot CLI ランタイム）
            ├─ BYOK provider ── Entra token ── Foundry /openai/v1/（deployment: chat = gpt-6-luna）
            └─ bash / powershell ツール ── ShellCommandPolicy の許可リスト
```

## 使用パッケージ

| パッケージ | バージョン |
|---|---|
| GitHub.Copilot.SDK | 1.0.16 |
| Microsoft.Agents.AI.GitHub.Copilot | 1.23.0 |
| Microsoft.Agents.AI.Foundry.Hosting | 1.23.0-preview.260928.1 |
| Microsoft.Extensions.AI.OpenAI | 10.10.1 |
| Azure.Identity | 1.21.0 |
| Aspire.AppHost.Sdk | 13.6.0 |
| Aspire.Hosting.Azure.AppContainers | 13.6.0 |
| Aspire.Hosting.Azure.ContainerRegistry | 13.6.0 |
| Aspire.Hosting.Foundry | 13.6.0-preview.1.26479.8 |

## 前提条件

- .NET 10 SDK
- Aspire CLI 13.6 以降（`aspire update --self` で更新）
- Docker（`aspire deploy` でのイメージのビルドと push に使用）
- Azure サブスクリプションと、リソースを作成できる権限
- `gpt-6-luna`（`GlobalStandard`）を利用できるリージョンとクォータ。容量は [AppHost.cs](./EnvReporter.AppHost/AppHost.cs) で 50K TPM に設定しています
- `az login` などで `DefaultAzureCredential` が使える状態

GitHub Copilot のサインインは使いません。推論はすべて BYOK で Foundry に送ります。Copilot CLI ランタイムは `GitHub.Copilot.SDK` がビルド時に取得し、出力とコンテナーに同梱します。

## ローカルで実行する（DEMO 1）

```powershell
aspire run
```

起動後、Aspire Dashboard の `env-reporter-web` のエンドポイントを開いて Agent テスト画面を表示します。画面の **Agent に送信** を押すと、Web と Agent の両方のトレースが Dashboard に送られます。

初回は、Aspire がサブスクリプション、リージョン、リソースグループの入力を求め、Azure 上に Foundry アカウント、project、deployment を作成します。agent 自体はローカルで動き、推論だけを Foundry に送ります。

Aspire ダッシュボードで `env-reporter` の **Send Message** を使うか、次のようにリクエストを送ります。ポートはダッシュボードで確認してください。

```powershell
$body = @{ input = '実行環境を教えて'; stream = $false } | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri http://localhost:8088/responses -ContentType 'application/json' -Body $body
```

ローカルの推論にはサインイン中のユーザーの Entra token を使います。`401` が返る場合は、そのユーザーに Foundry アカウントの `Cognitive Services OpenAI User` が割り当てられているかを確認してください。

`DefaultAzureCredential` は Azure CLI より先に Visual Studio などの資格情報を試すため、別テナントのサインインが使われると `Token tenant ... does not match resource tenant` で失敗します。これを避けるため、AppHost はローカル実行時だけ、ユーザーシークレットの `Azure:TenantId` を `AZURE_TENANT_ID` として Agent に渡します。

## Azure にデプロイする（DEMO 2）

`aspire deploy` は `Production` 環境で動くため、AppHost のユーザーシークレットは読み込まれません。接続先は環境変数で渡します。また、イメージのビルドと ACR への push に Docker（Docker Desktop など）が必要です。

```powershell
$env:Azure__TenantId = '<テナント ID>'
$env:Azure__SubscriptionId = '<サブスクリプション ID>'
$env:Azure__Location = 'japaneast'
$env:Azure__ResourceGroup = '<リソースグループ名>'
$env:Azure__CredentialSource = 'AzureCli'
$env:Parameters__existingAcrName = '<既存 ACR 名>'
$env:Parameters__existingAcrResourceGroup = '<既存 ACR のリソースグループ>'

# 初回は private ingress のままデプロイする。
$env:Foundry__ExposeWebIngress = 'false'
aspire deploy
```

Aspire が agent と Web のコンテナーイメージ（`linux/amd64`）を作成して既存 ACR に push し、Foundry project に Hosted Agent `env-reporter-ha`、Azure Container Apps に `env-reporter-web` をデプロイします。初回の Web ingress は private です。Web から Hosted Agent へは、Web の managed identity で `https://ai.azure.com/.default` の token を取得し、HTTPS Responses endpoint を呼び出します。ARM64 の PC でも、linux-x64 版の Copilot CLI がイメージに同梱されます。

### Entra ID Easy Auth を設定して Web を公開する

Web を外部公開する前に、Container Apps Easy Auth を構成します。次の例は同一テナントの単一テナント app registration を作成し、credential を Container App secret として保存します。credential の有効期限はテナントのポリシーに合わせてください。secret の値をログやソース管理に出さないでください。

```powershell
$envId = az containerapp show -n env-reporter-web -g $env:Azure__ResourceGroup --query properties.environmentId -o tsv
$domain = az containerapp env show --ids $envId --query properties.defaultDomain -o tsv
$webUrl = "https://env-reporter-web.$domain"
$callbackUri = "$webUrl/.auth/login/aad/callback"
$appName = 'env-reporter-web-auth'

$clientId = az ad app create --display-name $appName --sign-in-audience AzureADMyOrg `
  --web-home-page-url $webUrl --web-redirect-uris $callbackUri --query appId -o tsv
az ad app update --id $clientId --enable-id-token-issuance true --output none
$credentialEnd = (Get-Date).ToUniversalTime().AddDays(30).ToString('yyyy-MM-ddTHH:mm:ssZ')
$credential = az ad app credential reset --id $clientId --display-name 'Container Apps Easy Auth' `
  --end-date $credentialEnd --output json | ConvertFrom-Json
az ad sp create --id $clientId --output none

$clientSecret = $credential.password
az containerapp secret set -n env-reporter-web -g $env:Azure__ResourceGroup `
  --secrets "easyauth-secret=$clientSecret" --output none
az containerapp auth microsoft update -n env-reporter-web -g $env:Azure__ResourceGroup `
  --client-id $clientId --client-secret-name easyauth-secret --tenant-id $env:Azure__TenantId --yes --output none
az containerapp auth update -n env-reporter-web -g $env:Azure__ResourceGroup `
  --enabled true --action RedirectToLoginPage --redirect-provider AzureActiveDirectory `
  --require-https true --token-store false --yes --output none
$clientSecret = $null
$credential = $null

# Easy Auth を確認した後で外部 HTTPS ingress を有効にする。
$env:Foundry__ExposeWebIngress = 'true'
aspire deploy
```

再デプロイ後、`$webUrl` をブラウザーで開いて Entra ID にサインインし、画面から「実行環境を日本語で教えてください」を送信します。初回は組織の consent policy によりユーザー同意または管理者承認が必要な場合があります。Web → Hosted Agent の認証は Web の managed identity、ブラウザー → Web の認証は Easy Auth が担当します。

### Hosted Agent endpoint を直接確認する（任意）

Web UI を経由せず、Hosted Agent の endpoint に同じ質問を送る場合は次のようにします。

```powershell
$base = 'https://<Foundry アカウント名>.services.ai.azure.com/api/projects/env-reporter-project'
$token = az account get-access-token --scope https://ai.azure.com/.default --query accessToken -o tsv
$body = @{ input = '実行環境を教えて'; stream = $false } | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri "$base/agents/env-reporter-ha/endpoint/protocols/openai/responses?api-version=v1" `
  -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json; charset=utf-8' `
  -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

Aspire が生成する Web Container App は `minReplicas: 1` で稼働するため、デプロイ中は継続的な利用料金が発生する場合があります。

### コンテナーを root で実行している理由

Hosted Agent は、セッションごとの永続ストレージを `$HOME`（`/home/session`）にマウントします。.NET SDK のコンテナー発行は既定で非 root ユーザー（UID 1654）を使いますが、このユーザーでは `$HOME` に書き込めず、Copilot CLI がセッション作成時に `EACCES: Permission denied` で失敗しました。そのため [EnvReporter.Agent.csproj](./EnvReporter.Agent/EnvReporter.Agent.csproj) で `ContainerUser` を `root` にしています。Foundry 公式の Hosted Agent サンプルの Dockerfile も root で実行します。

### 推論用の権限

この agent は、BYOK で Foundry アカウントの `/openai/v1/` endpoint を直接呼び出します。project endpoint 経由ではありません。Microsoft Learn の [Hosted agent permissions reference](https://learn.microsoft.com/azure/foundry/agents/concepts/hosted-agent-permissions) では、この場合、agent identity にアカウントスコープのロールが必要とされています。

Aspire 13.6 の `aspire deploy` は、Hosted Agent の作成後に agent identity へ Foundry アカウントスコープの `Foundry User` を自動で割り当てます（デプロイログの `Assigned Foundry User role to hosted agent ...`）。手動の割り当ては不要でした。応答が `401` / `PermissionDenied` になる場合は、次のコマンドで agent identity のロールを確認してください。

```powershell
az role assignment list --assignee <agent identity のプリンシパル ID> --all --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

ロールの反映には最大 5 分ほどかかります。

### ログを確認する

Hosted Agent のコンテナーログは、セッション単位で取得できます。セッション ID は、応答の `agent_session_id` です。

```powershell
curl.exe -s -N -m 30 -H "Authorization: Bearer $token" -H "Accept: text/event-stream" `
  "$base/agents/env-reporter-ha/versions/<バージョン>/sessions/<セッション ID>:logstream?api-version=v1"
```

## 許可しているコマンド

[ShellCommandPolicy.cs](./EnvReporter.Agent/ShellCommandPolicy.cs) で、次のコマンドと完全に一致する場合だけ実行を許可します。それ以外のツール呼び出しとコマンドは、すべて実行前に拒否します。

| Linux（bash） | Windows（powershell） |
|---|---|
| `hostname` | `hostname` |
| `uname -a` | `[System.Runtime.InteropServices.RuntimeInformation]::OSDescription` |
| `cat /etc/os-release` | `[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture` |
| `nproc` | `[System.Environment]::ProcessorCount` |
| `free -h` | `dotnet --info` |
| `dotnet --info` | `$env:FOUNDRY_HOSTING_ENVIRONMENT` |
| `printenv FOUNDRY_HOSTING_ENVIRONMENT` | |

Foundry 実行環境の判定は Agent がコマンドの標準出力を解釈します。`FOUNDRY_HOSTING_ENVIRONMENT` の出力が `1` なら Foundry 上、空なら未設定で判定不能、それ以外は値を示して判定不能と答えるよう system message で指示しています。ツールの終了コードと標準出力を混同しないよう明示しています。

## 注意事項

- `ProviderConfig.BearerTokenProvider` は、GitHub.Copilot.SDK 1.0.16 では評価用 API（`GHCP001`）です。[Program.cs](./EnvReporter.Agent/Program.cs) では、該当箇所だけ警告を抑制しています。
- `gpt-6-luna` は、Aspire 13.6 preview の `FoundryModel` 記述子にまだありません。そのため、モデル名・バージョン（`2026-09-22`）・形式を文字列で指定しています。
- Aspire CLI が AppHost SDK（13.6.0）より古いと、`aspire publish` や `aspire deploy` が失敗することがあります。先に CLI を更新してください。
