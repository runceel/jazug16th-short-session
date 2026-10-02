---
layout: title
size: large
theme: custom
theme-file: ./themes/ms-modern/theme.css
deck: JAZUG 16th Short Session
title: GitHub Copilot SDK × Microsoft Foundry
---

# GitHub Copilot SDK Agent を Foundry にデプロイ

**JAZUG 16th Short Session**<br/>
日本マイクロソフト<br/>
Kazuki Ota

<!--
目安: 0:30
Copilot SDKのagentハーネスは維持し、BYOKでMicrosoft Foundryのモデルdeploymentを推論先に指定します。Hosted Agentとして実行する構成とデモを紹介します。
-->

---
layout: center
size: large
---

## GitHub Copilot SDK とは

- GitHub Copilot の **agent harness** をアプリケーションに組み込む SDK
- session、会話履歴、tool call、ストリーミング応答を扱う
- 組み込みツールに加え、独自ツールや MCP server を接続できる
- 同じ runtime を **Copilot Studio、Excel、Outlook、PowerPoint、Word** などでも採用
- **対応言語：** TypeScript / Python / Go / .NET / Java / Rust

```architecture
{
  "version": 1,
  "title": "GitHub Copilot SDK の構成",
  "canvas": {
    "width": 1600,
    "height": 440
  },
  "elements": [
    {
      "type": "node",
      "id": "app",
      "x": 60,
      "y": 135,
      "width": 320,
      "height": 170,
      "text": "Apps\nServices",
      "icon": "browser",
      "style": {
        "fill": "surface",
        "stroke": "border",
        "fontSize": 28,
        "fontWeight": 700,
        "autoFit": "none"
      }
    },
    {
      "type": "node",
      "id": "sdk",
      "x": 530,
      "y": 105,
      "width": 440,
      "height": 230,
      "text": "GitHub Copilot SDK\nAgent harness",
      "icon": "component",
      "style": {
        "fill": "accentStrong",
        "stroke": "accentStrong",
        "textColor": "light",
        "fontSize": 28,
        "fontWeight": 700,
        "autoFit": "none"
      }
    },
    {
      "type": "node",
      "id": "model",
      "x": 1120,
      "y": 20,
      "width": 400,
      "height": 170,
      "text": "Model provider",
      "icon": "cloud",
      "style": {
        "fill": "surfaceInfo",
        "stroke": "borderInfo",
        "fontSize": 28,
        "fontWeight": 700,
        "autoFit": "none"
      }
    },
    {
      "type": "node",
      "id": "tools",
      "x": 1120,
      "y": 250,
      "width": 400,
      "height": 170,
      "text": "Tools / MCP",
      "icon": "api",
      "style": {
        "fill": "surfaceSuccess",
        "stroke": "borderSuccess",
        "fontSize": 28,
        "fontWeight": 700,
        "autoFit": "none"
      }
    },
    {
      "type": "connector",
      "from": "app",
      "to": "sdk",
      "arrow": true,
      "routing": "straight"
    },
    {
      "type": "connector",
      "from": "sdk",
      "to": "model",
      "arrow": true,
      "routing": "straight"
    },
    {
      "type": "connector",
      "from": "sdk",
      "to": "tools",
      "arrow": true,
      "routing": "straight"
    }
  ]
}
```

<!--
目安: 1:00
Copilot SDKは単なるモデル呼び出し用SDKではなく、sessionやtool実行を含むagent harnessをアプリケーションから利用するためのSDKです。同じCopilot agent runtimeは、GitHub製品に加えてCopilot Studio、Excel、Outlook、PowerPoint、Wordなども支えています。GitHub Blogによると、これらの多くは独自のagent loopをCopilot SDKへ置き換えています。TypeScript、Python、Go、.NET、Java、Rustの6言語に対応しています。
出典: https://github.blog/ai-and-ml/generative-ai/migrating-the-github-copilot-runtime-to-rust-using-copilot/
-->

---
layout: center
size: large
---

## GitHub Copilot 認証と Foundry BYOK

### GitHub Copilot 認証

```csharp
var config = new SessionConfig
{
    Model = "gpt-6-luna",
};
```

### BYOK：Microsoft Foundry

```csharp
var config = new SessionConfig
{
    Model = "gpt-6-luna",
    Provider = new ProviderConfig
    {
        Type = "openai",
        BaseUrl = "https://example-foundry.services.ai.azure.com/openai/v1/",
    },
};
```

**Foundry を使う場合は、モデル名に加えて接続先と認証方法を指定する。**

<!--
目安: 0:45
左はGitHub Copilot認証を使う通常の推論経路、右はBYOKの接続先にMicrosoft Foundryを使う今回の構成です。CopilotClientとSession APIは同じですが、Foundry BYOKではSessionConfigにProviderを追加し、modelにはFoundryのdeployment名を指定します。次のスライドでProviderConfigの中身を示します。
-->

---
layout: center
size: large
---

## Foundry ProviderConfig の詳細

