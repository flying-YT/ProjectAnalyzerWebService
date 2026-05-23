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

### 1. サーバーの前提条件

デプロイ先のサーバーに以下がインストールされていることを確認してください。
- **IIS** (Windows の機能の有効化からインストール)
- **.NET 9.0 Hosting Bundle** (ASP.NET Core ランタイムと IIS 用モジュールが含まれています)

### 2. アプリケーションの発行 (Publish)

開発環境のコマンドプロンプトまたはターミナルで、プロジェクトのルートディレクトリに移動し、以下のコマンドを実行して発行済みファイルを作成します。

```bash
dotnet publish -c Release -o ./publish_output
```
これにより、`publish_output` フォルダ内にデプロイに必要なすべてのファイルが生成されます。

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
