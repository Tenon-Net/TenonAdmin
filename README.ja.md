<!-- README.zh-CN.md（正本）に合わせて同期してください -->

[English](README.md) | [简体中文](README.zh-CN.md) | 日本語

<p align="center">
  <img src="web/design-mockups/brand/icon-128.png" width="80" height="80" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center">
  <em>コード 3 行で、ASP.NET Core プロジェクトに完全で拡張可能な RBAC 権限管理を組み込めます。</em>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="License"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/stargazers"><img src="https://img.shields.io/github/stars/Tenon-Net/TenonAdmin" alt="Stars"></a>
  <a href="https://github.com/Tenon-Net/TenonAdmin/network/members"><img src="https://img.shields.io/github/forks/Tenon-Net/TenonAdmin" alt="Forks"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

---

TenonAdmin は ASP.NET Core、SqlSugar、Vue 3、Vite、Naive UI で構築した管理画面向けの権限管理システムです。

一般的な管理画面テンプレートとは少し違います。プロジェクト全体をコピーして二次開発するのではなく、ユーザー・ロール・メニュー・組織・データ権限・ログといった共通機能を、組み込み・差し替え・拡張が可能なモジュールとして提供します。

既存の ASP.NET Core プロジェクトでは、サービス登録・アプリのビルド・エンドポイントのマッピングを行うだけで、ログイン認証・RBAC 権限・管理画面という基盤機能を組み込めます。デフォルト実装をそのまま使うことも、業務に合わせてサービスやフローを差し替えることもできます。

## なぜ TenonAdmin を作ったのか

実際に管理システムを開発すると、ユーザー・ロール・メニュー・権限・組織・ログといった機能を繰り返し実装することになりがちです。

管理画面テンプレートをそのままコピーすれば着手は速いものの、業務コードが増えるにつれてプロジェクトはテンプレート自体と密結合していきます。あとから基盤機能をアップグレードしたり、上流の変更を取り込んだり、一部だけを差し替えたりするのが面倒になっていきます。

TenonAdmin は、こうした共通機能を具体的な業務から切り離し、権限管理の基盤をそのまま使えると同時に、既存プロジェクトにも自然に組み込めるようにすることを目指しています。

- **3 行で導入** — 既存の ASP.NET Core プロジェクトで登録とマッピングを行うだけで、ログイン認証・RBAC 権限・管理 API が有効になります。
- **デフォルトで起動** — 既定で SQLite を使い、起動時にテーブルを自動作成して初期データを投入します。
- **必要に応じて差し替え可能** — 主要サービスはインターフェース経由で登録され、デフォルト実装の要所は継承とオーバーライドに対応します。
- **依存は必要な分だけ** — ランタイムの依存は SqlSugar + `Microsoft.*` のみ。より重い機能は独立したオプションパッケージとして必要なときに追加します。
- **データ権限を内蔵** — 全データ / 自組織 / 自組織および配下 / 本人のみ / カスタム組織の 5 種類のデータスコープに対応します。
- **フロント・バック両方を提供** — バックエンドは権限と業務の基盤を、フロントエンドは Vue 3 と Naive UI ベースの管理画面を提供します。

## クイックスタート

リポジトリ同梱のサンプルを実行します:

```bash
dotnet run --project backend/samples/MinimalHost
```

初回起動時にデータベースの作成と初期データの投入を行い、ランダム生成された管理者パスワードをコンソールに出力します。必ず控えておいてください。

既存の ASP.NET Core プロジェクトでは、コアの導入はわずか 3 行です:

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

あとは通常の ASP.NET Core プロジェクトと同じく `app.Run()` でアプリを起動します。

起動後は、テーブルの自動作成・初期データ投入・JWT 認証・RBAC 権限・データ権限・管理 API がすべて登録されます。

## 主な機能

