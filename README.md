# ProjectAnalyzerWebService

ProjectAnalyzerWebService は、アップロードされたプロジェクトの ZIP ファイルを解析し、解析結果を Markdown ファイルとしてまとめて ZIP 形式で返す .NET 9 の Web アプリケーションです。
内部の解析処理には `ProjectAnalyzer.Core` ライブラリを利用しています。

## 主な機能

- **プロジェクトの解析**: ソースコードなどが含まれる ZIP ファイルを受け取り、内容を展開して解析します。
- **各種オプションの設定**: 解析時に以下のオプションを指定できます。
  - `omitCodeBlockTicks`: コードブロック記号（\`\`\`）を省略します。
  - `outputPerFile`: 元の階層を保ったままファイルごとに Markdown を出力します。
  - `sanitizeHtmlTags`: `<details>` などの HTML タグを全角に置換します。
  - `removeIndent`: 行頭のインデントを削除します。
  - `enableOcr`: Office ファイルに埋め込まれた画像を OCR でテキスト化します（`ProjectAnalyzer.Core` 1.4.0 以降は PaddleOCR の PP-OCRv6 small モデルを ONNX Runtime 上で実行します）。
  - `maxOutputSizeMb`: 出力 Markdown 1 ファイルあたりの分割しきい値（MB）です。
- **Shift_JIS 対応**: Windows 環境での文字化けを防ぐため、ZIP ファイルの展開および圧縮時に `Shift_JIS` エンコーディングを明示的に使用しています。
- **Web フロントエンド**: `wwwroot` フォルダに配置された静的ファイル（`index.html` など）を通じて、ブラウザから簡単に解析処理を実行できる UI を提供します。各オプションには常時表示の短い説明と、マウスオーバー（タップ／キーボードフォーカスにも対応）で開く詳細ツールチップを備えています。
- **制限事項の明示**: 画面下部の「制限事項・対応ファイル形式について」パネルに、アップロード上限・パスワード付きファイルの扱い・対応／非対応のファイル形式をまとめて記載しています。

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
  - `maxOutputSizeMb` (Number, 任意): 出力 Markdown 1 ファイルあたりの分割しきい値（MB）。`0.5`〜`50` の範囲で指定します。省略時は `ProjectAnalyzer.Core` の既定値（4MB）が使われます。値はサーバーのロケールに依存しないよう `InvariantCulture`（小数点はピリオド）で解釈されます。
- **レスポンス**: 解析結果が含まれた ZIP ファイル (`AnalysisResult.zip`)

---

## 制限事項

画面の「制限事項・対応ファイル形式について」パネルと同じ内容です。

### アップロードの上限

| 項目 | 上限 |
| --- | --- |
| ZIP ファイルのサイズ | 100MB |
| 展開後の合計サイズ | 500MB |
| ZIP 内のファイル数 | 10,000 件 |

### パスワード付きファイル

- **暗号化 ZIP（パスワード付き ZIP）は展開できません。** 解析がエラーになるため、パスワードを解除してからアップロードしてください。
- ZIP 内の**読み取りパスワード付き Office ファイル**（Excel / Word / PowerPoint）は開けないため、そのファイルのみスキップされ、出力に含まれません。
- 「読み取り専用推奨」や**書き込みパスワードのみ**のファイルは通常どおり解析できます。

### 対応ファイル形式

| 区分 | 形式 |
| --- | --- |
| Excel | `.xlsx` `.xlsm` `.xls`（図形・テキストボックスは `.xlsx` `.xlsm` のみ） |
| Word | `.docx` `.docm`（表は Markdown のテーブルに変換） |
| PowerPoint | `.pptx` |
| テキスト全般 | ソースコード、`.md`、`.json`、`.xml`、`.csv` など |

### 内容を抽出できない形式（原本をそのまま同梱）

- 旧形式の Office・RTF: `.doc` `.ppt` `.rtf`（Excel は `.xls` のみ対応）
- PDF: `.pdf`
- 単体の画像ファイル: `.png` `.jpg` `.gif` など（OCR は Office ファイルに**埋め込まれた**画像のみが対象）
- その他のバイナリ: 実行ファイル、動画、音声、圧縮ファイル、フォントなど

> これらは内容を抽出せず、**原本を結果 ZIP 内の `02_SkippedFiles/` フォルダへ元の階層のまま同梱**します（Markdown 側には `[Skipped: unsupported binary file ...]` の 1 行のみ出力）。原本がそのまま入るため、不要なバイナリが多いと結果 ZIP が大きくなります。不要なものはアップロード前に ZIP から除外するか、`.projectanalyzerignore` で除外してください。

### 除外設定

- `bin` / `obj` / `.vs` / `.git` は既定で除外されます。
- シンボリックリンクは辿らず、内容は読み込みません（フォルダツリーには `[symbolic link, not followed]` の注記付きで表示されます）。
- プロジェクト直下に `.projectanalyzerignore` を置くと、1 行に 1 つずつ除外対象を追加指定できます。

---

## 発行 (Publish) とリリース資材の作成

本アプリは **Windows x64**（IIS）と **Linux x64** の 2 環境向けに発行します。開発環境のターミナルでプロジェクトのルートディレクトリに移動し、以下のコマンドを実行します。

```bash
# 前回の発行物が残らないよう、発行先を削除してから発行する
rm -rf publish_output

