# GitHub Copilot SDK Agent の Microsoft Foundry Hosted Agent デプロイ技術調査

調査日: 2026-10-01  
対象資料: [10 分セッションのスライド](./slides.md)、[発表シナリオ](./jazug16th-shortsession-scenario.md)  
調査対象: GitHub Copilot SDK、Microsoft Agent Framework (MAF) 連携、Microsoft Foundry Hosted Agent、Aspire によるデプロイ

> このレポートは、調査日時点で公開されている GitHub、Microsoft Learn、Aspire の公式資料をもとに、発表シナリオの実装可能性と運用上の前提を整理したものです。SDK、Agent Framework、Aspire のパッケージ/API は更新されるため、実装時には参照先と採用バージョンを再確認してください。プレビュー版として記載された機能を GA 機能とみなさないでください。

## 1. 要約

このシナリオは、Copilot SDK を agent harness として維持し、BYOK (Bring Your Own Key) provider 設定で Microsoft Foundry の model deployment を推論先に指定します。アプリケーションは MAF の `AIAgent` として構成し、Foundry Hosted Agent が要求するプロトコルを実装したコンテナーとしてデプロイします。Aspire は Foundry リソースとアプリケーションを開発・デプロイ時に記述するためのホスティング統合として使います。

重要な点は、次の三つです。

1. **Copilot SDK の harness とモデル推論先は分けて考えます。** SDK は session、tool、agent 実行を担い、BYOK 設定はモデル provider を Foundry endpoint に向けます。Foundry の `/openai/v1/` endpoint の場合、provider type は `openai`、`model` にはカタログ上のモデル名ではなく、実際に作成した model deployment 名を指定します。
2. **BYOK の推論認証と Hosted Agent の endpoint 認証は別です。** BYOK provider がモデル endpoint の token 取得に使う principal を特定し、その principal と endpoint / model deployment に合った RBAC を確認します。Foundry project の managed identity と Hosted Agent の agent identity は別であり、一方の権限を他方の直接 endpoint 呼び出しに当てはめません。
3. **サーバー用途では最小権限とセッション分離が前提です。** シェル実行・ファイル操作等のツールは、デモで動くことと安全に公開できることが同義ではありません。共有バックエンドでは ambient な CLI ツールを避け、必要なツール・パス・ユーザーごとの認証・セッション状態をアプリ側で制御します。

### 発表で伝える構成

```text
クライアント
  │ Entra 認証など
  ▼
Foundry Agent Service のエンドポイント
  │ Responses または Invocations
  ▼
Hosted Agent コンテナー (MAF hosting adapter)
  │
  └─ Microsoft Agent Framework の AIAgent
       └─ Copilot SDK ── BYOK provider ── Foundry model endpoint
                              └─ token取得に使うprincipal・推論RBACを個別に確認
```

BYOK ではモデル推論要求を GitHub Copilot service 経由にしません。BYOK provider が Foundry endpoint にどう認証するかは別途設定し、Hosted Agent の agent identity、Foundry project の managed identity、クライアントの endpoint 認証を混同しないでください。

## 2. コンポーネントと責任範囲

| コンポーネント | 責任 | このシナリオでの位置づけ |
|---|---|---|
| GitHub Copilot SDK | SDK クライアント、セッション、会話、ツール、streaming 等の agent harness | Agent の実行と BYOK provider への接続 |
| Copilot CLI/runtime | SDK と通信し、agent runtime を実行する runtime | SDK の実行環境。BYOK で使う場合も選択した SDK/runtime 版の配布・起動要件を確認 |
| Copilot SDK BYOK provider | model provider、endpoint、deployment 名、API 形式、認証を指定 | Foundry model endpoint を推論先として使う |
| Microsoft Agent Framework | `AIAgent` の共通抽象化、agent/workflow の構成 | Copilot SDK クライアントを MAF agent として利用 |
| Foundry Agent Server adapter | Hosted Agent の HTTP/protocol、readiness、streaming、shutdown 等 | MAF agent の実行を Foundry の契約へ接続 |
| Microsoft Foundry Agent Service | Hosted Agent の endpoint、container lifecycle、agent identity、scaling、session state、version 管理 | Agent のマネージドホスティング |
| Aspire.Hosting.Foundry | Aspire AppHost で Foundry アカウント、project、デプロイ等をモデル化 | ローカル開発と Azure デプロイ構成の統合 |
| Azure Container Registry (ACR) | Agent image の格納・配布 | Hosted Agent デプロイ時に image を参照 |

