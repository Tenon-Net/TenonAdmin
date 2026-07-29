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
  <a href="https://www.nuget.org/packages/TenonAdmin"><img src="https://img.shields.io/nuget/v/TenonAdmin" alt="NuGet"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="Build"></a>
</p>

<p align="center">
  <a href="https://tenonadmin.52moyu.net/login"><strong>🔗 オンラインデモ</strong></a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="https://tenon.52moyu.net"><strong>📖 ドキュメント</strong></a>&nbsp;&nbsp;·&nbsp;&nbsp;<a href="CHANGELOG.md"><strong>📋 更新履歴</strong></a>
</p>

---

## 🎨 これは何？

TenonAdmin はバックオフィスの共通機能を NuGet パッケージにまとめています。ユーザー、ロール、メニュー、マルチ組織データ権限、辞書・設定、操作ログ、ファイルアップロード——どのバックオフィスでも結局作り直すことになるものを `dotnet add package` で導入できます。`Program.cs` に 3 行足せば、完全な管理 API が手に入ります：

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

- **デフォルトで動く** — ゼロ設定スタート：テーブル自動作成、シードデータ投入、SQLite フォールバック。初回起動に DB サーバーすら要りません。
- **気に入らない部分は差し替え** — 内蔵サービスはすべてインターフェース + `TryAdd` 登録。自分の実装を登録すれば内蔵実装は自動的に退きます。フォーク不要。
- **アップグレード = パッケージのバージョンを上げるだけ** — バグ修正も新機能もパッケージ更新で届き、ビジネスコードは一行も動かしません。

従来のやり方はテンプレートリポジトリをクローンすることでした：数百のファイルが自分の保守対象になり、ビジネスコードとフレームワークのコードが絡み合い、フレームワークの新バージョンが出ても diff を手作業でマージするしかありません。TenonAdmin はそこを逆転させます——共通機能はパッケージ管理に任せ、ビジネスコードはビジネスコードのままです。

フロントエンドも用意済みです：**機能同等の 2 つのテンプレート**（Vue と React）。好みの方を選んで、自分のプロジェクトの出発点にしてください。

## 🗺️ ランタイムアーキテクチャ

主リクエスト経路：デュアル SPA テンプレート → Host（SignalR 含む）→ 認証・データ権限 → ドメインサービス → SqlSugar → DB。支線はカーネル内蔵のジョブスケジューラと、任意の独立 Worker プロセス。

<p align="center">
  <a href="docs/architecture/tenon-runtime.ja.architecture.html">
    <img src="docs/architecture/tenon-runtime.ja.readme.png" alt="TenonAdmin ランタイムアーキテクチャ" width="100%">
  </a>
</p>

<p align="center">
  <a href="docs/architecture/tenon-runtime.ja.architecture.html"><strong>インタラクティブな構成図を開く</strong></a>
</p>

## 🔭 まず完成形を見たい？