dotnet publish ProjectAnalyzerWebService.csproj -c Release -r win-x64 --self-contained false -o ./publish_output/win-x64
dotnet publish ProjectAnalyzerWebService.csproj -c Release -r linux-x64 --self-contained false -o ./publish_output/linux-x64
```

GitHub の Release に添付する場合は、それぞれのフォルダの中身を zip にまとめます（`X.Y.Z` はリリースするバージョンに置き換えてください）。

```bash
(cd publish_output/win-x64 && zip -r ../ProjectAnalyzerWebService-vX.Y.Z-win-x64.zip .)
(cd publish_output/linux-x64 && zip -r ../ProjectAnalyzerWebService-vX.Y.Z-linux-x64.zip .)
```

| ファイル | 対象環境 | 発行先フォルダ |
| --- | --- | --- |
| `ProjectAnalyzerWebService-vX.Y.Z-win-x64.zip` | Windows x64（IIS） | `publish_output/win-x64` |
| `ProjectAnalyzerWebService-vX.Y.Z-linux-x64.zip` | Linux x64（glibc 系: Ubuntu / Debian / RHEL など） | `publish_output/linux-x64` |

> **補足 (RID を指定する理由)**: OCR で使用する ONNX Runtime と SkiaSharp はネイティブライブラリを含みます。`-r`（RID）を指定せずに発行すると、iOS / Android / macOS などを含む全プラットフォーム分のネイティブライブラリが `runtimes/` 以下にコピーされ、発行物が約 400MB（zip で約 250MB）になります。RID を指定すると対象環境の分だけになり、それぞれ 70〜80MB 程度（zip で 40MB 台）に収まります。その代わり、**発行した環境以外では OCR が動作しません**（例: win-x64 版を Linux で動かすことはできません）。

> **補足 (`--self-contained false`)**: .NET ランタイムは同梱せず、サーバーにインストールされたランタイムを使用します。各環境のサーバーの前提条件は、以下のデプロイ手順を参照してください。

> **補足 (発行先の除外)**: 発行先の `publish_output` はプロジェクト配下にあるため、`.csproj` を明示的に対象にしています（ソリューションを対象にすると `NETSDK1194` の警告が出ます）。また `publish_output` は `.gitignore` およびプロジェクトの `DefaultItemExcludes` で除外しているため、再発行を繰り返してもフォルダが入れ子にならず、作成した zip もコミット対象になりません。

---

## IIS へのデプロイ方法

このアプリケーションを Windows サーバーの IIS (Internet Information Services) にデプロイする手順は以下の通りです。

### 1. サーバーの前提条件 (初回デプロイ時のみ)

ASP.NET Core アプリケーションを IIS で動かすためには、サーバーに専用のモジュールをインストールする必要があります。**IIS への初回デプロイ時のみ**、以下がインストールされていることを確認し、未インストールの場合は導入してください。

- **IIS** (Windows の機能の有効化からインストール)
- **[.NET 9.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/9.0)**
  - ASP.NET Core アプリを IIS でホストするためのモジュール (ANCM) と .NET ランタイムが含まれる重要なパッケージです。リンク先の「ASP.NET Core Hosting Bundle」をダウンロードしてインストールしてください。
  - **注意**: インストール完了後、コマンドプロンプト（管理者）で `net stop was /y` および `net start w3svc` を実行して、IIS を再起動しモジュールを反映させてください。

### 2. 資材の用意

[発行 (Publish) とリリース資材の作成](#発行-publish-とリリース資材の作成) の手順で発行した `publish_output/win-x64` フォルダ、または Release からダウンロードした `ProjectAnalyzerWebService-vX.Y.Z-win-x64.zip` を用意します。

### 3. ファイルの配置

`publish_output/win-x64` フォルダ内（または zip を展開したフォルダ内）のすべてのファイルを、IIS サーバー上の任意の公開用フォルダ（例: `C:\inetpub\wwwroot\ProjectAnalyzer`）にコピーします。

> **注意 (更新時)**: 既存のバージョンを更新する場合は、公開用フォルダの中身を**丸ごと置き換えて**ください。差分コピーでは、不要になった旧バージョンのファイルが残る場合があります。

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
4. 同じアプリケーションプールの「詳細設定」で **「32 ビット アプリケーションの有効化」** が **False** であることを確認します。
   - OCR で使用する ONNX Runtime は 32 ビット (x86) 版のネイティブライブラリを提供していないため、True だと OCR を有効にした解析がエラーになります。

### 6. 動作確認

ブラウザからサーバーの URL (設定したポート番号) にアクセスし、フロントエンドの画面が表示されること、および ZIP ファイルの解析が正常に行えることを確認します。

---

## Linux へのデプロイ方法

このアプリケーションを Linux x64 サーバーで動かす手順は以下の通りです（Kestrel で直接起動します）。

### 1. サーバーの前提条件 (初回デプロイ時のみ)

- **[ASP.NET Core Runtime 9.0](https://learn.microsoft.com/ja-jp/dotnet/core/install/linux)** をインストールします。
  - 例 (Ubuntu): `sudo apt-get update && sudo apt-get install -y aspnetcore-runtime-9.0`
- OCR のネイティブライブラリは glibc 系ディストリビューション向けです。Alpine Linux などの musl 系では OCR が動作しません。
- OCR のために Tesseract などを別途インストールする必要はありません。

### 2. ファイルの配置

`publish_output/linux-x64` フォルダ、または Release からダウンロードした `ProjectAnalyzerWebService-vX.Y.Z-linux-x64.zip` の中身を、サーバー上の任意のフォルダ（例: `/opt/projectanalyzer`）に配置します。

```bash
sudo mkdir -p /opt/projectanalyzer
sudo unzip ProjectAnalyzerWebService-vX.Y.Z-linux-x64.zip -d /opt/projectanalyzer
```

> **注意 (更新時)**: 既存のバージョンを更新する場合は、フォルダの中身を**丸ごと置き換えて**ください。

### 3. 起動

待ち受けるアドレスとポートを `ASPNETCORE_URLS` で指定して起動します。

```bash
cd /opt/projectanalyzer
ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet ProjectAnalyzerWebService.dll
```

常駐させる場合は systemd のサービスとして登録します。以下は設定例です（`User` は環境に合わせて変更してください）。

```ini
# /etc/systemd/system/projectanalyzer.service
[Unit]
Description=ProjectAnalyzerWebService

