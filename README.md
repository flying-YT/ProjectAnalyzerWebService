# ProjectAnalyzerWebService

ProjectAnalyzerWebService は、アップロードされたプロジェクトの ZIP ファイルを解析し、解析結果を Markdown ファイルとしてまとめて ZIP 形式で返す .NET 9 の Web アプリケーションです。
内部の解析処理には `ProjectAnalyzer.Core` ライブラリを利用しています。

## 主な機能

- **プロジェクトの解析**: ソースコードなどが含まれる ZIP ファイルを受け取り、内容を展開して解析します。
- **各種オプションの設定**: 解析時に以下のフラグを指定できます。
  - `omitCodeBlockTicks`
  - `outputPerFile`
  - `sanitizeHtmlTags`
  - `removeIndent`
  - `enableOcr`
- **Shift_JIS 対応**: Windows 環境での文字化けを防ぐため、ZIP ファイルの展開および圧縮時に `Shift_JIS` エンコーディングを明示的に使用しています。
- **Web フロントエンド**: `wwwroot` フォルダに配置された静的ファイル（`index.html` など）を通じて、ブラウザから簡単に解析処理を実行できる UI を提供します。

## 前提条件

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## ローカルでの実行方法

1. リポジトリをクローンまたはダウンロードし、プロジェクトのルートディレクトリ（`.csproj` のある場所）でターミナルを開きます。
2. 以下のコマンドを実行してアプリケーションを起動します。

```bash
dotnet run
```

3. ブラウザを開き、コンソールに表示された URL（例: `http://localhost:5000`）にアクセスしてフロントエンドを利用します。

## API エンドポイント

- **URL**: `POST /api/analyze`
- **Content-Type**: `multipart/form-data`
- **パラメータ**:
  - `file` (File): 解析対象の ZIP ファイル
  - `omitCodeBlockTicks` (Boolean)
  - `outputPerFile` (Boolean)
  - `sanitizeHtmlTags` (Boolean)
  - `removeIndent` (Boolean)
  - `enableOcr` (Boolean)
- **レスポンス**: 解析結果が含まれた ZIP ファイル (`AnalysisResult.zip`)

---

## IIS へのデプロイ方法

このアプリケーションを Windows サーバーの IIS (Internet Information Services) にデプロイする手順は以下の通りです。

### 1. サーバーの前提条件 (初回デプロイ時のみ)

ASP.NET Core アプリケーションを IIS で動かすためには、サーバーに専用のモジュールをインストールする必要があります。**IIS への初回デプロイ時のみ**、以下がインストールされていることを確認し、未インストールの場合は導入してください。

- **IIS** (Windows の機能の有効化からインストール)
- **[.NET 9.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0)**
  - ASP.NET Core アプリを IIS でホストするためのモジュール (ANCM) と .NET ランタイムが含まれる重要なパッケージです。リンク先の「ASP.NET Core Hosting Bundle」をダウンロードしてインストールしてください。
  - **注意**: インストール完了後、コマンドプロンプト（管理者）で `net stop was /y` および `net start w3svc` を実行して、IIS を再起動しモジュールを反映させてください。

### 2. アプリケーションの発行 (Publish)

開発環境のコマンドプロンプトまたはターミナルで、プロジェクトのルートディレクトリに移動し、以下のコマンドを実行して発行済みファイルを作成します。

```bash
dotnet publish ProjectAnalyzerWebService.csproj -c Release -o ./publish_output
```
これにより、`publish_output` フォルダ内にデプロイに必要なすべてのファイルが生成されます。

> **補足**: 発行先の `publish_output` はプロジェクト配下にあるため、`.csproj` を明示的に対象にしています（ソリューションを対象にすると `NETSDK1194` の警告が出ます）。また `publish_output` は `.gitignore` およびプロジェクトの `DefaultItemExcludes` で除外しているため、再発行を繰り返してもフォルダが入れ子にならず、コミット対象にもなりません。

### 3. ファイルの配置

