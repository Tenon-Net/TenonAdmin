<!-- README.md と同期してください -->

[English](README.md) | [简体中文](README.zh-CN.md) | 日本語

<p align="center">
  <img src="web/design-mockups/brand/tenon-logo.svg" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center">
  <em>NuGet パッケージひとつ、コード 3 行で、業務管理システムの基盤が手に入ります。</em>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="License"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/stargazers"><img src="https://img.shields.io/github/stars/Tenon-Net/TenonAdmin" alt="Stars"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/network/members"><img src="https://img.shields.io/github/forks/Tenon-Net/TenonAdmin" alt="Forks"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

---

TenonAdmin は ASP.NET Core + SqlSugar + Vue 3 + Vite + Naive UI で作った管理画面テンプレートです。ログイン認証、RBAC、多組織データ権限、管理 UI がすぐに使えて、どのパーツも差し替えられます。社内管理システムを作るたびにユーザーと権限をゼロから組むのが面倒なら、これを使ってください。単体で動かすことも、既存の ASP.NET Core プロジェクトに組み込むこともできます。

「榫卯（ほぞ）」―― 釘を使わず、部品同士が噛み合って組み替えられる木造建築の技法。TenonAdmin の設計思想そのものです。

## なぜ作ったのか

.NET の管理画面フレームワークは色々ありますが、大体は動くアプリを渡してくるだけで、そこからのカスタマイズが大変です。フォークして、上流の更新を追いかけて、使わない依存関係まで抱えることになります。TenonAdmin は違います。NuGet パッケージとして配布されるので、あなたのプロジェクトがこちらを参照する形になります。

- **コード 3 行で導入** — スキャフォールドもボイラープレートも不要。`AddTenonAdmin()` + `MapTenonAdmin()` だけで、認証・RBAC・管理画面がそろいます。
- **設定不要で起動** — デフォルト SQLite、テーブル自動作成、初回起動時にランダムな管理者パスワードをコンソールに出力。`dotnet run` ですぐログインできます。
- **すべて差し替え可能** — サービスはインターフェース経由、メソッドは `virtual`、DI は `TryAdd` 登録。ワークフローの一部だけをオーバーライドでき、メソッド全体をコピーする必要はありません。4 段階: 設定 → サービス置換 → 継承オーバーライド → エンドポイント上書き。
- **依存関係を押し付けない** — ランタイムは SqlSugar + `Microsoft.*` のみ。より重い機能は必要になったときだけオプションパッケージで追加します。現在は `TenonAdmin.Caching.Redis`（マルチレプリカ構成の前提条件）があります。
- **多組織データ権限** — 多くの管理フレームワークが省略するか表面的にしか実装しない機能です。5 種類のスコープ（全体 / 自組織 / 自組織＋配下 / 本人のみ / カスタム）をロールごとに設定し、ORM のグローバルフィルタで自動適用されます。

## クイックスタート

同梱のサンプルを実行:

```bash
dotnet run --project backend/samples/MinimalHost
# 初回起動時、ランダムな管理者パスワードがコンソールに出力されます
```

