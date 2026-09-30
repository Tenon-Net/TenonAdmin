<!-- 本文件与中文基准版 README.zh-CN.md 保持同步。 -->

[English](README.md) | [简体中文](README.zh-CN.md) | 日本語

<p align="center">
  <img src="web/design-mockups/brand/tenon-mark.png" width="96" height="96" alt="TenonAdmin">
</p>

<h1 align="center">TenonAdmin</h1>

<p align="center"><strong>3 行のコードで、ASP.NET Core プロジェクトに管理画面フレームワークを導入。</strong></p>

<p align="center">NuGet で導入 · 業務コードは自分のプロジェクトで管理 · Vue / React のフロントエンド</p>

<p align="center">
  <a href="https://www.nuget.org/packages/TenonAdmin"><img src="https://img.shields.io/badge/NuGet-0.7.0-004880?logo=nuget&logoColor=white" alt="NuGet 0.7.0"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/ASP.NET_Core-512BD4?logo=dotnet&logoColor=white" alt="ASP.NET Core">
  <img src="https://img.shields.io/badge/Vue-3-4FC08D?logo=vuedotjs&logoColor=white" alt="Vue 3">
  <img src="https://img.shields.io/badge/Naive_UI-Vue-36AD6A" alt="Naive UI">
  <img src="https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=20232A" alt="React 19">
  <img src="https://img.shields.io/badge/Ant_Design-6-0170FE?logo=antdesign&logoColor=white" alt="Ant Design 6">
  <img src="https://img.shields.io/badge/SqlSugar-ORM-2F6F9F" alt="SqlSugar ORM">
  <a href="https://github.com/Tenon-Net/TenonAdmin/actions/workflows/backend-ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Tenon-Net/TenonAdmin/backend-ci.yml?branch=dev" alt="dev バックエンドのビルド状況"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Tenon-Net/TenonAdmin" alt="Apache License 2.0"></a>
</p>

<p align="center">
  <a href="#クイックスタート">クイックスタート</a> ·
  <a href="#スクリーンショット">スクリーンショット</a> ·
  <a href="https://tenonadmin.52moyu.net/login">オンラインデモ</a> ·
  <a href="https://tenon.52moyu.net/">ドキュメント</a> ·
  <a href="CHANGELOG.md">更新履歴</a>
</p>

## 概要

TenonAdmin は ASP.NET Core 向けの管理画面フレームワークです。ユーザー、ロール、メニュー、組織、データ権限などの共通機能と、Vue と React の 2 種類の管理画面を提供します。社内業務システムや運用管理画面の開発に使えるほか、既存の .NET プロジェクトにも導入できます。

NuGet で `TenonAdmin` をインストールし、`Program.cs` に次のコードを追加します。

```csharp
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();
```

