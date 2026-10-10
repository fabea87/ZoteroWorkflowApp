using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public class AnydocService
{
    public string DefaultCacheDirectory { get; }

    public AnydocService()
    {
        DefaultCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZoteroWorkflowApp",
            "MarkdownVault"
        );

        if (!Directory.Exists(DefaultCacheDirectory))
        {
            Directory.CreateDirectory(DefaultCacheDirectory);
        }
    }

    public string GetActiveVaultDirectory(string? customVaultPath = null)
    {
        var dir = !string.IsNullOrWhiteSpace(customVaultPath) ? customVaultPath : DefaultCacheDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    public async Task<(string filePath, string content)> ConvertPaperToObsidianMarkdownAsync(
        ZotSeekResultItem item,
        string? customVaultPath = null,
        string engineMode = "PdfPig",
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var vaultDir = GetActiveVaultDirectory(customVaultPath);
        var safeFileName = $"{item.ItemKey}_{SanitizeFileName(item.Title)}.md";
        var outputPath = Path.Combine(vaultDir, safeFileName);

        // Check if already cached and up-to-date
        if (File.Exists(outputPath))
        {
            var existing = await File.ReadAllTextAsync(outputPath, Encoding.UTF8, ct);
            if (existing.Length > 400)
            {
                progress?.Report($"[Markdown 缓存命中] 直接读取 Vault 笔记: {Path.GetFileName(outputPath)}");
                item.AnydocMarkdownPath = outputPath;
                item.AnydocMarkdown = existing;
                if (string.IsNullOrEmpty(item.Doi))
                {
                    item.Doi = ExtractDoiFromText(existing);
                }
                return (outputPath, existing);
            }
        }

        string rawBody = string.Empty;
        string usedEngine = "MetadataOnly";
        var sw = Stopwatch.StartNew();

        if (!string.IsNullOrEmpty(item.ResolvedPdfPath) && File.Exists(item.ResolvedPdfPath))
        {
            var isPdf = item.ResolvedPdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (isPdf && (string.Equals(engineMode, "PdfPig", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(engineMode, "Auto", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    progress?.Report($"[PdfPig 原生引擎] 正在极速提取双栏学术排版: {Path.GetFileName(item.ResolvedPdfPath)} ...");
                    rawBody = await Task.Run(() => ExtractPdfWithPdfPigLayout(item.ResolvedPdfPath, ct), ct);
                    usedEngine = "UglyToad.PdfPig (RecursiveXYCut Academic Layout)";
                }
                catch (Exception ex)
                {
                    progress?.Report($"[PdfPig 告警] {ex.Message}，尝试回退至 anydoc...");
                }
            }

            // Use anydoc if explicitly selected or if PdfPig returned empty (e.g. non-PDF or weird encoding)
            if (string.IsNullOrWhiteSpace(rawBody) || string.Equals(engineMode, "Anydoc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(engineMode, "Anydoc", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(rawBody))
                {
                    progress?.Report($"[anydoc CLI] 正在转换文档: {Path.GetFileName(item.ResolvedPdfPath)} ...");
                    var anydocText = await ConvertViaAnydocCliAsync(item.ResolvedPdfPath, progress, ct);
                    if (!string.IsNullOrWhiteSpace(anydocText))
                    {
                        rawBody = anydocText;
                        usedEngine = "@firecrawl/anydoc";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                rawBody = item.MatchedChunk?.Snippet ?? "(PDF 为扫描件或无文本层，回退至 ZotSeek 摘要)";
                usedEngine = "FallbackSnippet";
            }
        }
        else
        {
            progress?.Report($"[无PDF附件] 文献 {item.ItemKey} 使用 ZotSeek 语义切片构建 Markdown 笔记。");
            rawBody = item.MatchedChunk?.Snippet ?? "(无本地 PDF 附件，仅包含元数据与摘要)";
        }

        sw.Stop();

        if (string.IsNullOrEmpty(item.Doi) && !string.IsNullOrEmpty(rawBody))
        {
            item.Doi = ExtractDoiFromText(rawBody);
        }

        // Prepend Obsidian YAML Frontmatter
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"zotero_key: \"{item.ItemKey}\"");
        sb.AppendLine($"title: \"{EscapeYaml(item.Title)}\"");
        sb.AppendLine($"authors: \"{EscapeYaml(item.Authors ?? "Unknown")}\"");
        sb.AppendLine($"year: {item.Year?.ToString() ?? "null"}");
        if (!string.IsNullOrEmpty(item.Doi))
        {
            sb.AppendLine($"doi: \"{EscapeYaml(item.Doi)}\"");
        }
        if (!string.IsNullOrEmpty(item.PublicationTitle))
        {
            sb.AppendLine($"journal: \"{EscapeYaml(item.PublicationTitle)}\"");
        }
        sb.AppendLine($"zotero_uri: \"zotero://select/library/items/{item.ItemKey}\"");
        if (!string.IsNullOrEmpty(item.ResolvedPdfPath))
        {
            sb.AppendLine($"pdf_path: \"{EscapeYaml(item.ResolvedPdfPath)}\"");
        }
        sb.AppendLine($"extraction_engine: \"{usedEngine}\"");
        sb.AppendLine($"extraction_ms: {sw.ElapsedMilliseconds}");
        sb.AppendLine("tags:");
        sb.AppendLine("  - zotero-paper");
        sb.AppendLine("  - literature-note");
        sb.AppendLine($"extracted_at: \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {item.Title}");
        sb.AppendLine();
        sb.AppendLine($"> **Authors**: {item.Authors ?? "Unknown"} ({item.Year?.ToString() ?? "N/A"})  ");
        sb.AppendLine($"> **Zotero**: [在 Zotero 中打开条目](zotero://select/library/items/{item.ItemKey})  ");
        sb.AppendLine($"> **Extraction Engine**: `{usedEngine}` ({sw.ElapsedMilliseconds} ms)  ");
        sb.AppendLine();
        sb.AppendLine("## Extracted Full Text");
        sb.AppendLine();
        sb.AppendLine(rawBody);

        var finalMd = sb.ToString();
        await File.WriteAllTextAsync(outputPath, finalMd, Encoding.UTF8, ct);

        item.AnydocMarkdownPath = outputPath;
        item.AnydocMarkdown = finalMd;

        progress?.Report($"[Markdown 归档完成] {Path.GetFileName(outputPath)} ({finalMd.Length:N0} 字符, 耗时 {sw.ElapsedMilliseconds} ms)");
        return (outputPath, finalMd);
    }

    /// <summary>
    /// High-speed native C# PDF-to-Markdown converter using UglyToad.PdfPig's RecursiveXYCut
    /// multi-column academic page segmenter + font-height heading detection + de-hyphenation.
    /// </summary>
    public static string ExtractPdfWithPdfPigLayout(string pdfPath, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(pdfPath);

        var wordExtractor = NearestNeighbourWordExtractor.Instance;
        var pageSegmenter = RecursiveXYCut.Instance;
        var readingOrder = UnsupervisedReadingOrderDetector.Instance;

        for (int pIndex = 1; pIndex <= document.NumberOfPages; pIndex++)
        {
            ct.ThrowIfCancellationRequested();
            var page = document.GetPage(pIndex);
            var letters = page.Letters;
            if (letters == null || letters.Count == 0) continue;

            var words = wordExtractor.GetWords(letters).ToList();
            if (words.Count == 0) continue;

            // Estimate body font height on this page
            var heights = words
                .Select(w => w.Letters.Count > 0 ? w.Letters.Average(l => l.PointSize > 0 ? l.PointSize : l.GlyphRectangle.Height) : 10.0)
                .OrderBy(h => h)
                .ToList();
            double medianFontSize = heights.Count > 0 ? heights[heights.Count / 2] : 10.0;

            var blocks = pageSegmenter.GetBlocks(words);
            var orderedBlocks = readingOrder.Get(blocks).ToList();

            sb.AppendLine($"[Page {pIndex}]");
            sb.AppendLine();

            foreach (var block in orderedBlocks)
            {
                var blockTextRaw = block.Text?.Trim();
                if (string.IsNullOrWhiteSpace(blockTextRaw)) continue;

                // Skip tiny running page numbers
                if (blockTextRaw.Length <= 4 && int.TryParse(blockTextRaw, out _)) continue;

                // Compute average font size of block
                var blockWords = block.TextLines.SelectMany(tl => tl.Words).ToList();
                double blockFontSize = blockWords.Count > 0
                    ? blockWords.Average(w => w.Letters.Count > 0 ? w.Letters.Average(l => l.PointSize > 0 ? l.PointSize : l.GlyphRectangle.Height) : medianFontSize)
                    : medianFontSize;

                bool isBold = blockWords.Count > 0 && blockWords.Count(w => w.FontName != null && w.FontName.Contains("Bold", StringComparison.OrdinalIgnoreCase)) > blockWords.Count / 2;

                var cleanedPara = CleanAcademicParagraph(block);

                // Detect section headings
                if (cleanedPara.Length < 140 && !cleanedPara.EndsWith(".") &&
                    (blockFontSize >= medianFontSize * 1.18 || (isBold && IsAcademicSectionHeader(cleanedPara))))
                {
                    if (blockFontSize >= medianFontSize * 1.45)
                        sb.AppendLine($"## {cleanedPara}");
                    else
                        sb.AppendLine($"### {cleanedPara}");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine(cleanedPara);
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().Trim();
    }

    private static string CleanAcademicParagraph(TextBlock block)
    {
        var lines = block.TextLines;
        if (lines == null || lines.Count == 0) return block.Text ?? string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            var lineText = lines[i].Text.Trim();
            if (string.IsNullOrEmpty(lineText)) continue;

            // De-hyphenate across line breaks in English academic papers
            if (lineText.EndsWith("-") && i < lines.Count - 1)
            {
                var nextFirstWord = lines[i + 1].Words.FirstOrDefault()?.Text ?? "";
                if (nextFirstWord.Length > 0 && char.IsLower(nextFirstWord[0]))
                {
                    sb.Append(lineText.Substring(0, lineText.Length - 1));
                    continue;
                }
            }

            sb.Append(lineText);
            if (i < lines.Count - 1) sb.Append(' ');
        }

        return Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
    }

    private static bool IsAcademicSectionHeader(string text)
    {
        return Regex.IsMatch(text, @"^(\d+(\.\d+)*\.?\s+[A-Z]|Abstract|Introduction|Literature\s+Review|Method|Methodology|Results|Discussion|Conclusion|References|Appendix|引言|文献综述|研究方法|研究结果|讨论|结论|参考文献)", RegexOptions.IgnoreCase);
    }

    private static async Task<string> ConvertViaAnydocCliAsync(string inputFilePath, IProgress<string>? progress, CancellationToken ct)
    {
        var tempRawMd = Path.Combine(Path.GetTempPath(), $"anydoc_{Guid.NewGuid():N}.md");
        try
        {
            var isWin = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWin ? "cmd.exe" : "/bin/sh",
                Arguments = isWin
                    ? $"/c npx -y @firecrawl/anydoc \"{inputFilePath}\" -o \"{tempRawMd}\""
                    : $"-c \"npx -y @firecrawl/anydoc '{inputFilePath}' -o '{tempRawMd}'\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(ct);

            if (File.Exists(tempRawMd))
            {
                return await File.ReadAllTextAsync(tempRawMd, Encoding.UTF8, ct);
            }
        }
        catch (Exception ex)
        {
            progress?.Report($"[anydoc CLI 异常] {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempRawMd))
            {
                try { File.Delete(tempRawMd); } catch { }
            }
        }
        return string.Empty;
    }

    private static string? ExtractDoiFromText(string text)
    {
        var head = text.Length > 4000 ? text.Substring(0, 4000) : text;
        var m = Regex.Match(head, @"\b(10\.\d{4,9}/[-._;()/:A-Za-z0-9]+)");
        if (m.Success)
        {
            return m.Groups[1].Value.TrimEnd('.', ',', ';', ')', ']', '>');
        }
        return null;
    }

    private static string EscapeYaml(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (Array.IndexOf(invalid, c) < 0 && c != ' ' && c != ':' && c != '?' && c != '*')
                sb.Append(c);
            else
                sb.Append('_');
        }
        var clean = sb.ToString().Trim('_');
        return clean.Length > 45 ? clean.Substring(0, 45) : clean;
    }
}
