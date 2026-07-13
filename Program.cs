using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;

var builder = WebApplication.CreateBuilder(args);

// アップロードサイズの上限を明示的に設定（既定値への暗黙依存を避ける）
const long MaxUploadBytes = 100L * 1024 * 1024; // 100MB
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

var app = builder.Build();

// リクエストボディの上限をリクエスト単位で明示的に引き上げる。
// IISインプロセスホスティングではKestrelのMaxRequestBodySize設定は無視され、
// 既定の約28.6MB(30,000,000バイト)を超えるとサーバー層でHTTP 413が返る。
// このFeatureはKestrel・IIS(インプロセス)双方が実装しており、ボディ読み取り前に
// 設定することで両環境で上限を統一できる（[RequestSizeLimit]と同じ仕組み）。
// なおIIS自体のリクエストフィルタリング上限(maxAllowedContentLength)はweb.config側で別途設定する。
app.Use(async (context, next) =>
{
    var maxBodySizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (maxBodySizeFeature is { IsReadOnly: false })
    {
        maxBodySizeFeature.MaxRequestBodySize = MaxUploadBytes;
    }
    await next();
});

// .NET環境でShift_JISなどのエンコーディングを使用できるようにプロバイダーを登録
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// ZIPエントリ名のデコードに使うエンコーディング（プロバイダー登録後に取得）
var shiftJisEncoding = Encoding.GetEncoding("Shift_JIS");
var strictUtf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

// ZIPエントリ名のエンコーディングを判定してデコードする。
// Windowsエクスプローラ製ZIPはShift_JIS、macOS Finder製ZIPはUTF-8でファイル名を格納し、
// どちらも言語エンコーディングフラグを立てないため、固定エンコーディングでは両環境を両立できない。
// アーカイブをLatin1で開いて生バイトを保持し、UTF-8として妥当ならUTF-8、不正ならShift_JISとみなす。
string DecodeZipEntryName(string rawName)
{
    // 0xFFを超える文字が含まれる場合は、.NETが言語エンコーディングフラグを見てUTF-8で
    // デコード済み（=Latin1の生バイトではない）ため、そのまま使用する。
    foreach (var ch in rawName)
    {
        if (ch > 0xFF) return rawName;
    }

    var rawBytes = Encoding.Latin1.GetBytes(rawName);
    try
    {
        return strictUtf8Encoding.GetString(rawBytes);
    }
    catch (DecoderFallbackException)
    {
        return shiftJisEncoding.GetString(rawBytes);
    }
}

// 静的ファイル（HTMLなど）を配信できるようにする（wwwrootフォルダ用）
app.UseStaticFiles();