MAF は Hosted Agent の唯一の選択肢ではありません。Foundry は runtime contract を満たす独自実装も受け入れます。MAF を使う理由は、Agent の抽象化・構成と、Hosted Agent protocol adapter を活用して実装する HTTP plumbing を減らせることです。Copilot SDK の MAF provider と Hosted Agent adapter はそれぞれ別の統合であり、片方を追加しただけで他方の要件が自動的に満たされるとは限りません。

## 3. GitHub Copilot SDK

### 3.1 SDK の役割

Copilot SDK は、agent runtime をアプリケーションから利用するための SDK です。標準的な利用では、アプリケーションが `CopilotClient` を作成し、runtime を開始して session を作り、入力を送り、応答・イベントを処理します。BYOK 設定ではモデル provider と Foundry endpoint を指定し、SDK の agent/session/tool 機能を維持しながら推論先を切り替えます。SDK と runtime の関係や接続方法は言語・実行モードに依存します。サーバー構成では、headless runtime の起動方式、BYOK 設定の受け渡し、およびネットワーク要件を採用バージョンで確認してください。

### 3.2 MAF provider

GitHub の MAF integration guide は、Copilot SDK を MAF の agent provider として使う専用統合を案内しています。

- .NET: `GitHub.Copilot.SDK` と `Microsoft.Agents.AI.GitHub.Copilot` を利用し、`CopilotClient` を起動して `AsAIAgent()` で MAF の `AIAgent` として扱う例があります。
- Python: `copilot-sdk` と `agent-framework-github-copilot` を使い、`GitHubCopilotAgent` を作成する例があります。
- 統合パッケージの提供対象は .NET と Python です。TypeScript、Go、Java、Rust について同じ MAF provider があるとは資料に記載されておらず、これらの言語では Copilot SDK 自体を直接利用する方針です。
- GitHub の統合ガイドは .NET パッケージの例で `--prerelease` を指定しています。従って、利用する版・API の安定性を確認し、バージョンを固定して検証する必要があります。
- GitHub の MAF integration guide は一般的な導入前提として GitHub Copilot subscription と Copilot CLI (または SDK 同梱 CLI) を記載しています。一方、BYOK guide は GitHub Copilot 認証を経由しないモデル provider 利用を説明します。BYOK の推論認証で Copilot 認証を使わないことと、MAF integration/runtime の配布・起動要件が不要になることは同義ではありません。選択する SDK/runtime と MAF integration のバージョンで、実際の prerequisite と BYOK 設定の pass-through をクリーンな環境から検証してください。

MAF の `AIAgent` ラッパーは、GitHub Copilot が提供する全機能を抽象化して同じ動作にするものではありません。セッション設定、runtime、ツール許可、Copilot 特有の機能がどこまで MAF の共通 API に露出するかは、採用する integration のバージョンで実機確認してください。

### 3.3 SDK のサーバー利用とマルチテナント

GitHub のサーバーデプロイ資料では、SDK と headless runtime を TCP で分離して構成できます。複数の利用者を扱うサーバーでは、次の設計が案内されています。BYOK では利用者間で Foundry endpoint、deployment、token provider、session state が混在しないことも確認します。

- `mode: "empty"` を使い、CLI のデフォルト設定やホストの ambient tool を無効化する。
- 利用者ごとの session ID、Foundry provider の認証情報、ツール許可リスト、workspace/path を分離する。
- `availableTools` 等で利用可能なツールを明示する。
- runtime を複数 SDK client で共有する場合にも、利用者間で session state や認証が混在しない構成にする。
- session timeout と `COPILOT_HOME` / filesystem を含む状態の分離・保存方針を決める。

「1 リクエストにつき1 agent」を作るか、session を再利用するか、runtime と Foundry token provider をどの単位で共有するかは、デモの単一ユーザー構成から本番へ移す際に別途設計が必要です。

## 4. 認証・権限境界

### 4.1 このシナリオの BYOK 推論認証

