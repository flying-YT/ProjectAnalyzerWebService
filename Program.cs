using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;

var builder = WebApplication.CreateBuilder(args);
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
    [FromForm] bool enableOcr) =>
{
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
    var resultZipPath = Path.Combine(tempWorkDir, "result.zip");

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

        // 2. ZIPファイルを展開　第3引数にShift_JISを指定して展開時の文字化けを防ぐ
        ZipFile.ExtractToDirectory(uploadZipPath, extractTargetDir, shiftJisEncoding);

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

        // 4. 出力されたMarkdownファイル群をZIP化　圧縮時にもShift_JISを指定してWindows標準機能で解凍しやすくする
        ZipFile.CreateFromDirectory(outputDir, resultZipPath, CompressionLevel.Optimal, false, shiftJisEncoding);

        // 5. ZIPファイルをメモリに読み込む（読み込み後に一時フォルダを削除するため）
        var fileBytes = await File.ReadAllBytesAsync(resultZipPath);

        return Results.File(fileBytes, "application/zip", "AnalysisResult.zip");
    }
    catch (Exception ex)
    {
        return Results.Problem($"処理中にエラーが発生しました: {ex.Message}");
    }
    finally
    {
        // 6. 処理が終わったら一時ディレクトリを削除してクリーンアップ
        if (Directory.Exists(tempWorkDir))
        {
            Directory.Delete(tempWorkDir, true);
        }
    }
})
.DisableAntiforgery(); // 簡易的なフォーム制動のためCSRFチェックを無効化（必要に応じて設定）

app.Run();