- **ログイン認証** — アカウント/パスワードログイン、画像キャプチャ、JWT、リフレッシュトークンローテーション、ログインロックアウト、オンラインセッションと強制ログアウト
- **RBAC 権限** — ロール管理、ディレクトリ／ページ／ボタンの 3 階層メニュー、ボタンレベルの権限コード、ロール-メニュー認可
- **マルチアプリポータル** — アプリ/モジュール管理、アプリごとのメニューツリー、アプリの選択と切り替え、ユーザーごとのデフォルトアプリ
- **データ権限** — 5 種類のデータスコープに対応し、ORM のグローバルフィルタでクエリ時に自動適用
- **組織** — ユーザー、組織ツリー、役職の管理。ユーザーは複数ロールを持ち、主所属組織を設定可能
- **メッセージ通知** — アプリ内通知とお知らせを、全体 / 指定ロール / 指定ユーザーへ送信でき、ヘッダーのベルで未読通知とメッセージパネルを提供
- **辞書と設定** — 辞書タイプ、辞書項目、キーバリュー設定。キャッシュとイベント駆動のキャッシュ無効化に対応
- **ログ管理** — 操作ログを自動記録し機密入力をマスキング、ログインの IP・User-Agent・結果も保持
- **ファイル管理** — ローカルファイルのアップロード/ダウンロード、サイズ制限、拡張子ホワイトリスト、パストラバーサル防御
- **個人設定** — パスワード変更、プロフィール編集、アバター設定

フロントエンドは Vue 3 と Naive UI を使用し、現在 3 種類の切り替え可能なログインページスキンを提供しています。

## 拡張とカスタマイズ

TenonAdmin は複数の階層の拡張手段を用意しており、変更範囲に応じて選べます:

1. **設定の変更** — `appsettings.json` の `TenonAdmin` セクションを調整
2. **サービスの置換** — カスタム実装を登録し、システムのデフォルト実装を置き換える
3. **デフォルト実装の継承** — 既存サービスを継承し、調整が必要なフローステップだけをオーバーライド
4. **エンドポイントの拡張** — デフォルトルートを置換、または独自の業務 API を追加

エンティティ拡張とカスタム業務モジュールにも対応しており、既存の権限体系の上で実際の業務開発を続けられます。

## 技術スタック

### バックエンド

- .NET 10（ASP.NET Core）
- SqlSugar ORM
- JWT Bearer 認証
- SQLite（デフォルト）／MySQL／SQL Server／PostgreSQL

### フロントエンド

- Vue 3.5 + TypeScript 5.7
- Naive UI 2.41
- Pinia 3（永続化対応）
- Vue Router 4
- Vue I18n
- Vite 6
- ECharts 5.6

### NuGet パッケージ

```text
TenonAdmin.Core → TenonAdmin.SqlSugar → TenonAdmin.Services → TenonAdmin.AspNetCore → TenonAdmin
```

通常は `TenonAdmin` を参照すればバックエンドの全機能が手に入ります。より細かい依存制御が必要な場合は、個別のレイヤーを参照することもできます。

## プロジェクト状況

現在のバージョンは **`0.1.0`**、nuget.org で公開しています:

```bash
dotnet add package TenonAdmin
```

バックエンドカーネル、完全な管理画面、設定センター、コンテナ配布、マルチレプリカ対応（Redis キャッシュ、レプリカ間で共有されるレート制限カウンタ、レプリカごとの Snowflake ワーカー ID）がいずれも動作し、CI でカバーされています。

**1.0 までに API が変わる可能性があります** — 破壊的変更は [更新履歴](CHANGELOG.md) に明記します。開発は `dev` ブランチで行っています。

## プロジェクト構成

```text
tenon-admin/
├── backend/
│   ├── src/
│   │   ├── TenonAdmin.Core/            # 契約層：インターフェース、Options、ErrorCode
│   │   ├── TenonAdmin.SqlSugar/        # データ層：ORM、リポジトリ、CodeFirst
│   │   ├── TenonAdmin.Services/        # ドメイン層：エンティティ、サービス、RBAC
│   │   ├── TenonAdmin.AspNetCore/      # ASP.NET Core 統合：コントローラ、フィルタ、JWT
│   │   ├── TenonAdmin/                 # 完全なバックエンドメタパッケージ
│   │   └── TenonAdmin.Caching.Redis/   # オプションの Redis キャッシュ実装
│   ├── samples/MinimalHost/            # 最小サンプルプロジェクト
│   └── tests/                          # xUnit テスト
├── web/                                # Vue 管理画面フロントエンド
└── docs/                               # ドキュメントと開発計画
```

## ドキュメント

- [ビジネスモジュール開発ガイド](docs/new-business-guide.md)
- [デプロイ](docs/deployment.md)
- [アーキテクチャと設計](docs/rebuild-design.md)
- [ロードマップ](docs/dev-plan.md)

## ライセンス

本プロジェクトは [Apache License 2.0](LICENSE) の下で公開されています。