今回の選択は、GitHub Copilot service をモデル推論先として利用する代わりに、Copilot SDK の BYOK provider から Foundry model endpoint を直接呼び出す構成です。従って、GitHub App の **Copilot Requests: Read & write**、`COPILOT_GITHUB_TOKEN`、Copilot Requests の組織課金をこの推論経路の要件として扱いません。モデル利用の課金・quota・データ処理条件は、対象の Foundry deployment、subscription、Microsoft の契約・サービス条件で確認します。

GitHub Copilot SDK の BYOK 設定では少なくとも provider type、Foundry endpoint、model、API wire format、credential source を決めます。Foundry の Azure OpenAI-compatible `/openai/v1/` endpoint では SDK provider type `openai` を使い、`model` に Foundry の **deployment 名**を渡します。API wire format はモデルと SDK/runtime の組み合わせが対応する `responses` または `completions` を選び、実際の deployment で確認します。Foundry カタログ上の model ID を deployment 名としてそのまま流用しないでください。

認証の選択肢は次のとおりです。

| 認証 | 実装・運用上の確認 |
|---|---|
| Microsoft Entra ID (推奨候補) | Hosted Agent の実行 identity から token を取得し、BYOK の bearer-token provider に安全に渡す。固定 bearer token をイメージや長期設定値に保存せず、期限切れ前の更新を検証する |
| Foundry API key | secret store から実行時に供給し、rotation、アクセス制限、ログ・例外への露出防止を実装する。image、ソース管理、デプロイ artifact へ埋め込まない |

BYOK は「Copilot SDK にモデルを指定すれば自動的に Foundry 認証が成立する」という意味ではありません。SDK の BYOK 設定が対象バージョンでどの形式・token callback を受け取れるか、MAF integration がその session/provider 設定を保持するかを確認します。GitHub の MAF integration guide は通常導入の prerequisite に Copilot subscription と Copilot CLI/runtime を記載する一方、BYOK guide は GitHub Copilot authentication を使わない provider 認証を説明しています。この二つだけから、BYOK 時に CLI/runtime の全要件や subscription prerequisite まで不要と結論づけることはできません。採用する版でクリーン環境の動作を検証し、実際に必要な前提だけを構成資料に残してください。

### 4.2 Foundry / Azure の認証と権限

Foundry Hosted Agent は agent identity を持ち、Foundry project 自身も managed identity を持ちます。Hosted Agent permissions reference によると、project endpoint を通じた model inference は project managed identity で account-level deployment に proxy され、project managed identity には account scope の `Foundry User` が必要です（project 作成者の権限に応じて自動割当される場合があります）。一方、このシナリオの Copilot SDK BYOK は Foundry の `/openai/v1/` account endpoint を直接呼ぶため、project endpoint の proxy 経路とは異なります。BYOK bearer-token provider が実際に使う principal を確認し、その principal に選択した endpoint と model deployment の要件に合う権限を割り当てます。Hosted Agent の agent identity を使うなら、account-level access を明示的に確認します。keyless token scope は `https://ai.azure.com/.default` です。ロールは model/API によって異なり、OpenAI model のみなら `Cognitive Services OpenAI User`、より広い Foundry model access では `Cognitive Services User` または `Foundry User` が候補です。選択deploymentの公式要件とscopeを実環境で検証します。これらは**実行時 principal の権限**であり、デプロイ操作者の権限を置き換えません。

Hosted Agent をデプロイする操作者の最低限のロールは、公式デプロイ資料では Foundry project scope の **Foundry Project Manager** です。実際の構成で追加のリソース作成や role assignment を行う場合、Azure control plane 側で必要な権限も別途満たす必要があります。`azd` / VS Code のデプロイ経路は多くの RBAC 設定を補助しますが、最小権限で何が設定されたかをレビューします。

### 4.3 信頼境界・データフロー

今回の BYOK 推論では、prompt と model inference request は設定した Foundry endpoint に送信し、GitHub Copilot service を推論経路にしません。ただし、Hosted Agent の実行環境が Foundry 上にあるだけで、データが Foundry の境界内に留まるとは断定できません。アプリケーションが使う SDK/runtime の制御通信、package/image registry、tool が呼ぶ外部 API/MCP、ログ・trace なども個別にデータフローへ含めてください。

本番では入力データ分類、Foundry の契約・地域・保持・処理条件、利用者への通知、監査ログ、保持期間、障害時の再試行を確認します。GitHub Copilot service への推論送信はこの選択構成の前提ではありません。GitHub Copilot 認証方式へ切り替える場合には、別途 GitHub のデータ処理・契約・課金・組織ポリシーを再評価します。