```csharp
var tokenRequest = new TokenRequestContext(
    ["https://ai.azure.com/.default"]);

var config = new SessionConfig
{
    Model = "gpt-6-luna",
    Provider = new ProviderConfig
    {
        Type = "openai",
        BaseUrl = "https://example-foundry.services.ai.azure.com/openai/v1/",
        WireApi = "responses",
        BearerTokenProvider = async _ =>
            (await credential.GetTokenAsync(
                tokenRequest, CancellationToken.None)).Token,
    },
};
```

- `example-foundry`は説明用の架空のリソース名
- `Model`にはカタログ名ではなく **Foundryのdeployment名**を指定

<!--
目安: 0:45
FoundryのOpenAI互換endpointを使うため、provider typeはopenai、WireApiはresponsesを指定します。example-foundryは説明用の架空のリソース名です。BearerTokenProviderは実行時のidentityからEntra tokenを取得します。固定tokenをソースコードやコンテナーイメージに保存しません。
-->

---
layout: center
size: large
---

## ローカルから Hosted Agent へ

| コンポーネント | このデモでの役割 |
|---|---|
| GitHub Copilot SDK | session、tool、agent harness |
| BYOK provider 設定 | Foundry endpoint と deployment 名を指定 |
| Microsoft Agent Framework | SDK agent を `AIAgent` として構成 |
| Foundry Hosted Agent | agent container と endpoint を管理 |
| Aspire | Foundry リソースと agent のデプロイを記述 |

推論要求は Copilot SDK から Foundry model endpoint へ送る。

<!--
目安: 1:00
ここまで説明したCopilot SDK agentを、ローカル実行からFoundry Hosted Agentへ移します。SDKのBYOK設定は推論先、Microsoft Agent Frameworkはagentの抽象化とHosted Agent adapter、AspireはAzureリソースとデプロイを担当します。
-->

---
layout: center
size: large
---

## DEMO 1｜ローカル実行

**質問：** 「実行環境を教えて」

1. Foundry deployment を指定した Copilot SDK agent に入力
2. agent がシェル実行ツールを選択して環境を確認
3. ツールの実行結果を用いて回答

**ツール実行の許可対象：** デモに必要な操作に限定

<!--
目安: 2:15（説明0:30 + デモ1:45）
同じFoundry deploymentを推論先にした状態で、組み込みシェルツールを使う流れを示します。ローカルの認証は開発用Entra IDまたはAPI keyを使い、資格情報はソースやイメージに含めません。
-->

---
layout: center
size: large
---

## DEMO 2｜Aspire で Azure にデプロイ

```text
Copilot SDK agent
  └─ BYOK: Foundry endpoint + deployment 名
       ↓ Microsoft Agent Framework
Hosted Agent adapter
       ↓ Aspire で構成・デプロイ
Microsoft Foundry Hosted Agent
```

<!--
目安: 1:15
Aspireを使った構成とデプロイを示します。AspireはBYOKのprovider設定、実行時の認証トークン供給、ツールの安全性を自動で決めるものではありません。デプロイ済み環境を使う場合は設定箇所だけ短く説明します。
-->

---
layout: center
size: large
---

## Hosted Agent の実行とモデル認証

- Foundry Agent Service が agent container と endpoint を管理
- BYOK token provider が使う実行 principal を特定
- keyless 認証では `https://ai.azure.com/.default` の Entra token を使用
- endpoint と deployment に対応する推論 RBAC を確認

<!--
目安: 1:00
デプロイ後の実行では二つの認証を区別します。クライアントからHosted Agent endpointへの認証と、Hosted Agent内のBYOK providerからモデルendpointへの認証です。後者はtoken providerが実際に使うprincipalを確認します。Foundry projectのmanaged identityにproject endpoint用のFoundry Userロールがあることは、BYOKの直接endpoint呼び出しに使う別principalの権限を意味しません。`/openai/v1/` のkeyless推論ではscopeは `https://ai.azure.com/.default` です。必要なロールはモデルとendpointにより異なり、OpenAIモデル専用なら `Cognitive Services OpenAI User`、より広いFoundryモデルの推論では `Cognitive Services User` または `Foundry User` が候補です。選択deploymentの要件を確認します。
-->

---
layout: center
size: large
---

## DEMO 2｜Hosted Agent の実行

- Hosted Agent endpoint にリクエスト
- ローカルと同じ Foundry deployment による応答を確認
- session 実行と tool call を確認

<!--
目安: 1:30
デプロイしたagentにローカルと同じ入力を与え、推論先がFoundry deploymentであること、ツール実行と応答を確認します。デプロイ操作に時間がかかる場合は、事前にデプロイしたendpointを使います。
-->

---
layout: center
size: large
---

## まとめ

- Copilot SDK の BYOK で Foundry model deployment を推論先に指定
- Microsoft Agent Framework で SDK agent を構成
- Aspire を用いて Foundry Hosted Agent としてデプロイ

<!--
目安: 0:45
Copilot SDKのagent harnessを維持しながら、推論先をMicrosoft Foundryのmodel deploymentとして明示できます。ローカルとHosted Agentの両方で、deployment名、endpoint、実行IDの権限を確認することが重要です。全体で約10分です。
-->