生成された `publish_output` フォルダ内のすべてのファイルを、IIS サーバー上の任意の公開用フォルダ（例: `C:\inetpub\wwwroot\ProjectAnalyzer`）にコピーします。

### 4. IIS でのサイト作成

1. IIS マネージャーを開きます。
2. 左側の接続ペインで「サイト」を右クリックし、「Web サイトの追加」を選択します。
3. 以下の情報を入力して「OK」をクリックします。
   - **サイト名**: 任意の名前（例: `ProjectAnalyzerSite`）
   - **物理パス**: 手順3でファイルを配置したフォルダのパス（例: `C:\inetpub\wwwroot\ProjectAnalyzer`）
   - **ポート**: 空いているポート（例: `80` または `8080`）

### 5. アプリケーションプールの設定 (重要)

ASP.NET Core アプリケーションは IIS のマネージドコードに依存しないため、アプリケーションプールの設定を変更する必要があります。

1. IIS マネージャーの左側のペインで「アプリケーション プール」を選択します。
2. 手順4で作成したサイトと同名のアプリケーションプール（例: `ProjectAnalyzerSite`）を右クリックし、「基本設定」を選択します。
3. **.NET CLR バージョン** を **「マネージド コードなし (No Managed Code)」** に変更して「OK」をクリックします。

### 6. 動作確認

ブラウザからサーバーの URL (設定したポート番号) にアクセスし、フロントエンドの画面が表示されること、および ZIP ファイルの解析が正常に行えることを確認します。

---

## アップロードサイズの上限について

大きな ZIP ファイルをアップロードした際に、以下のような IIS のエラー画面が表示されることがあります。

> エラー: 要求のエンティティが大きすぎるため、ページを表示できませんでした。

これは HTTP 404.13 (Request Filtering: Content length too large) で、リクエストサイズが上限を超えたときに **アプリケーションに到達する前に IIS が返す** ものです。本アプリのアップロード上限は **100MB** で、これを実現するために以下の 3 か所を設定しています。上限を変更する場合はすべて同じ値に揃えてください。

| 制限層 | 設定箇所 | 既定値 | 本アプリの設定値 |
| --- | --- | --- | --- |
| IIS リクエストフィルタリング | `web.config` の `maxAllowedContentLength` | 30,000,000 バイト (≒28.6MB) | 104,857,600 バイト (100MB) |
| アプリ (Kestrel / IIS インプロセス) | `Program.cs` の `MaxUploadBytes` | 30,000,000 バイト (≒28.6MB) | 104,857,600 バイト (100MB) |
| フォーム (multipart) | `Program.cs` の `MaxUploadBytes` | 128MB | 104,857,600 バイト (100MB) |

### 上限を変更する手順

1. **アプリ側**: [`Program.cs`](Program.cs) 先頭の `MaxUploadBytes` を変更します。これにより Kestrel・IIS インプロセス（`IHttpMaxRequestBodySizeFeature` 経由）・フォーム制限が一括で変わります。
2. **IIS 側**: プロジェクトルートの [`web.config`](web.config) 内 `requestLimits` の `maxAllowedContentLength`（バイト単位）を同じ値に変更します。
   - この `web.config` は発行 (`dotnet publish`) 時に IIS 用へ自動変換されますが、`requestFiltering` の設定は保持されるため、**再発行のたびに手作業で編集する必要はありません**。

> **補足 (IIS インプロセスホスティング)**: `hostingModel="inprocess"` では Kestrel の `MaxRequestBodySize` 設定は無視されます。本アプリはこの制約に対応するため、リクエスト単位で上限を設定するミドルウェア（`IHttpMaxRequestBodySizeFeature`）を用いており、Kestrel と IIS の双方で同じ上限が適用されます。

> **注意**: 発行済みフォルダを IIS サーバー上で直接運用しており、`web.config` を手動で編集した場合は、`maxAllowedContentLength` の変更のみであれば IIS の再起動は不要です（数十秒で反映されます）。IIS マネージャーの「要求フィルター」→「機能設定の編集」からも同じ値を設定できます。
