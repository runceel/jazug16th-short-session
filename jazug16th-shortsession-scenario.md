# GitHub Copilot SDK Agent を Microsoft Foundry Hosted Agent として実行する

## GitHub Copilot SDK

GitHub Copilot SDK は、agent のハーネスを外部アプリケーションから利用するための SDK です。今回の構成では Copilot SDK と Microsoft Agent Framework の連携を維持し、モデル推論先は Copilot SDK の BYOK provider 設定で Microsoft Foundry にします。

Foundry の `/openai/v1/` endpoint を使う場合、Copilot SDK の provider type は `openai`、`model` には Foundry で作成した model deployment 名を指定します。API 形式は選択したモデルが対応する Responses API または Chat Completions API に合わせます。推論に GitHub Copilot 認証は使わず、Foundry の API key または Microsoft Entra token を使います。

Microsoft Agent Framework は `AIAgent` の共通抽象化を提供し、GitHub Copilot SDK をラップする provider が用意されています。BYOK の provider 設定が採用する MAF integration のバージョンから設定できることを検証します。

## ローカルデモ

**DEMO:** Foundry deployment を推論先にした GitHub Copilot SDK + Microsoft Agent Framework の agent に「実行環境を教えて」と入力します。agent がシェル実行ツールで環境情報を確認し、その結果を使って回答します。ツール実行の許可はデモで必要な操作に限定します。ローカルの Foundry 認証には開発用 Microsoft Entra ID または API key を使い、資格情報をソースコードやコンテナーイメージに含めません。

## Azure へのデプロイ

Microsoft Foundry Hosted Agent は、所定の runtime protocol に従う agent container を Foundry Agent Service が管理する仕組みです。MAF の Hosted Agent adapter を使い、Aspire で Foundry リソースと agent のデプロイを記述します。Hosted Agent は「常時起動」とは限らず、Foundry が endpoint と要求に応じた session の実行環境を管理します。

**DEMO:** ローカルで実行した agent を Aspire で Azure にデプロイし、Hosted Agent endpoint に同じ入力を送ります。Hosted Agent 内のBYOK providerがモデル endpoint のtoken取得に使うprincipalを特定し、そのprincipalで認証を構成します。keyless 認証では `https://ai.azure.com/.default` scope の Microsoft Entra token を取得できることを確認します。必要なRBACロールはモデル種別とendpointで異なるため、選択deploymentの要件に合わせて対象resource scopeで確認します。MAF adapter がBYOK providerの認証設定を受け渡せることも、採用バージョンで検証します。

## まとめ

- Copilot SDK の BYOK を使い、Microsoft Foundry の model deployment を推論先として明示する。
- Microsoft Agent Framework で SDK agent を構成し、Hosted Agent adapter で Foundry の runtime protocol に接続する。
- Aspire を利用して Azure へデプロイする。BYOK の endpoint・deployment 名・実行時認証・権限は別途構成し検証する。