## 5. Microsoft Foundry Hosted Agent

### 5.1 実行モデル

Hosted Agent は、自分の agent code をコンテナー化し Foundry Agent Service に登録する方式です。Foundry は専用 endpoint、agent identity、コンテナーの実行管理、scaling、session state、observability、lifecycle/version 管理を提供します。リクエストを受けると、Agent Service がその session 用の実行環境にルーティングします。

したがって「Hosted Agent はクラウド上で常時起動している」とは限りません。公式説明ではリクエスト時に session 用 compute を provision し、active / idle / resume の状態を扱います。スライドや口頭説明では「Azure 上で管理ホストされ、専用 endpoint から要求に応答する」と表現し、「常に稼働」は避けるのが正確です。

### 5.2 コンテナー runtime contract

標準 container deployment でコンテナーが満たす契約は以下です。

| 要件 | 内容 |
|---|---|
| アーキテクチャ | `linux/amd64` (x86_64)。ARM image はそのままでは互換性がない |
| HTTP listener | port `8088`、HTTP/1.1、平文 HTTP。TLS はプラットフォーム側で終端 |
| readiness | `GET /readiness` に `200 OK` |
| protocol endpoint | 少なくとも `POST /responses` または `POST /invocations` |
| platform variables | 起動時にプラットフォームから渡される環境変数を処理 |
| shutdown | `SIGTERM` を受けて接続を閉じ、書き込みを flush して終了 |

Foundry の Agent Server adapter (`Azure.AI.AgentServer.Responses` / `Azure.AI.AgentServer.Invocations`、Python では対応する `azure-ai-agentserver-*`) は、HTTP server、port、readiness、protocol の parse/format、SSE、OpenTelemetry、graceful shutdown、platform environment を扱います。採用する protocol に応じた adapter のバージョンを使い、handler のみを実装する構成が基本です。

**Responses** は OpenAI Responses API 互換で、対応 adapter は会話 ID がある場合の conversation history hydration を扱います。**Invocations** は任意 JSON の pass-through で、payload と state 管理をアプリケーション側が担います。MAF 統合・クライアントの期待・streaming 要件に合わせて選択してください。MAF Hosted Agent adapter を使う場合、MAF の agent にどう接続するかはその package/version の sample に合わせ、Hosted Agent の adapter を手作業で重複実装しないようにします。

### 5.3 デプロイの流れと version

公式の一般的な lifecycle は次のとおりです。

1. Agent の依存関係を解決し、コンテナー image を build して ACR に push。
2. image から Hosted Agent version を作成。
3. Foundry が専用 agent identity と実行環境を構成。
4. version の状態が `active` になるまで確認。
5. 専用 endpoint へリクエストを送信。

`azd` は agent と Azure リソースの provision/deploy を自動化でき、資料では `azd up` を初回の構成・デプロイ、`azd deploy` を既存環境への code-only デプロイの例として示しています。これは Aspire のコマンド/API と同一ではありません。今回のデモは Aspire を使うため、AppHost のデプロイフローに沿ってください。デプロイ後に version を確認し、production endpoint の version pin / promote 方針を用意してから本番切替します。

## 6. Aspire による Foundry 接続

Aspire の Azure AI Foundry hosting integration は `Aspire.Hosting.Foundry` NuGet パッケージを AppHost に加え、`AddFoundry(...)`、Foundry project、model deployment、hosted agent、その他のリソースを AppHost から記述する統合です。`WithReference(...)` は consuming resource に connection 情報を渡すために使われます。Foundry project に hosted agent を含める publish 構成では、既定 ACR の作成等、run と publish で動作が異なる項目がある点に注意してください。

調査時に取得できた Aspire hosting integration のドキュメント例は `Aspire.Hosting.Foundry` `13.5.3-preview.1.26425.3` を掲載しています。これは**ドキュメントに掲載された preview package の例**であり、導入時点の最新安定版を意味しません。プロジェクトでは利用可能な最新の互換版・preview 状態を NuGet と Aspire docs で確認して pin し、AppHost の local run と publish/deploy の両方を検証します。

Aspire はアプリケーションの構成・依存関係のモデル化とデプロイ作業をまとめやすくしますが、次を自動解決するものではありません。

