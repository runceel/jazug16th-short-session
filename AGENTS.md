# AGENTS.md

JAZUG 16th のショートセッション「GitHub Copilot SDK Agent を Foundry にデプロイ」の発表資料とデモのリポジトリです。AI エージェントがこのリポジトリで作業するときの前提とルールをまとめます。

## セッションの前提

| 項目 | 内容 |
|---|---|
| イベント | JAZUG 16th（Japan Azure User Group）ショートセッション |
| 発表者 | 日本マイクロソフト Kazuki Ota |
| 持ち時間 | 約 10 分（デモ 2 本を含む） |
| 聴衆 | Azure / .NET を使う開発者・エンジニア |
| 主題 | GitHub Copilot SDK の agent harness をそのまま使い、BYOK で Microsoft Foundry の model deployment を推論先にし、Aspire で Foundry Hosted Agent としてデプロイする |

話の流れ: Copilot SDK の概要 → BYOK の設定 → ProviderConfig の詳細 → デモ構成 → DEMO 1（ローカル）→ DEMO 2（デプロイ）→ Hosted Agent の認証 → DEMO 2（実行）→ まとめ。

## ファイル構成

| パス | 内容 |
|---|---|
| `slides.md` | 発表スライド本体（MarkdStage の Markdown） |
| `themes/ms-modern/` | カスタムテーマ（`theme.css`、表紙・裏表紙用の `theme.json` と `assets/`） |
| `demo/` | デモアプリ（.NET 10 + Aspire）。詳細は `demo/README.md` |
| `jazug16th-shortsession-scenario.md` | 発表シナリオ（話す内容の骨子） |
| `technical-research-report.md` | 技術調査レポート。スライドの記述の根拠と出典 |
| `.github/skills/markdstage/` | MarkdStage の利用ガイド（skill） |

`slides.pdf`、`slides.pptx`、`capture/`、`bin/`、`obj/` は生成物で、`.gitignore` 対象です。

## スライド作成のルール

### 1 枚 1 テーマ

- 1 枚のスライドで扱うテーマは 1 つにする。見出し（`##`）がそのスライドのテーマを表す。
- 複数のテーマを扱う内容になったら、詰め込まずにスライドを分ける。分割の例: 「GitHub Copilot 認証と Foundry BYOK」（比較）と「Foundry ProviderConfig の詳細」（設定値の説明）。
- 本文は見出しのテーマを説明する内容だけにする。補足は speaker notes に書く。

### 技術者向けの文体

広告やマーケティング資料ではなく、エンジニア向けの技術発表資料として書く。

- 「革新的」「圧倒的」「シームレス」「劇的に」など、根拠のない形容や誇張表現を使わない。
- 機能を説明するときは、何を設定すると何が起きるかを具体的に書く（API 名、設定項目、コマンド、ID、ロール名など）。
- 制約・前提・プレビュー版であることなど、利用者がつまずく点を省かない。プレビュー機能を GA のように書かない。
- 事実の記述は `technical-research-report.md` または公式ドキュメント（Microsoft Learn、GitHub Docs / Blog、Aspire docs）で裏付けを取り、出典 URL を speaker notes に残す。
- 「簡単に」などの評価語は、具体的な理由を併記できる場合にだけ使う（例: 同じ AppHost で `aspire run` と `aspire deploy` を切り替えられる）。

### 表記

- 本文は日本語。製品名・API 名・用語は英語のまま書く（`deployment`、`agent identity`、`session`、`tool call` など）。
- スライド本文では、日本語と英数字の間に半角スペースを入れる（例: `GitHub Copilot SDK とは`）。
- 製品名は正式名称を使う: GitHub Copilot SDK、Microsoft Foundry、Microsoft Agent Framework、Aspire（旧称の .NET Aspire は使わない）、Foundry Hosted Agent。
- コード、型名、コマンド、パスはバッククォートで囲む（`GitHubCopilotAgent`、`aspire deploy`、`/responses`）。
- 強調は `**太字**` で、1 枚あたり数か所までにする。
- サンプルコードのリソース名は架空の `example-foundry` を使う。実際のエンドポイント、テナント ID、サブスクリプション ID、キー、トークンをスライドに書かない。

### スライドの書式（MarkdStage）

- スライドは `---` で区切る。各スライドの先頭に front matter を書く。通常のスライドは次の形式。

  ```markdown
  ---
  layout: center
  size: large
  ---
  ```

- 1 枚目の表紙だけ `layout: title` で、`theme: custom` と `theme-file: ./themes/ms-modern/theme.css` を指定している。テーマの指定は変更しない。
- 図は `architecture` フェンス（Architecture DSL）で書く。書く前に `markdstage guide architecture-schema` を確認する。
- スライド用の HTML や CSS を手書きしない。はみ出しは、文章を短くするか、スライドを分けるか、front matter の `size` を変えて解決する。
- 書式の詳細は `.github/skills/markdstage/SKILL.md` と `markdstage guide slide-format` を参照する。

### Speaker notes

各スライドの末尾に、HTML コメントで speaker notes を書く。

```markdown
<!--
目安: 1:00
話す内容。スライドに書かなかった補足や前提をここに書く。
出典: https://...
-->
```