自分のプロジェクトでは `Program.cs` に 3 行:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
app.Run();
```

テーブル作成、シードデータ、JWT 認証、RBAC がすべて準備されます。ビジネスモジュールの追加は[ガイド](docs/new-business-guide.md)をご覧ください。

## 機能一覧

- **認証** — ユーザー名/パスワード + キャプチャ、JWT + リフレッシュトークンローテーション、ログインロックアウト、オンラインセッション管理・強制ログアウト
- **RBAC** — ロール、3 階層メニュー（ディレクトリ / ページ / ボタン）、ボタンレベルの権限コード、ロール-メニュー認可
- **マルチアプリポータル** — モジュール管理、アプリごとのメニューツリー、ログイン時のアプリ選択、ユーザーごとのデフォルトアプリ
- **多組織データ権限** — 5 種類のスコープ、ロールごとの設定、ORM グローバルフィルタによるクエリレベルの自動適用
- **組織** — ユーザー、組織ツリー、役職；ユーザーの複数ロール対応、主所属組織
- **辞書 / 設定** — 辞書タイプと辞書項目、キーバリュー型システム設定（キャッシュ付き・イベント駆動で無効化）
- **ログ** — 操作ログ（自動記録、入力マスキング付き）、ログインログ（IP / UA / 結果）
- **ファイル管理** — ローカルアップロード/ダウンロード、サイズ制限、拡張子ホワイトリスト、パストラバーサル防御
- **個人設定** — パスワード変更、プロフィール編集、アバター

フロントエンド: Vue 3 + Naive UI 管理画面テンプレート。3 種類の切り替え可能なログインページスキンを同梱。

## カスタマイズ

4 段階のオーバーライド、深さの順に:

1. **設定** — `appsettings.json` の `TenonAdmin` セクションを変更
2. **サービス置換** — 任意の組み込みインターフェースに自分の実装を登録（`TryAdd` によりあなたの実装が優先）
3. **継承オーバーライド** — デフォルト実装を継承し、テンプレートメソッドの必要なステップだけを上書き
4. **エンドポイント上書き** — 組み込みコントローラのルートを置換・拡張

エンティティ拡張とカスタムビジネスモジュールにも対応 — [詳細](docs/rebuild-design.md)。

## 技術スタック

**バックエンド**

- .NET 10 (ASP.NET Core)
- SqlSugar ORM
- JWT Bearer 認証
- Snowflake ID 生成
- OpenAPI（フロントエンドのコントラクトソース）
- SQLite（デフォルト）/ MySQL / SQL Server / PostgreSQL

**フロントエンド**

- Vue 3.5 + TypeScript 5.7
- Naive UI 2.41
- Pinia 3（永続化対応）
- Vue Router 4 · Vue I18n
- Vite 6
- ECharts 5.6
- openapi-fetch（コントラクト生成 API クライアント）
- OxLint

**NuGet パッケージ（5 個）**

```
TenonAdmin.Core → TenonAdmin.SqlSugar → TenonAdmin.Services → TenonAdmin.AspNetCore → TenonAdmin
```

`TenonAdmin` をインストールすればフルスタック。個別レイヤーの参照も可能です。

## プロジェクト状況

現在のバージョンは **`0.1.0`**、nuget.org で公開しています:

```bash
dotnet add package TenonAdmin
```

バックエンドカーネル、全管理画面、設定センター、コンテナ配布、マルチレプリカ対応（Redis キャッシュ、レプリカ間で共有されるレート制限カウンタ、レプリカごとの Snowflake ワーカー ID）がいずれも動作し、CI でカバーされています。

**1.0 までに API が変わる可能性があります** — 破壊的変更は [更新履歴](CHANGELOG.md) に明記します。開発は `dev` ブランチで行っています。

## プロジェクト構成

```
tenon-admin/
├── backend/
│   ├── src/
│   │   ├── TenonAdmin.Core/            # 契約層：インターフェース、Options、ErrorCode
│   │   ├── TenonAdmin.SqlSugar/        # データ層：ORM、リポジトリ、CodeFirst
│   │   ├── TenonAdmin.Services/        # ドメイン層：エンティティ、サービス、RBAC
│   │   ├── TenonAdmin.AspNetCore/      # ホスト：コントローラ、フィルタ、JWT
│   │   ├── TenonAdmin/                 # メタパッケージ（これをインストール）
│   │   └── TenonAdmin.Caching.Redis/   # オプション：Redis キャッシュ
│   ├── samples/MinimalHost/            # サンプルプロジェクト（3 行で起動）
│   └── tests/                          # xUnit テスト
├── web/                                # Vue 管理画面フロントエンド
└── docs/                               # 設計ドキュメント、ガイド、ロードマップ
```

## ドキュメント

- [ビジネスモジュール追加ガイド](docs/new-business-guide.md)
- [デプロイ](docs/deployment.md)
- [アーキテクチャと設計](docs/rebuild-design.md)
- [ロードマップ](docs/dev-plan.md)

## ライセンス

[Apache-2.0](LICENSE)