- BYOK provider の Foundry endpoint、deployment 名、wire format、Entra token provider または secret の供給・更新。
- agent code が呼び出す全ての外部サービスの認可とデータ保護。
- unsafe tool を安全なものへ変えること。
- Hosted Agent runtime contract に対応していない任意プロセスを有効な agent にすること。
- 推論先 Foundry resource の RBAC assignment と、実行時 principal での認証確認。

## 7. ツール実行・運用上の安全性

### 7.1 デモのシェルツール

シェル・ファイル・URL・MCP などのツールは、agent が外部作用を起こす入口です。Copilot SDK の `onPreToolUse` hook は tool 実行前に allow / deny / ask を返し、引数の変更、追加 context、出力抑制にも使えます。デモでは必要な対象ツールにだけ限定して許可し、実行コマンドを固定または制限し、作業 directory を読み取り専用にできるならそうします。

ログには tool 名・安全な範囲の結果・request correlation ID を残し、token、機微な引数、ファイル内容などを記録しないでください。「全 tool を承認する」「すべての shell command を許可する」hook は開発用の挙動例であって、本番ポリシーではありません。

### 7.2 Hosted Agent の隔離

Hosted Agent の sandbox や専用 agent identity があっても、アプリケーションが明示的に許可する tool の範囲、ネットワーク送信、ユーザー間の session 分離、外部システムの RBAC が不要になるわけではありません。共有 API にする場合は、ユーザー認証・認可を endpoint 側で検証し、ユーザー A の session、GitHub token、workspace、tool allowlist をユーザー B に流用しないようにします。

### 7.3 信頼性と観測性

- 起動時の依存サービス/環境変数を検証し、不足している場合は明示的に失敗させる。
- Readiness は agent が実際に要求を処理できる状態を示すようにする。
- SIGTERM 時に client/runtime と HTTP server を適切に終了する。
- request/session ID、agent version、latency、tool call、失敗の種類を相関できるログ・trace を用意する。秘密情報や prompt 全文を不用意に記録しない。
- 複数リクエストや長時間実行を想定し、timeout、retry、idempotency、stream disconnect、session persistence を確認する。
- 新しい agent version を即時 production に切り替えず、version pin と明示的な promotion/rollback 手順を使う。

## 8. 発表デモの実施前チェックリスト

### ローカル

- [ ] .NET または Python の SDK、Copilot CLI/runtime、MAF integration、Agent Server adapter の具体的な package version を固定した。
- [ ] Copilot SDK の BYOK provider 設定に Foundry の `/openai/v1/` endpoint、実在する deployment 名、対応する wire format を設定し、選択 deployment で応答を確認した。
- [ ] BYOK 構成に GitHub Copilot 認証を誤って要求したり、GitHub Copilot service へ推論を fallback したりしないことを確認した。
- [ ] Foundry API key または Microsoft Entra token provider が選択した SDK/runtime/MAF integration で実際に利用できることを確認した。
- [ ] MAF integration guide の通常 prerequisite と BYOK guide の説明の差を確認し、選択したバージョンで CLI/runtime と subscription の必要性をクリーン環境から検証した。
- [ ] `CopilotClient` の起動・終了、Agent Framework wrapper、ツール呼び出しを順に確認した。
- [ ] シェルツールは `uname -a` や `dotnet --info` のような読み取り専用で予測可能な操作に限定した。
- [ ] tool permission はデモに必要なものだけ許可し、拒否時・runtime 不在時の表示を確認した。

### Azure / Hosted Agent

- [ ] Foundry project、利用可能な region、必要な model/connection、ACR、ネットワーク制約を確認した。
- [ ] デプロイ操作者の Foundry Project Manager と、追加 role assignment に必要な Azure 権限を確認した。
- [ ] `linux/amd64` image、port 8088、`/readiness`、Responses/Invocations endpoint、SIGTERM を検証した。
- [ ] BYOK provider が実際に token を要求する principal と Foundry resource scope を特定し、選択deploymentの公式要件に合う最小 RBAC（例: OpenAI model は `Cognitive Services OpenAI User`、より広い Foundry model access は `Cognitive Services User` または `Foundry User`）を割り当てて実行時に確認した。
- [ ] Entra token を使う場合は `https://ai.azure.com/.default` scope、token 更新、期限切れ時の明示的な失敗を確認した。API key の場合は secret store、rotation、ログ非出力を確認した。
- [ ] Foundry endpoint の契約、quota、deployment 名、API 形式、データ処理・保持・地域要件を確認した。
- [ ] Aspire の local run / publish と実際のデプロイで差がないことを確認し、使用 package version を固定した。
- [ ] デプロイ済み Hosted Agent version が `active` で、Azure endpoint からローカルと同じ入力を試せる。