- 1 行目は必ず `目安: m:ss`（デモを含むスライドは `目安: 2:15（説明0:30 + デモ1:45）` のように内訳を書く）。
- 発表全体は約 10 分に収める。スライドを追加・変更したら、全スライドの「目安」の合計を確認し、超えた分は別のスライドで調整する。
- 本文は話し言葉ではなく、要点を説明する文章で書く。
- 外部の情報を引用した場合は `出典:` に URL を書く。

## スライドの検証

MarkdStage CLI は Store/MSIX 版と npm 版の両方がインストールされている場合があります（`Get-Command markdstage -All` で確認）。

`slides.md` を編集したら、次の順で確認します。

```powershell
markdstage validate .\slides.md --json   # 構造・テーマ・Architecture DSL の検証
markdstage inspect .\slides.md --json    # 1280x720 に収まっているか（はみ出しの検出）
markdstage capture .\slides.md --pages 3 # 見た目を確認したいページだけ PNG を出力
```

- `validate` と `inspect` でエラーやはみ出しがない状態を維持する（現状はどちらも問題なし）。
- 一部だけ変更した場合は `markdstage inspect .\slides.md --json --slide <n>` で対象スライドだけ確認できる。
- `capture` は `--pages` を付けないと、はみ出したスライドだけを出力する。
- `total` には裏表紙（テーマが自動で追加）も含まれるため、`slides.md` のスライド数より 1 多い。
- 発表・書き出しは `markdstage present .\slides.md`、`markdstage export .\slides.md --output slides.pdf`（または `slides.pptx`）。

## デモ（`demo/`）

「実行環境を教えて」と聞くと、agent が許可リストのシェルコマンドだけを実行して、OS・CPU アーキテクチャ・.NET ランタイムなどを答えるデモです。ローカル（Windows）と Hosted Agent（Linux コンテナー）で回答が変わることで、同じコードがクラウドで動いていることを示します。

| プロジェクト | 役割 |
|---|---|
| `EnvReporter.AppHost` | Aspire AppHost。Foundry アカウント、project、`gpt-6-luna` の deployment、Hosted Agent を定義 |
| `EnvReporter.Agent` | Copilot SDK + Microsoft Agent Framework の agent。Hosted Agent の Responses プロトコルで公開 |
| `EnvReporter.Agent/FoundryModelOptions.cs` | Aspire が注入する `CHAT_URI` / `CHAT_MODELNAME` をバインドし、起動時に検証 |
| `EnvReporter.Agent/ShellCommandPolicy.cs` | `SessionHooks.OnPreToolUse` フックで、シェルツールを固定の読み取り専用コマンドに制限 |

### ビルドと実行

```powershell
dotnet build .\demo\EnvReporter.slnx   # ビルド
cd .\demo; aspire run                   # DEMO 1: ローカル実行（推論は Azure 上の Foundry）
aspire deploy                           # DEMO 2: Foundry Hosted Agent にデプロイ
```

- 前提: .NET 10 SDK、Aspire CLI 13.6 以降、Docker（`aspire deploy` 時）、Azure の権限と `gpt-6-luna` のクォータ。
- `aspire run` / `aspire deploy` は Azure にリソースを作成し、課金が発生する。実行前にユーザーの確認を取る。
- 発表者が `aspire run` を実行中の場合、`EnvReporter.AppHost.exe` がロックされて `dotnet build` がファイルコピーのエラー（MSB3027）で失敗する。コンパイルエラーではないので、実行中のプロセスを勝手に停止しない。
- テナント ID などの接続情報は、ローカルではユーザーシークレット、`aspire deploy` では環境変数（`Azure__TenantId` など）で渡す。リポジトリにコミットしない。

### 変更時の注意

- Aspire の ServiceDefaults は使わない。`AgentHost.CreateBuilder` が OpenTelemetry を自動構成するため、`UseOtlpExporter` を併用すると OTLP exporter の二重登録で起動時に例外になる。
- `EnvReporter.Agent.csproj` の `ContainerUser` は `root` のままにする。Hosted Agent がマウントする `$HOME`（`/home/session`）に非 root ユーザーが書き込めず、Copilot CLI が `EACCES` で失敗するため。
- `ProviderConfig.BearerTokenProvider` は GitHub.Copilot.SDK 1.0.16 では評価用 API（`GHCP001`）。`Program.cs` で該当箇所だけ警告を抑制している。
- `gpt-6-luna` は Aspire 13.6 preview の `FoundryModel` 記述子にないため、AppHost でモデル名・バージョン・形式を文字列で指定している。
- シェルコマンドの許可リストを変更したら、`demo/README.md` の「許可しているコマンド」の表も更新する。許可するのは読み取り専用のコマンドに限る。
- パッケージのバージョンを変更したら、`demo/README.md` の「使用パッケージ」の表も更新する。

## スライドとデモの整合性

スライドとデモで内容が食い違わないようにします。どちらかを変更したら、もう一方も確認してください。

- モデル / deployment 名（`gpt-6-luna`）、`WireApi = "responses"`、トークンのスコープ（`https://ai.azure.com/.default`）
- `SessionConfig` / `ProviderConfig` のコード例と `demo/EnvReporter.Agent/Program.cs` の実装
- 認証の区間（クライアント → Hosted Agent、Hosted Agent → モデル）と RBAC ロール名
- `aspire run` と `aspire deploy` の違い（実行場所、endpoint、モデル呼び出しに使う ID）

スライドのコード例は説明用に簡略化してよいが、実装と矛盾する値や API 名は書かない。