[Service]
WorkingDirectory=/opt/projectanalyzer
ExecStart=/usr/bin/dotnet /opt/projectanalyzer/ProjectAnalyzerWebService.dll
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Restart=always
User=www-data

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now projectanalyzer
```

> **補足**: 解析処理では OS の一時フォルダ（通常は `/tmp`）を作業領域として使用します。展開後のサイズは最大 500MB になるため、一時フォルダに十分な空き容量を確保してください。

### 4. 動作確認

ブラウザから `http://<サーバーのアドレス>:5000` にアクセスし、フロントエンドの画面が表示されること、および ZIP ファイルの解析が正常に行えることを確認します。

> **補足 (リバースプロキシ)**: nginx などのリバースプロキシを前段に置く場合は、プロキシ側のリクエストサイズ上限もアップロード上限 (100MB) 以上にしてください（例: nginx の `client_max_body_size 100m;`）。上限が小さいと、アプリに到達する前に HTTP 413 が返ります。

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

> **補足 (Linux)**: Linux では `web.config` は使用されず（linux-x64 版の発行物には含まれません）、アプリ側の `MaxUploadBytes` のみが適用されます。リバースプロキシを使う場合は、プロキシ側の上限も合わせてください。

> **補足 (IIS インプロセスホスティング)**: `hostingModel="inprocess"` では Kestrel の `MaxRequestBodySize` 設定は無視されます。本アプリはこの制約に対応するため、リクエスト単位で上限を設定するミドルウェア（`IHttpMaxRequestBodySizeFeature`）を用いており、Kestrel と IIS の双方で同じ上限が適用されます。

> **注意**: 発行済みフォルダを IIS サーバー上で直接運用しており、`web.config` を手動で編集した場合は、`maxAllowedContentLength` の変更のみであれば IIS の再起動は不要です（数十秒で反映されます）。IIS マネージャーの「要求フィルター」→「機能設定の編集」からも同じ値を設定できます。
