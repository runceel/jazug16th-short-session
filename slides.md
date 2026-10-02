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
    // カタログ名ではなく Foundry の deployment 名を指定
    Model = "gpt-6-luna",
    Provider = new ProviderConfig
    {
        Type = "openai",
        // example-foundry は説明用の架空のリソース名
        BaseUrl = "https://example-foundry.services.ai.azure.com/openai/v1/",
        WireApi = "responses",
        BearerTokenProvider = async _ =>
            (await credential.GetTokenAsync(
                tokenRequest, CancellationToken.None)).Token,
    },
};
```

<!--
目安: 0:45
FoundryのOpenAI互換endpointを使うため、provider typeはopenai、WireApiはresponsesを指定します。example-foundryは説明用の架空のリソース名です。BearerTokenProviderは実行時のidentityからEntra tokenを取得します。固定tokenをソースコードやコンテナーイメージに保存しません。
-->

---
layout: center
size: large
---

## デモアプリの構成

| コンポーネント | 役割 |
|---|---|
| GitHub Copilot SDK | session と tool 実行。BYOK で Foundry deployment に推論要求を送信 |
| Microsoft Agent Framework | `GitHubCopilotAgent` で SDK agent を `AIAgent` として扱い、Hosted Agent の Responses プロトコルで公開 |
| Aspire AppHost | Foundry project、model deployment、Hosted Agent を定義 |

`aspire run` ではローカルで、`aspire deploy` では Foundry Hosted Agent として同じ agent を起動する。

<!--
目安: 1:00
デモアプリは三つの要素で構成しています。Copilot SDKは前のスライドで示したBYOK設定でFoundry deploymentに推論要求を送ります。Microsoft Agent FrameworkはCopilot SDK agentをAIAgentとして扱い、Foundry Hosted AgentのResponsesプロトコルで公開します。Aspire AppHostはFoundry project、model deployment、Hosted Agentを定義します。同じAppHostを、ローカル実行とAzureへのデプロイの両方に使います。
-->

---
layout: center
size: large
---

## DEMO 1｜ローカル実行

**質問：** 「実行環境を教えて」

1. `aspire run` で起動した agent の `/responses` に入力
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

| 項目 | DEMO 1（`aspire run`） | DEMO 2（`aspire deploy`） |
|---|---|---|
| agent のコードと AppHost | 共通 | 共通 |
| 実行場所 | 開発 PC 上のプロセス | Foundry が管理する Linux コンテナー |
| 受信 endpoint | ローカルの `/responses` | Hosted Agent endpoint |
| 推論先 | Foundry deployment | 同じ deployment |
| モデル呼び出しの ID | サインイン中のユーザー | Hosted Agent の agent identity |

`aspire deploy` は、コンテナーイメージを ACR に push し、Foundry project に Hosted Agent を登録する。

<!--
目安: 1:15
DEMO 1と同じAppHostをaspire deployで実行します。Aspireはagentのコンテナーイメージをビルドして ACR に push し、Foundry projectにHosted Agentを登録します。ローカル実行との違いは実行場所、受信endpoint、モデル呼び出しに使うIDです。AspireはBYOKのprovider設定、実行時の認証トークン供給、ツールの安全性を自動で決めるものではありません。デプロイ済み環境を使う場合は設定箇所だけ短く説明します。
-->

---
layout: center
size: large
---

## Hosted Agent の 2 つの認証

デプロイ後は、**誰が・どこに**アクセスするかで認証を分けて考える

| 区間 | 認証する ID | 設定すること |
|---|---|---|
| ① クライアント → Hosted Agent | 呼び出し元のユーザー / アプリ | endpoint へのアクセス権 |
| ② Hosted Agent 内の Copilot SDK → モデル | Hosted Agent の agent identity | 推論用の RBAC ロール |

**② はローカル実行時（サインイン中のユーザー）と ID が変わるため、ロール付与を忘れると推論が失敗する**

<!--
目安: 1:00
デプロイ後の実行では二つの認証を区別します。①クライアントからHosted Agent endpointへの認証と、②Hosted Agent内のBYOK providerからモデルendpointへの認証です。②はローカルではサインイン中のユーザーでしたが、Hosted Agentではagent identityに変わるため、token providerが実際に使うprincipalを確認します。Foundry projectのmanaged identityにproject endpoint用のFoundry Userロールがあることは、BYOKの直接endpoint呼び出しに使う別principalの権限を意味しません。`/openai/v1/` のkeyless推論ではscopeは `https://ai.azure.com/.default` です。必要なロールはモデルとendpointにより異なり、OpenAIモデル専用なら `Cognitive Services OpenAI User`、より広いFoundryモデルの推論では `Cognitive Services User` または `Foundry User` が候補です。選択deploymentの要件を確認します。
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
size: xlarge
---

## まとめ

- **GitHub Copilot SDK**
  - コーディング用途に限らず、**汎用的な Agent** の基盤として使われ始めている
  - BYOK で Foundry deployment を推論先にできる
- **Microsoft Agent Framework**
  - `GitHubCopilotAgent` で **Copilot SDK にも対応**
  - SDK agent を `AIAgent` として扱える
- **Aspire**
  - 同じ AppHost で `aspire run` はローカル実行、`aspire deploy` で **Foundry Hosted Agent に簡単にデプロイ**

<!--
目安: 0:45
まとめです。GitHub Copilot SDKはCopilotのagent harnessをそのまま組み込めるSDKで、コーディング用途に限らず、Copilot StudioやOfficeアプリなど汎用的なAgentの基盤として使われ始めています。BYOKでMicrosoft Foundryのmodel deploymentを推論先に指定できます。Microsoft Agent FrameworkはGitHubCopilotAgentでCopilot SDKにも対応しており、SDK agentをAIAgentとして扱い、Hosted AgentのResponsesプロトコルで公開できます。そしてAspireを使うと、同じAppHostでローカル実行とFoundry Hosted Agentへのデプロイを簡単に行えます。デプロイ後は、モデル呼び出しに使う実行IDの権限を確認することが重要です。全体で約10分です。
-->