[オンラインデモ](https://tenonadmin.52moyu.net/login)で動いているのは、カーネル同梱のサンプルホストではありません。独立した消費者アプリ **[tenon-example](https://github.com/Tenon-Net/tenon-example)** です：NuGet からパッケージを入れ、フロントエンドテンプレートを `degit` で取得し、CRM 業務モジュールを書き、本番にデプロイしたもの。ソースは全公開——TenonAdmin を採用したあなたのコードも、ああいう形になります。

ログインページにはワンクリックボタンが 4 つ。うち 3 つの業務アカウントは同じ顧客一覧を開いて、それぞれ 214 件、128 件、42 件を見ます。しかもそれを叩く `CustomerService` には組織フィルターが 1 行もありません。データ権限のグローバルフィルターが、ビジネスコードの外側で掛けているからです。どこで掛かっているかは[同じクエリ、3 つの数字](https://github.com/Tenon-Net/tenon-example/blob/dev/docs/showcase-multi-org-data-scope.md)（中国語）に。

## 🚀 クイックスタート

### 動作要件

- .NET 10 SDK
- Node.js 20+（フロントエンドテンプレートを動かす場合のみ）

### まず動かしてみる

リポジトリをクローンして、バックエンドはコマンド 1 つ：

```bash
dotnet run --project backend/samples/MinimalHost
```

初回起動でデータベースとテーブルを作成、シードデータを投入し、ランダム生成された管理者パスワードをコンソールに出力します（アカウントは `superAdmin`）。API は http://localhost:5100 で待機。

フロントエンドはどちらか一方を（両方起動してもポートは衝突しません）：

```bash
cd web && npm install && npm run dev            # Vue 版 → http://localhost:5173
cd web-react && npm install && npm run dev      # React 版 → http://localhost:5174
```

ブラウザを開いて、コンソールに出た認証情報でログインすれば、フル機能のバックオフィスが使えます。Windows ならもっと楽：リポジトリルートの `dev.bat` をダブルクリックすれば、バックエンド + 両フロントエンドが一度に起動します。

### 自分のプロジェクトに組み込む

```bash
dotnet add package TenonAdmin
```

`Program.cs` に先ほどの 3 行を足せば、起動時に JWT 認証・RBAC・データ権限・全管理エンドポイントが自動登録されます。データベースを変えたい？設定を 1 ブロック書くだけ：

```jsonc
// appsettings.json
"TenonAdmin": {
  "Database": {
    "DbType": "MySql",          // Sqlite / MySql / SqlServer / PostgreSQL
    "ConnectionString": "..."
  }
}
```

### 内蔵実装が気に入らない？差し替えましょう

内蔵サービスはすべてインターフェース + `TryAdd` 登録——先にあなたの実装を登録すれば、内蔵実装は自動的に退きます：

```csharp
// 例:パスワードハッシュアルゴリズムの差し替え。AddTenonAdmin の前に登録する
builder.Services.AddSingleton<IPasswordHasher, MyPasswordHasher>();
builder.Services.AddTenonAdmin(builder.Configuration);
```

さらに細かい粒度も：長いサービスメソッドは小さな `virtual` ステップに分割されているので、内蔵サービスを継承して気になる 1 ステップだけをオーバーライドできます。メソッドを丸ごとコピーする必要はありません。この差し替え可能性はスローガンではなく、専用の契約テスト一式がロックしています。

## ✨ バックエンド機能

- **認証** — アカウント/パスワード + キャプチャ、JWT + リフレッシュトークンローテーション、ログインロックアウト、オンラインセッションと強制ログアウト
- **RBAC** — ロール管理、ディレクトリ／ページ／ボタンの 3 階層メニュー、ボタンレベル権限コード、ロール-メニュー認可
- **データ権限** — 全データ / 自組織 / 自組織+配下 / 本人のみ / カスタム、ORM グローバルフィルタで自動適用——ビジネスコードにフィルタを書く必要なし
- **マルチアプリポータル** — アプリ管理、独立メニューツリー、アプリ選択と切り替え
- **組織** — 組織ツリー、役職、複数ロール対応ユーザーと主所属組織
- **通知** — アプリ内通知・お知らせ、全体 / ロール / ユーザー指定で送信
- **辞書と設定** — 辞書タイプ + 項目 + キーバリュー設定、キャッシュとイベント駆動キャッシュ無効化
- **ログ** — 操作ログ自動記録、機密入力マスキング
- **ファイル管理** — アップロード/ダウンロード、サイズ制限、拡張子ホワイトリスト、パストラバーサル防御
- **インポート/エクスポート** — xlsx インポートウィザード（プレビュー、セル単位バリデーション、重複検出、部分コミット）と列選択エクスポート。オプションパッケージ `TenonAdmin.Excel` が提供し、入れなければ発行サイズは 1 バイトも増えません
- **ジョブスケジューラ** — カーネル内蔵。新しい依存も別プロセスも不要です：cron（秒始まりの 6 フィールド、`L`/`W`/`#` 対応）、固定間隔、単発の 3 種類のトリガー。ペイロードは `IAdminJob` クラスを 1 つ書くだけ、あるいはコードを書かずに HTTP 呼び出しを設定するだけでも構いません。複数レプリカはデータベースのリースでリーダーを選出するため、1 台落ちてもスケジュールは止まらず、同じ発火はクラスタ全体で必ず 1 回だけ実行されます
- **マルチデータベース** — SQLite（デフォルト）/ MySQL / SQL Server / PostgreSQL、切り替えは設定変更だけ
- **マルチレプリカ** — オプション Redis キャッシュ、レプリカ間レート制限カウンタ共有、レプリカごとの Snowflake ワーカー ID——スケールアウトで落とし穴なし
- **抑制された依存関係** — コアパッケージのランタイム依存は SqlSugarCore + Microsoft.* のみ。サードパーティフレームワークの動物園をあなたのプロジェクトに持ち込みません

## 🖥️ フロントエンド：公式テンプレートは 2 つ、好きな方を

同じバックエンド契約に対して、完全に独立した 2 つのフロントエンドテンプレートを用意しています。使い慣れたスタックの方をどうぞ：

| | `web/` | `web-react/` |
|---|---|---|
| フレームワーク | Vue 3 + Naive UI | React 19 + Ant Design 6 |
| 状態管理 / ルーティング | Pinia + vue-router | zustand + react-router |
| 多言語 | vue-i18n | react-i18next |
| 開発ポート | :5173 | :5174 |

コード共有ゼロは意図的な設計です：2 つのテンプレートは互いに一切 import せず、ユーティリティ関数 1 つ共有しません。片方を持っていけばその依存だけを背負い、もう片方を消しても何も起きません。機能はページ単位で対等に移植済み：

- **契約生成 API** — OpenAPI → `schema.d.ts`、エンドツーエンドの型安全。エンドポイントが変わればフロントのコンパイルが落ちて教えてくれます
- **動的ルーティング** — バックエンドのメニューツリーがルート登録を駆動、マルチアプリポータルのシームレス切り替え
- **ボタンレベル権限** — Vue は `v-auth` ディレクティブ、React は `<Can>` コンポーネント、権限コードは共通
- **列駆動テーブル** — 1 つの `columns` 配列で検索フォーム・辞書レンダリング・列設定を同時に駆動
- **デザイントークン + ライト/ダークテーマ** — 4 層の CSS 変数トークン、システム追従 / 手動切替
- **3 種のログインページスキン** — すぐに使える切り替え式、スタイル分離
- **自社開発コンポーネント** — FormContainer（モーダル/ドロワー統合）、StatusSwitch（悲観更新トグル）、辞書スイート、OrgTreeSelect、FileUpload（チャンク/リジューム/即時アップロード）、PasswordStrength、ImportWizard（インポートウィザード）、チャートラッパーなど、各テンプレートに独立実装

## 🧩 リポジトリ構成

| ディレクトリ | 内容 |
|---|---|
| `backend/` | .NET 10 カーネル(NuGet パッケージ 9 つ)+ サンプルホスト + テスト |
| `web/` | Vue 3 + Naive UI フロントエンドテンプレート、自己完結 |
| `web-react/` | React 19 + Ant Design 6 フロントエンドテンプレート、自己完結 |
| `templates/` | `dotnet new tenon-app` プロジェクトテンプレート |
| `site/` | ドキュメントサイトのソース(VitePress、中英対応) |
| `docs/` | 設計ドキュメントと開発記録 |

## 📋 プロジェクト状況

**1.0 までに API が変わる可能性があります** — 破壊的変更は[更新履歴](CHANGELOG.md)に明記します。開発は `dev` ブランチで行っています。Issue や PR は歓迎です。

## 📄 ライセンス

[Apache License 2.0](LICENSE)
