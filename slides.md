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

---
layout: center
size: xlarge
---

## 自己紹介

```adaptive-card
{
  "type": "AdaptiveCard",
  "version": "1.5",
  "body": [
    {
      "type": "ColumnSet",
      "columns": [
        {
          "type": "Column",
          "width": "stretch",
          "verticalContentAlignment": "Center",
          "items": [
            {
              "type": "TextBlock",
              "text": "大田 一希 (Kazuki Ota)",
              "size": "ExtraLarge",
              "weight": "Bolder",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "Microsoft",
              "size": "Large",
              "isSubtle": true,
              "spacing": "Small",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "Cloud Solution Architect & Evangelist",
              "size": "Large",
              "isSubtle": true,
              "spacing": "None",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "**X**: @okazuki",
              "size": "Large",
              "spacing": "ExtraLarge",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "**zenn**: zenn.dev/okazuki",
              "size": "Large",
              "spacing": "Small",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "**好き**: C# / GitHub Copilot",
              "size": "Large",
              "spacing": "Small",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "**マイブーム**: Markdown プレゼンの探求",
              "size": "Large",
              "spacing": "Small",
              "wrap": true
            }
          ]
        },
        {
          "type": "Column",
          "width": "auto",
          "verticalContentAlignment": "Center",
          "items": [
            {
              "type": "Image",
              "url": "assets/profile-square.jpg",
              "altText": "Kazuki Ota",
              "style": "Person",
              "width": "320px",
              "height": "320px"
            }
          ]
        }
      ]
    }
  ]
}
```

---
layout: center
size: xlarge
---

## GitHub Copilot SDK とは

- GitHub Copilot の **agent harness** をアプリケーションに組み込む SDK
- session、会話履歴、tool call、ストリーミング応答を扱う
- 組み込みツールに加え、独自ツールや MCP server を接続できる
- **Copilot Studio、Excel、Outlook、PowerPoint、Word、Copilot Cowork** などでも採用
- **対応言語：** TypeScript / Python / Go / .NET / Java / Rust

```architecture
{
  "version": 1,
  "title": "GitHub Copilot SDK の構成",
  "canvas": {
    "width": 1600,
    "height": 350
  },
  "elements": [
    {
      "type": "node",
      "id": "app",
      "x": 60,
      "y": 75,
      "width": 320,
      "height": 170,
      "text": "アプリ",
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
      "y": 45,
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
      "y": -10,
      "width": 400,
      "height": 150,
      "text": "モデル プロバイダー",
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
      "y": 160,
      "width": 400,
      "height": 150,
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
コーディング特化ではなくなってきている。
出典: https://github.blog/ai-and-ml/generative-ai/migrating-the-github-copilot-runtime-to-rust-using-copilot/
-->

---
layout: center
size: large
---

## GitHub Copilot SDK の使い方

1. `GitHub.Copilot.SDK` パッケージの追加
2. `CopilotClient` を作成
3. `SendAndWaitAsync` で呼出し

```csharp
using GitHub.Copilot;

await using var client = new CopilotClient();

await using var session = await client.CreateSessionAsync(
  new SessionConfig
  {
    Model = "gpt-6-luna",
  });

var reply = await session.SendAndWaitAsync("Hello, world!");
Console.WriteLine(reply?.Data.Content);
```

<!--
とっても簡単！
-->

---
layout: center
size: large
---

## Microsoft Foundry のモデルも呼べる

`SessionConfig` の `Provider` にモデルのデプロイ名、トークンの設定などをしておくと呼んでくれる。

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

<!--
GitHub Copilot とは関係ないモデルも呼べる
-->

---
layout: center
size: xlarge
---

## Microsoft Foundry にデプロイしたい

Microsoft Agent Framework は GitHub Copilot SDK にも対応

- `CopilotClient` を Agent Framework の `AIAgent` に変換可能
- `AIAgent` は Hosted Agent にデプロイ可能

つまり **GitHub Copilot SDK を使ったエージェントを Microsoft Foundry にデプロイ可能**

---
layout: section
size: xlarge
---

## Microsoft Foundry Hosted Agent にデプロイ

### GitHub Copilot SDK & Microsoft Agent Framework


---
layout: center
size: xlarge
---

## まとめ

- **GitHub Copilot SDK**
  - コーディング用途に限らず、**汎用的な Agent** の基盤として使われ始めている
  - BYOK で Foundry にデプロイしたモデルを推論先にできる
- **Microsoft Agent Framework**
  - `AsAIAgent` で `CopilotClient` を `AIAgent` に変換可能
  - GitHub Copilot SDK で作った Agent を Azure にデプロイのに良さそう…!!