### 10 分枠での時間管理

資料上はローカル実行とクラウド実行をそれぞれ見せる構成です。コンテナー build、image push、Foundry version 作成・起動待ちはライブ発表の時間に収まらない場合があるため、事前に一度デプロイして endpoint と version を確認します。ライブでは、(1) ローカル agent の tool call、(2) Hosted Agent endpoint への同一入力、(3) 両者の差分に絞ります。デプロイ操作は成功済み画面または短いログにし、失敗しても説明を続けられるよう応答例を準備します。

## 9. 発表資料への技術的な反映候補

発表の中立的な技術説明を保ちながら、以下を反映すると誤解を減らせます。

1. Hosted Agent の説明は「常に稼働」ではなく「Foundry が管理する実行環境と endpoint。要求に応じて session compute を割り当てる」とする。
2. MAF integration、Foundry Hosted Agent adapter、Aspire hosting integration を別の役割として説明する。
3. 推論経路は「Copilot SDK BYOK provider → Foundry endpoint」、認証経路は「実行 principal → token provider → Foundry resource RBAC」として表し、クライアントから Hosted Agent endpoint への認証とは分ける。
4. `/openai/v1/` の provider type `openai`、Foundry deployment 名、wire format を明記し、選択 deployment と SDK/runtime の互換性を検証する。
5. MAF の通常 prerequisite と BYOK の認証迂回説明に差があること、MAF integration から BYOK 設定を渡せるか未検証であることを説明者向け注記に残す。
6. `Aspire.Hosting.Foundry` が preview 版の例であることと、package version を固定・確認する必要性を補足する。
7. 「ツール自動承認対象を限定」とする方針は適切。さらに本番では session ごとの tool allowlist とユーザー分離も必要と補足する。

## 10. 参考資料

### GitHub Copilot SDK

- [GitHub Copilot SDK overview](https://docs.github.com/en/copilot/how-tos/copilot-sdk)
- [Microsoft Agent Framework integration](https://docs.github.com/en/copilot/how-tos/copilot-sdk/integrations/microsoft-agent-framework)
- [Copilot SDK: BYOK and custom model providers](https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/byok)
- [Backend services setup](https://docs.github.com/en/copilot/how-tos/copilot-sdk/setup/backend-services)
- [Multi-tenancy and server deployments](https://docs.github.com/en/copilot/how-tos/copilot-sdk/setup/multi-tenancy)
- [Authentication options](https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth)
- [Server-to-server authentication](https://docs.github.com/en/copilot/how-tos/copilot-sdk/auth/server-to-server-tokens)
- [Copilot SDK `onPreToolUse` hook reference](https://github.com/github/copilot-sdk/blob/main/docs/hooks/pre-tool-use.md)

### Microsoft Foundry Hosted Agent

- [Hosted agents in Foundry Agent Service](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agents)
- [Hosted agent runtime contract](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agent-contract)
- [Deploy a hosted agent](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/deploy-hosted-agent)
- [Create and deploy your first hosted agent](https://learn.microsoft.com/en-us/azure/foundry/agents/quickstarts/quickstart-hosted-agent)
- [Hosted agent permissions reference](https://learn.microsoft.com/en-us/azure/foundry/agents/concepts/hosted-agent-permissions)
- [Microsoft Foundry Models REST API reference (v1)](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/responses?view=rest-microsoft-foundry-v1)
- [Configure keyless authentication with Microsoft Entra ID](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/how-to/configure-entra-id)
- [Foundry Models endpoint and deployment](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/endpoints)

### Aspire

- [Aspire Azure AI Foundry hosting integration](https://aspire.dev/integrations/cloud/azure/azure-ai-foundry/azure-ai-foundry-host/)
- [Aspire.Hosting.Foundry on NuGet](https://www.nuget.org/packages/Aspire.Hosting.Foundry)