// ZIPアップロードと分析処理のAPIエンドポイント
// フロントエンドから送信される設定値を受け取るため、[FromForm]を追加
app.MapPost("/api/analyze", async (
    [FromForm] IFormFile file,
    [FromForm] bool omitCodeBlockTicks,
    [FromForm] bool outputPerFile,
    [FromForm] bool sanitizeHtmlTags,
    [FromForm] bool removeIndent,
    [FromForm] bool enableOcr,
    ILogger<Program> logger) =>
{
    // 展開後サイズ・エントリ数の上限（ZIP爆弾対策）
    const long MaxExtractedBytes = 500L * 1024 * 1024; // 500MB
    const int MaxEntryCount = 10_000;

    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("ファイルが選択されていないか、空です。");
    }

    if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("ZIPファイルをアップロードしてください。");
    }

    // 作業用の一時ディレクトリを作成
    var tempWorkDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    var uploadZipPath = Path.Combine(tempWorkDir, "uploaded.zip");
    var extractTargetDir = Path.Combine(tempWorkDir, "source");
    var outputDir = Path.Combine(tempWorkDir, "output");
    // 結果ZIPは作業ディレクトリの外に置き、ストリーム返却後に単体で自動削除する
    var resultZipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".zip");

    try
    {
        Directory.CreateDirectory(extractTargetDir);
        Directory.CreateDirectory(outputDir);

        // 1. アップロードされたZIPファイルを保存
        using (var stream = new FileStream(uploadZipPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 2. ZIPを手動で展開する。エントリ名のエンコーディングを自動判定（Win=Shift_JIS / mac=UTF-8）
        //    しつつ、ZIP爆弾対策（サイズ・件数の上限）とパストラバーサル対策を同時に行う。
        //    Latin1で開くことで、フラグ未設定エントリのファイル名を生バイトのまま取得する。
        using (var archive = ZipFile.Open(uploadZipPath, ZipArchiveMode.Read, Encoding.Latin1))
        {
            if (archive.Entries.Count > MaxEntryCount)
            {
                return Results.BadRequest("ZIP内のファイル数が多すぎます。");
            }

            // パストラバーサル判定の基準となる展開先ルートの絶対パス（末尾に区切り文字を付与）
            var extractRoot = Path.GetFullPath(extractTargetDir) + Path.DirectorySeparatorChar;

            long totalUncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxExtractedBytes)
                {
                    return Results.BadRequest("展開後のサイズが上限を超えています。");
                }

                var decodedName = DecodeZipEntryName(entry.FullName);

                // パストラバーサル対策：展開先ルート配下に収まることを検証
                var destinationPath = Path.GetFullPath(Path.Combine(extractTargetDir, decodedName));
                if (!destinationPath.StartsWith(extractRoot, StringComparison.Ordinal))
                {
                    return Results.BadRequest("不正なパスを含むエントリが含まれています。");
                }

                // ディレクトリエントリ（名前が区切り文字で終わる）は作成のみ
                if (decodedName.EndsWith('/') || decodedName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        // 3. ProjectAnalyzerの設定と実行
        // 画面から受け取ったフラグを設定に反映
        var settings = SettingsLoader.Load(
            projectPath: extractTargetDir,
            outputPath: outputDir,
            outputToFile: true, // これはシステム要件として固定
            omitCodeBlockTicks: omitCodeBlockTicks,
            outputPerFile: outputPerFile,
            sanitizeHtmlTags: sanitizeHtmlTags,
            removeIndent: removeIndent,
            enableOcr: enableOcr
        );

        // CoreのAnalyzerを使用して解析を実行
        using (var analyzer = new Analyzer(settings))
        {
            analyzer.Analyze();
        }

        // 4. 出力されたMarkdownファイル群をZIP化する。
        //    エンコーディングを指定しない（null）ことで、非ASCIIのファイル名はUTF-8で書き込まれ、
        //    言語エンコーディングフラグも付与される。これによりmacOSと最新Windowsの双方で
        //    正しく解凍できる（クロスプラットフォーム対応）。
        ZipFile.CreateFromDirectory(outputDir, resultZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        // 5. 結果ZIPをストリームとして返す（メモリ全読みを避ける）
        //    FileOptions.DeleteOnClose により、レスポンス送信完了後にファイルが自動削除される
        var resultStream = new FileStream(
            resultZipPath, FileMode.Open, FileAccess.Read, FileShare.None,
            bufferSize: 81920, options: FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        return Results.File(resultStream, "application/zip", "AnalysisResult.zip");
    }
    catch (Exception ex)
    {
        // 詳細はサーバーログにのみ記録し、利用者には汎用メッセージを返す
        logger.LogError(ex, "ZIP分析処理中にエラーが発生しました。");

        // 例外発生時は返却前の結果ZIPが残る可能性があるため削除する
        if (File.Exists(resultZipPath))
        {
            File.Delete(resultZipPath);
        }

        return Results.Problem("処理中にエラーが発生しました。時間をおいて再度お試しください。");
    }
    finally
    {
        // 処理が終わったら作業用一時ディレクトリを削除してクリーンアップ
        if (Directory.Exists(tempWorkDir))
        {
            Directory.Delete(tempWorkDir, true);
        }
    }
})
.DisableAntiforgery(); // 簡易的なフォーム制動のためCSRFチェックを無効化（必要に応じて設定）

app.Run();