認証、ロール権限、データ権限、管理 API の登録はフレームワークが行います。既定では SQLite を使用し、初回起動時にデータベース、テーブル、初期アカウントを作成するため、先にデータベースサーバーを用意する必要はありません。起動コード全体とフロントエンドの実行手順は[クイックスタート](#クイックスタート)にあります。

顧客や注文などの業務エンティティ、API、画面は、自分のプロジェクトで開発します。ログイン処理、ファイル保存、権限ルールは拡張インターフェースで変更できます。バックエンドの更新は NuGet で受け取り、業務コードはフレームワークのソースと分けて管理します。

## 主な特徴

### 自分のプロジェクトで業務を開発し、フレームワークの動作も調整

顧客管理モジュールを開発する場合、既存のアカウント、ロール、メニュー、ファイル管理を再利用し、顧客エンティティ、業務 API、管理画面をアプリケーションプロジェクトに実装します。業務コードをフレームワークのソースに書き込む必要はなく、既存の業務ロジックもそのまま残せます。

ファイルをオブジェクトストレージに保存したい場合や、ログイン結果に業務情報を追加したい場合は、対応するサービスを差し替えるか、組み込みサービスを継承して必要な処理だけを変更できます。インターフェースとオーバーライド可能なメソッドが用意されているため、一つの処理を変えるために実装全体をコピーする必要はありません。

バックエンドの修正や新機能は NuGet のバージョンとして提供されます。更新時は更新履歴を確認し、必要に応じて拡張コードやフロントエンドの API 呼び出しを調整してください。具体例は[サービスの差し替えと拡張](skills/replace-service.md)にあります。

### 操作できる機能と、参照できるデータを制御

ロール権限は、ユーザーが使えるメニュー、ボタン、API を決めます。データ権限は、参照できる業務レコードの範囲を決めます。同じ顧客一覧でも、部門責任者には自部門のデータ、上位の責任者には配下の部門を含むデータを表示でき、部門をまたぐ業務では参照可能な組織を個別に指定できます。

業務エンティティをデータ範囲の規約に合わせると、対応するクエリには現在のユーザーのロールに応じたフィルターが適用されます。顧客の検索処理は業務サービスに置き、データ範囲はフレームワークで処理します。直接実行する SQL や独自のデータアクセスには、別途権限チェックが必要です。

独立したサンプルアプリでは、顧客一覧を使ってアカウントごとの表示データの違いを確認できます。[複数組織のデータ権限サンプル](https://github.com/Tenon-Net/tenon-example/blob/dev/docs/showcase-multi-org-data-scope.md)を参照してください。

### チームに合わせて Vue と React を選択

Vue 版は Vue 3 と Naive UI、React 版は React 19 と Ant Design 6 を使用します。両方とも同じバックエンド API に接続するため、業務アプリではどちらか一方を選べます。

新しい画面では、テーブル、フォーム、辞書選択、組織・ユーザー選択、ファイルアップロード、権限制御のコンポーネントを再利用できます。例えば、顧客一覧の絞り込み、ページング、編集ダイアログ、ボタン権限は既存の画面とコンポーネントに沿って実装でき、モジュールごとに作り直す必要はありません。

API の型はバックエンドの OpenAPI から生成でき、API の変更に合わせて更新できます。各フロントエンドは依存関係とコンポーネントを個別に管理します。詳細は [Vue コンポーネント](web/COMPONENTS.md)と [React コンポーネント](web-react/COMPONENTS.md)のドキュメントにあります。

### 業務に合わせて承認フローを追加

承認ワークフローはオプションパッケージとして提供します。フローデザイナーで承認者、条件分岐、並列分岐、通知先を設定し、各ステップでフォーム項目を閲覧・編集できる範囲を指定できます。フローを公開すると、ユーザーは申請の提出、承認待ちタスクの処理、進捗や処理履歴の確認ができます。

承認画面では、差し戻し、取り下げ、担当者変更、委任、承認者の追加・削除に対応します。フロー管理者は実行状態、タイムアウト、再試行の記録を確認できます。Vue と React の両方に、同じバックエンド API を使うワークフロー画面があります。

`TenonAdmin` と同じバージョンの `TenonAdmin.Workflow` をインストールして登録してください。AI 評価は参考用の結果を記録するもので、自動で承認、却下、フローの進行を行うことはありません。導入と設定は[ワークフローのドキュメント](https://tenon.52moyu.net/guide/workflow)にあります。

### AI コーディングアシスタントに開発規約を共有

リポジトリには、モジュール、エンティティ、API、画面、サービス拡張、インポート・エクスポートなどの開発 Skills を用意しています。実装手順、参考コード、確認項目を含むため、AI コーディングアシスタントに先に読ませることで、プロジェクトのエンティティ、サービス、権限、コンポーネントの規約に沿ったコード作成を進められます。

例えば、次のように依頼できます。

> `skills/new-module.md` を参照し、製品名、コード、分類、有効状態を持つ製品管理モジュールを追加してください。分類による絞り込み、インポート・エクスポート、メニューとボタンの権限も含めてください。

Skills は開発手順と参考テンプレートであり、独立したコードジェネレーターではありません。生成後は業務ルール、権限、テスト結果を確認してください。一覧は[開発 Skills](skills/README.md)にあります。

## クイックスタート

バックエンドには .NET 10 SDK が必要です。フロントエンドには Node.js 22.12 以降を推奨します。まずリポジトリのサンプルを動かし、業務に組み込む際は下記の独立プロジェクトへの導入手順を使ってください。

> フロントエンドのテンプレートはバックエンドのバージョンに合わせ、拡張パッケージは `TenonAdmin` と同じバージョンを使ってください。更新前に[更新履歴](CHANGELOG.md)を確認してください。

### サンプル一式を実行

`dev` ブランチをクローンしてバックエンドを起動します。

```bash
git clone --branch dev --single-branch https://github.com/Tenon-Net/TenonAdmin.git
cd TenonAdmin
dotnet run --project backend/samples/MinimalHost
```

バックエンドは `http://localhost:5100` で動作します。サンプルは既定で SQLite を使用し、初回の初期化時にテーブルと `superAdmin` アカウントを作成します。

初期のランダムなパスワードは、**アカウントを最初に作成したときだけ**コンソールに表示されます。ログインに使うため、保存してください。

別のターミナルを開き、リポジトリのルートから使用するフロントエンドを起動します。

**Vue：**

```bash
cd web
npm install
npm run dev
```

`http://localhost:5173` を開き、`superAdmin` とコンソールのパスワードでログインします。

**React：**

```bash
cd web-react
npm install
npm run dev
```

`http://localhost:5174` を開き、同じアカウントでログインします。フロントエンドはどちらか一方でよく、バックエンドを複数起動する必要はありません。ログイン後は初期パスワードを変更してください。

### 自分の ASP.NET Core プロジェクトに導入

プロジェクトのディレクトリでバックエンドパッケージをインストールします。

```bash
dotnet add package TenonAdmin
```

新しいホストの最小構成の `Program.cs` は次のとおりです。

```csharp
using TenonAdmin.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// TenonAdmin 的核心接入
builder.Services.AddTenonAdmin(builder.Configuration);
var app = builder.Build();
app.MapTenonAdmin();

app.Run();
```

`AddTenonAdmin` はフレームワークのサービスを登録し、`MapTenonAdmin` は管理 API を追加します。既存プロジェクトでは、この 2 か所の呼び出しを元の起動処理に組み込み、アプリケーションの設定、サービス、業務ルートは残してください。

管理画面にはバックエンドのバージョンに合った Vue または React のテンプレートを使います。既定のデータベースは SQLite です。MySQL、SQL Server、PostgreSQL を使う場合は、設定でデータベースの種類と接続文字列を指定します。

データベース設定、新規プロジェクトのテンプレート、既存アプリへの導入手順は[導入ガイド](https://tenon.52moyu.net/guide/getting-started)にあります。Excel、外部ログイン、Redis、ワークフローの拡張は必要に応じてインストールし、各ドキュメントに従って登録と設定を行ってください。

## 機能一覧

| 分類 | 主な機能 |
| --- | --- |
| ユーザーと組織 | ユーザー、ロール、組織ツリー、役職、オンラインセッション、強制ログアウト |
| 権限管理 | メニュー、ボタン、API の権限。全データ、所属組織、所属組織と配下の組織、本人、指定組織のデータ範囲 |
| ログインとセキュリティ | アカウントとパスワード、画像認証、ログインロック、リクエスト制限。設定による多要素認証、必要に応じた SMS・外部ログイン連携 |
| 日常の管理 | 複数アプリのポータル、辞書、設定、お知らせ、プッシュ通知、操作ログ、ごみ箱 |
| ファイル管理 | アップロードとダウンロード、署名付きアクセス、分割アップロード、再開可能なアップロード、既存ファイルの即時アップロード |
| Excel インポート・エクスポート（オプション） | 取込テンプレート、取込データの確認、セル単位の検証、重複チェック、エラーレポート、出力列の選択 |
| 定期ジョブ | スケジュール、一定間隔、単発の実行。コード・HTTP・SQL ジョブ、実行ログ、タイムアウト、再試行、失敗通知。SQL 実行は設定で有効化が必要 |
| 承認ワークフロー（オプション） | フロー設計、動的フォーム、ステップごとの項目権限、承認処理、タイムアウト処理、実行履歴 |
| フロントエンド | Vue と React のテンプレート、中国語・英語、ライト・ダークテーマ、レイアウト設定、業務コンポーネント、OpenAPI 型生成 |
| データとデプロイ | SQLite、MySQL、SQL Server、PostgreSQL、複数データベース接続、独立したジョブプロセス、Docker、ヘルスチェック、Redis キャッシュ拡張 |

本番環境の設定と複数レプリカの構成は[デプロイガイド](docs/deployment.md)を参照してください。ワークフローから外部システムにメッセージを送る場合は、実際に送信する実装が必要です。詳細は[ワークフローのドキュメント](https://tenon.52moyu.net/guide/workflow)にあります。

## スクリーンショット

画像をクリックすると原寸で表示できます。

<!-- 每行两张，使用仓库中的等比缩略图；新增截图时继续追加 <tr>，并同步原图与缩略图。 -->
<table>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/vue-admin.png"><img src="docs/screenshots/thumbs/vue-admin.png" alt="Vue ユーザー管理" width="480"></a>
      <br>Vue ユーザー管理
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/react-admin.png"><img src="docs/screenshots/thumbs/react-admin.png" alt="React ユーザー管理" width="480"></a>
      <br>React ユーザー管理
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/role-permissions.png"><img src="docs/screenshots/thumbs/role-permissions.png" alt="ロール権限" width="480"></a>
      <br>ロール権限
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/org-management.png"><img src="docs/screenshots/thumbs/org-management.png" alt="組織管理" width="480"></a>
      <br>組織管理
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/dictionary.png"><img src="docs/screenshots/thumbs/dictionary.png" alt="辞書管理" width="480"></a>
      <br>辞書管理
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/file-management.png"><img src="docs/screenshots/thumbs/file-management.png" alt="ファイル管理" width="480"></a>
      <br>ファイル管理
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/scheduled-jobs.png"><img src="docs/screenshots/thumbs/scheduled-jobs.png" alt="定期ジョブ" width="480"></a>
      <br>定期ジョブ
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/workflow-pending.png"><img src="docs/screenshots/thumbs/workflow-pending.png" alt="承認待ち" width="480"></a>
      <br>承認待ち
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/workflow-designer.png"><img src="docs/screenshots/thumbs/workflow-designer.png" alt="フローデザイナー" width="480"></a>
      <br>フローデザイナー
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/workflow-approval.png"><img src="docs/screenshots/thumbs/workflow-approval.png" alt="承認の詳細" width="480"></a>
      <br>承認の詳細
    </td>
  </tr>
  <tr>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/integration-apps.png"><img src="docs/screenshots/thumbs/integration-apps.png" alt="接続アプリ" width="480"></a>
      <br>接続アプリ＊
    </td>
    <td width="50%" align="center" valign="top">
      <a href="docs/screenshots/delivery-tasks.png"><img src="docs/screenshots/thumbs/delivery-tasks.png" alt="確実な配信" width="480"></a>
      <br>確実な配信＊
    </td>
  </tr>
</table>

＊接続アプリと確実な配信の画像は、元の README から引き継いだものです。この文章の確認に使用した `dev` のスナップショットでは対応する実装を確認できていないため、この 2 枚は現在のブランチにシステム連携機能が含まれることを示すものではありません。

## ドキュメントと業務サンプル

[tenon-example](https://github.com/Tenon-Net/tenon-example) は独立した業務アプリです。バックエンドは NuGet で TenonAdmin を利用し、フロントエンドは管理画面テンプレートを基にしています。業務コードには CRM モジュールが含まれます。フレームワークの外で業務コードをどう構成するかを知るには、このプロジェクトが参考になります。

[オンラインデモ](https://tenonadmin.52moyu.net/login) はこのアプリを別途デプロイしたもので、機能はサンプルアプリのバージョンに従います。現在の `dev` ブランチを試す場合は、リポジトリのサンプルをローカルで実行してください。

| 目的 | ドキュメント |
| --- | --- |
| フレームワークの導入とデータベース設定 | [クイックスタート](https://tenon.52moyu.net/guide/getting-started) |
| 業務モジュールの追加 | [モジュール開発](skills/new-module.md) · [開発 Skills 一覧](skills/README.md) |
| フロントエンド画面の開発 | [Vue コンポーネント](web/COMPONENTS.md) · [React コンポーネント](web-react/COMPONENTS.md) |
| 組み込みサービスの調整 | [サービスの差し替えと拡張](skills/replace-service.md) |
| インポート・エクスポートと承認の追加 | [Excel インポート・エクスポート](skills/wire-import-export.md) · [承認ワークフロー](https://tenon.52moyu.net/guide/workflow) |
| バックエンド構造の確認 | [アーキテクチャ](https://tenon.52moyu.net/backend/architecture) |
| デプロイと更新 | [デプロイガイド](docs/deployment.md) · [更新履歴](CHANGELOG.md) |

<details>
<summary>リポジトリ構成</summary>

```text
TenonAdmin/
├── backend/
│   ├── src/          バックエンド NuGet パッケージのソース
│   ├── samples/      サンプルホストと独立したジョブプロセス
│   └── tests/        バックエンドのテスト
├── web/              Vue 管理画面
├── web-react/        React 管理画面
├── templates/        バックエンドのプロジェクトテンプレート
├── skills/           開発手順と参考コード
├── site/             ドキュメントサイト
└── docs/             アーキテクチャ、デプロイ、開発資料
```

</details>

## コントリビューション

開発は `dev` ブランチで行います。不具合は [Issue](https://github.com/Tenon-Net/TenonAdmin/issues) で報告し、使用バージョン、再現手順、関連ログを添えてください。コードの変更は `dev` 向けの PR として提出してください。

## 著作権とライセンス

Copyright © Tenon-Net

本プロジェクトは [Apache License 2.0](LICENSE) の下で、商用利用、変更、再配布ができます。

本プロジェクトまたは派生物を再配布する場合は、ライセンスの写しを添付し、変更したファイルに変更内容を明記し、配布するソースコードに該当する著作権、特許、商標、帰属表示を残してください。NOTICE ファイルの帰属表示を扱う場合は、ライセンス第 4 条に従ってください。

第三者のコンポーネントと素材には、それぞれのライセンスが適用されます。
