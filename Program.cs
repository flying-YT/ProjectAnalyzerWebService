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

// .NET環境でShift_JISなどのエンコーディングを使用できるようにプロバイダーを登録
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

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

        // Shift_JISのエンコーディングオブジェクトを取得
        var shiftJisEncoding = Encoding.GetEncoding("Shift_JIS");

        // 1. アップロードされたZIPファイルを保存
        using (var stream = new FileStream(uploadZipPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // 2. 展開前にZIP爆弾（解凍爆弾）対策のチェックを行う
        using (var archive = ZipFile.Open(uploadZipPath, ZipArchiveMode.Read, shiftJisEncoding))
        {
            if (archive.Entries.Count > MaxEntryCount)
            {
                return Results.BadRequest("ZIP内のファイル数が多すぎます。");
            }

            long totalUncompressed = 0;
            foreach (var entry in archive.Entries)
            {
                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxExtractedBytes)
                {
                    return Results.BadRequest("展開後のサイズが上限を超えています。");
                }
            }
        }

        // 3. ZIPファイルを展開　第3引数にShift_JISを指定して展開時の文字化けを防ぐ
        ZipFile.ExtractToDirectory(uploadZipPath, extractTargetDir, shiftJisEncoding);

        // 4. ProjectAnalyzerの設定と実行
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

        // 5. 出力されたMarkdownファイル群をZIP化　圧縮時にもShift_JISを指定してWindows標準機能で解凍しやすくする
        ZipFile.CreateFromDirectory(outputDir, resultZipPath, CompressionLevel.Optimal, false, shiftJisEncoding);

        // 6. 結果ZIPをストリームとして返す（メモリ全読みを避ける）
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