using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public class ExportService
{
    public static void EnsureLocalJsLibraries(string targetDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetDirectory)) return;
            Directory.CreateDirectory(targetDirectory);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var jsName in new[] { "vis-network.min.js", "marked.min.js" })
            {
                var dst = Path.Combine(targetDirectory, jsName);
                if (File.Exists(dst) && new FileInfo(dst).Length >= 1000) continue;

                // 1. Try reading from embedded WPF Resource inside Single-File .exe
                try
                {
                    var uri = new Uri($"pack://application:,,,/Assets/{jsName}", UriKind.Absolute);
                    var streamInfo = System.Windows.Application.GetResourceStream(uri);
                    if (streamInfo?.Stream != null)
                    {
                        using var s = streamInfo.Stream;
                        using var fs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.Read);
                        s.CopyTo(fs);
                        continue;
                    }
                }
                catch { }

                // 2. Fallback to file on disk
                var src = Path.Combine(baseDir, "Assets", jsName);
                if (File.Exists(src))
                {
                    File.Copy(src, dst, overwrite: true);
                }
            }
        }
        catch { }
    }

    public static string GenerateObsidianMarkdown(
        string query,
        List<ZotSeekResultItem> papers,
        KnowledgeGraphData graph,
        string literatureReviewMarkdown)
    {
        var sb = new StringBuilder();

        // Obsidian YAML Frontmatter
        sb.AppendLine("---");
        sb.AppendLine("tags:");
        sb.AppendLine("  - literature-review");
        sb.AppendLine("  - zotero-workflow");
        sb.AppendLine("  - knowledge-graph");
        sb.AppendLine($"query: \"{EscapeYaml(query)}\"");
        sb.AppendLine($"created: \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"");
        sb.AppendLine($"paper_count: {papers.Count}");
        sb.AppendLine($"concept_count: {graph.Nodes?.Count ?? 0}");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine($"# 文献综述与高阶学科知识图谱：{query}");
        sb.AppendLine();
        sb.AppendLine($"> **检索主题**: `{query}`  ");
        sb.AppendLine($"> **生成时间**: `{DateTime.Now:yyyy-MM-dd HH:mm}`  ");
        sb.AppendLine($"> **纳入核心文献**: `{papers.Count}` 篇 | **核心概念节点**: `{graph.Nodes?.Count ?? 0}` 个 | **学术演进关系**: `{graph.Edges?.Count ?? 0}` 条  ");
        sb.AppendLine();

        // Section 1: Literature Review
        sb.AppendLine("## 一、深度文献综述 (Comprehensive Literature Review)");
        sb.AppendLine();
        sb.AppendLine(literatureReviewMarkdown.Trim());
        sb.AppendLine();

        // Section 2: Research Clusters
        if (graph.ResearchClusters != null && graph.ResearchClusters.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine("## 二、研究流派与主题聚类 (Research Clusters & Schools of Thought)");
            sb.AppendLine();
            foreach (var cluster in graph.ResearchClusters)
            {
                sb.AppendLine($"### 📌 流派：{cluster.ClusterName}");
                sb.AppendLine($"- **核心学术主张**: {cluster.ThemeSummary}");
                if (cluster.Papers != null && cluster.Papers.Count > 0)
                {
                    sb.AppendLine($"- **代表性文献**: {string.Join("；", cluster.Papers)}");
                }
                if (!string.IsNullOrWhiteSpace(cluster.ConsensusAndDivergence))
                {
                    sb.AppendLine($"- **内部共识与分歧点**: {cluster.ConsensusAndDivergence}");
                }
                sb.AppendLine();
            }
        }

        // Section 3: Discipline Evolution
        sb.AppendLine("---");
        sb.AppendLine("## 三、学科演进历时脉络与范式转移 (Discipline Evolution & Paradigm Shifts)");
        sb.AppendLine();
        if (graph.Evolution != null)
        {
            foreach (var evo in graph.Evolution)
            {
                sb.AppendLine($"### ⏳ {evo.Stage} —— {evo.Paradigm}");
                if (!string.IsNullOrWhiteSpace(evo.DrivingQuestion))
                    sb.AppendLine($"- **核心学术追问**: {evo.DrivingQuestion}");
                if (!string.IsNullOrWhiteSpace(evo.MethodShift))
                    sb.AppendLine($"- **研究方法与工具变革**: {evo.MethodShift}");
                if (evo.RepresentativePapers != null && evo.RepresentativePapers.Count > 0)
                    sb.AppendLine($"- **代表性文献**: {string.Join(", ", evo.RepresentativePapers)}");
                if (evo.KeyDevelopments != null && evo.KeyDevelopments.Count > 0)
                {
                    sb.AppendLine("- **关键理论与实证突破**:");
                    foreach (var dev in evo.KeyDevelopments)
                    {
                        sb.AppendLine($"  - {dev}");
                    }
                }
                if (!string.IsNullOrWhiteSpace(evo.UnresolvedGap))
                    sb.AppendLine($"- **遗留瓶颈（下一阶段推力）**: {evo.UnresolvedGap}");
                sb.AppendLine();
            }
        }

        // Section 4: Knowledge Graph Entities
        sb.AppendLine("---");
        sb.AppendLine("## 四、核心理论与构念图谱矩阵 (Knowledge Graph Entities)");
        sb.AppendLine();
        sb.AppendLine("| 概念实体 (Obsidian 双链) | 类别 | 年份 | 来源文献 | 权重 | 学术定义与机制 | 实证证据 / 局限性 |");
        sb.AppendLine("| :--- | :--- | :---: | :--- | :---: | :--- | :--- |");
        if (graph.Nodes != null)
        {
            foreach (var node in graph.Nodes)
            {
                var wikiLink = $"[[{node.Label}]]";
                var evLim = $"{(string.IsNullOrEmpty(node.Evidence) ? "" : "证据: " + node.Evidence)} {(string.IsNullOrEmpty(node.Limitation) ? "" : "| 局限: " + node.Limitation)}".Trim();
                sb.AppendLine($"| **{wikiLink}** | `{node.Category}` | {node.Year ?? "—"} | {node.Paper ?? "—"} | ⭐ {node.Importance}/10 | {EscapeTable(node.Summary)} | {EscapeTable(evLim)} |");
            }
        }
        sb.AppendLine();

        // Section 5: Entity Relationships
        sb.AppendLine("### 概念语义关系网络 (Relational Topology)");
        sb.AppendLine();
        sb.AppendLine("| 源概念 | 演进/逻辑关系 | 强度 | 目标概念 | 作用机制详述 |");
        sb.AppendLine("| :--- | :---: | :---: | :--- | :--- |");
        if (graph.Edges != null)
        {
            foreach (var edge in graph.Edges)
            {
                sb.AppendLine($"| [[{edge.Source}]] | `{edge.Relation}` | {edge.Weight}/5 | [[{edge.Target}]] | {EscapeTable(edge.Detail)} |");
            }
        }
        sb.AppendLine();

        // Section 6: Connected Papers Network
        sb.AppendLine("---");
        sb.AppendLine("## 五、文献互引与对话网络 (Connected Papers Lineage)");
        sb.AppendLine();
        if (graph.PaperConnections != null && graph.PaperConnections.Count > 0)
        {
            foreach (var pc in graph.PaperConnections)
            {
                sb.AppendLine($"- **《{pc.SourceTitle}》** ➔ **《{pc.TargetTitle}》**");
                sb.AppendLine($"  - **对话性质**: `{pc.Type}` | **共享构念**: {pc.SharedConcepts ?? "—"}");
                sb.AppendLine($"  - **学术关联阐述**: {pc.Explanation}");
            }
            sb.AppendLine();
        }

        // Section 6.5: Bibliometrics & Text Mining
        if (graph.Bibliometrics != null)
        {
            var bib = graph.Bibliometrics;
            sb.AppendLine("---");
            sb.AppendLine("## 六、语料库主题建模、共现词网络与 OpenAlex 文献计量分析");
            sb.AppendLine();
            sb.AppendLine($"> **分析语料总词数**: `{bib.TotalCorpusWords:N0}` 词 | **OpenAlex 累计被引**: `{bib.TotalCitations}` 次 | **平均被引**: `{bib.MeanCitations}` 次 | **平均 FWCI**: `{bib.MeanFwci}` | **发表跨度**: `{bib.YearRange}`");
            sb.AppendLine();

            if (bib.TopicModels.Count > 0)
            {
                sb.AppendLine("### 6.1 文本主题建模 (Topic Modeling & Lexical Clusters)");
                sb.AppendLine();
                sb.AppendLine("| 主题簇 (Topic) | 语料权重占比 | 核心高频特征词 (Top Terms) | 代表性高相关文献 |");
                sb.AppendLine("| :--- | :---: | :--- | :--- |");
                foreach (var tm in bib.TopicModels)
                {
                    sb.AppendLine($"| **{EscapeTable(tm.TopicLabel)}** | {tm.WeightPercent}% | `{string.Join("`, `", tm.TopTerms)}` | {EscapeTable(string.Join("；", tm.DominantPapers))} |");
                }
                sb.AppendLine();
            }

            if (bib.PaperMetrics.Count > 0)
            {
                sb.AppendLine("### 6.2 OpenAlex 引文影响力与文献 TF-IDF 特征词矩阵");
                sb.AppendLine();
                sb.AppendLine("| 文献标题 | 作者 (年份) | 期刊/来源 | DOI | 全球被引 (OpenAlex) | FWCI | 文本 TF-IDF 特征词 |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :---: | :---: | :--- |");
                foreach (var pm in bib.PaperMetrics)
                {
                    var kws = pm.DistinctiveKeywords.Count > 0 ? string.Join(", ", pm.DistinctiveKeywords) : "—";
                    sb.AppendLine($"| {EscapeTable(pm.Title)} | {EscapeTable(pm.Authors)} ({pm.Year?.ToString() ?? "—"}) | {EscapeTable(pm.Venue)} | `{pm.Doi}` | **{pm.CitedByCount}** | {pm.Fwci} | {EscapeTable(kws)} |");
                }
                sb.AppendLine();
            }

            if (bib.BibliographicCouplings.Count > 0)
            {
                sb.AppendLine("### 6.3 文献耦合度与直接互引检测 (Bibliographic Coupling via OpenAlex)");
                sb.AppendLine();
                foreach (var bc in bib.BibliographicCouplings.Take(15))
                {
                    var citeTag = bc.IsDirectCitation ? " **[存在直接引证]**" : "";
                    sb.AppendLine($"- **《{bc.PaperA}》** ⟷ **《{bc.PaperB}》**：共同参考文献 **{bc.SharedReferences}** 篇 (耦合强度 Cosine={bc.CouplingStrength}){citeTag}");
                }
                sb.AppendLine();
            }

            if (bib.CoCitedSeminalWorks != null && bib.CoCitedSeminalWorks.Count > 0)
            {
                sb.AppendLine("### 6.4 领域高频共引奠基文献与缺失文献发现 (Co-Cited Seminal & Missing Papers Snowballing)");
                sb.AppendLine();
                sb.AppendLine("| 文献标题 | 作者 (年份) | 期刊/来源 | DOI | 被当前文献集共引 | OpenAlex总被引 | Zotero在库状态 |");
                sb.AppendLine("| :--- | :--- | :--- | :--- | :---: | :---: | :---: |");
                foreach (var sw in bib.CoCitedSeminalWorks)
                {
                    var status = sw.InLocalZotero ? "✅ 已在库中" : "🔍 **库中缺失(建议补全)**";
                    var doiLink = sw.Doi != "—" ? $"[{sw.Doi}](https://doi.org/{sw.Doi})" : "—";
                    sb.AppendLine($"| {EscapeTable(sw.Title)} | {EscapeTable(sw.Authors)} ({sw.Year?.ToString() ?? "—"}) | {EscapeTable(sw.Venue)} | {doiLink} | **{sw.CoCitedCount} 篇共引** | {sw.GlobalCitations} | {status} |");
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(bib.LlmInterpretationMarkdown))
            {
                sb.AppendLine("### 6.5 文献计量与语料文本挖掘深度解读 (LLM Scientometric & Text-Mining Synthesis)");
                sb.AppendLine();
                sb.AppendLine(bib.LlmInterpretationMarkdown.Trim());
                sb.AppendLine();
            }
        }

        // Section 7: Paper Vault Links
        sb.AppendLine("---");
        sb.AppendLine("## 七、纳入研读的文献清单与本地 Markdown 知识库链接");
        sb.AppendLine();
        for (int i = 0; i < papers.Count; i++)
        {
            var p = papers[i];
            var mdFileName = !string.IsNullOrEmpty(p.AnydocMarkdownPath)
                ? Path.GetFileNameWithoutExtension(p.AnydocMarkdownPath)
                : p.ItemKey;

            sb.AppendLine($"### {i + 1}. [[{mdFileName}|{p.Title}]]");
            sb.AppendLine($"- **作者**: {p.Authors ?? "未知"} ({p.Year?.ToString() ?? "未知年份"})");
            sb.AppendLine($"- **Zotero Key**: `{p.ItemKey}` | [在 Zotero 中定位]({p.Links?.Select ?? $"zotero://select/library/items/{p.ItemKey}"})");
            if (!string.IsNullOrEmpty(p.AnydocMarkdownPath))
            {
                sb.AppendLine($"- **本地 Markdown 笔记**: `[[{mdFileName}]]` (`{p.AnydocMarkdownPath}`)");
            }
            if (!string.IsNullOrEmpty(p.ResolvedPdfPath))
            {
                sb.AppendLine($"- **本地链接 PDF**: `{p.ResolvedPdfPath}`");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string GenerateWysiwygMarkdownHtml(string markdownText, string selectedTheme = "warm-paper", bool editable = false)
    {
        var mdJson = JsonSerializer.Serialize(markdownText ?? "");
        var contentEditableAttr = editable ? "true" : "false";

        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <script src="marked.min.js"></script>
  <script>if(typeof marked === 'undefined') document.write('<scr'+'ipt src="https://cdn.jsdelivr.net/npm/marked@12.0.1/marked.min.js"></scr'+'ipt>');</script>
  <style>
    * { box-sizing: border-box; }
    body {
      margin: 0;
      padding: 28px 40px;
      transition: background 0.2s, color 0.2s;
    }
    .page-sheet {
      max-width: 920px;
      margin: 0 auto;
      padding: 44px 56px;
      border-radius: 10px;
      outline: none;
    }
    .wikilink {
      display: inline-block;
      padding: 1px 7px;
      border-radius: 5px;
      font-weight: 600;
      font-size: 0.94em;
      text-decoration: none;
      margin: 0 2px;
    }
    .yaml-banner {
      font-family: Consolas, monospace;
      font-size: 12px;
      padding: 10px 14px;
      border-radius: 6px;
      margin-bottom: 24px;
      opacity: 0.85;
    }

    /* 1. Warm Paper Ink-Blue (Default) */
    body.theme-warm-paper {
      background: #F3EFE6;
      color: #1E242B;
      font-family: "Georgia", "Noto Serif SC", "Songti SC", "Microsoft YaHei", serif;
      line-height: 1.85;
      font-size: 16px;
    }
    body.theme-warm-paper .page-sheet {
      background: #FBF9F5;
      border: 1px solid #E2D9C8;
      box-shadow: 0 6px 24px rgba(45, 35, 20, 0.06);
    }
    body.theme-warm-paper h1, body.theme-warm-paper h2, body.theme-warm-paper h3 {
      color: #1E3A8A;
      font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
      border-bottom: 1.5px solid #D6CBB5;
      padding-bottom: 6px;
      margin-top: 28px;
    }
    body.theme-warm-paper blockquote {
      background: #F4EFE4;
      border-left: 4px solid #1E3A8A;
      padding: 12px 18px;
      margin: 16px 0;
      color: #374151;
    }
    body.theme-warm-paper table { width: 100%; border-collapse: collapse; margin: 20px 0; font-size: 14.5px; }
    body.theme-warm-paper th, body.theme-warm-paper td { border: 1px solid #D6CBB5; padding: 9px 12px; }
    body.theme-warm-paper th { background: #EFE9DC; color: #1E3A8A; }
    body.theme-warm-paper .wikilink { background: #E0E7FF; color: #1E40AF; border: 1px solid #C7D2FE; }

    /* 2. Obsidian Knowledge Vault */
    body.theme-obsidian {
      background: #F4F4F8;
      color: #1F2328;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif;
      line-height: 1.78;
      font-size: 15.5px;
    }
    body.theme-obsidian .page-sheet {
      background: #FFFFFF;
      border: 1px solid #DCDCE5;
      box-shadow: 0 4px 16px rgba(109, 40, 217, 0.05);
    }
    body.theme-obsidian h1, body.theme-obsidian h2, body.theme-obsidian h3 {
      color: #5B21B6;
      border-bottom: 1px solid #E5E7EB;
      padding-bottom: 6px;
      margin-top: 26px;
    }
    body.theme-obsidian blockquote {
      background: #F5F3FF;
      border-left: 4px solid #7C3AED;
      padding: 12px 16px;
      margin: 16px 0;
      color: #4C1D95;
    }
    body.theme-obsidian table { width: 100%; border-collapse: collapse; margin: 18px 0; font-size: 14px; }
    body.theme-obsidian th, body.theme-obsidian td { border: 1px solid #E5E7EB; padding: 8px 12px; }
    body.theme-obsidian th { background: #F5F3FF; color: #5B21B6; }
    body.theme-obsidian .wikilink { background: #F3E8FF; color: #6D28D9; border: 1px solid #DDD6FE; }

    /* 3. Academic Journal (APA / Nature Print Style) */
    body.theme-academic-apa {
      background: #E5E7EB;
      color: #000000;
      font-family: "Times New Roman", "SimSun", "Songti SC", serif;
      line-height: 1.9;
      font-size: 16px;
    }
    body.theme-academic-apa .page-sheet {
      background: #FFFFFF;
      border: 1px solid #9CA3AF;
      box-shadow: 0 8px 25px rgba(0, 0, 0, 0.10);
      padding: 56px 68px;
    }
    body.theme-academic-apa h1, body.theme-academic-apa h2, body.theme-academic-apa h3 {
      color: #000000;
      font-weight: bold;
      border-bottom: 1px solid #111827;
      padding-bottom: 4px;
      margin-top: 28px;
    }
    body.theme-academic-apa blockquote {
      background: #F9FAFB;
      border-left: 3px solid #111827;
      padding: 10px 18px;
      font-style: italic;
    }
    body.theme-academic-apa table {
      width: 100%;
      border-collapse: collapse;
      border-top: 2px solid #000000;
      border-bottom: 2px solid #000000;
      margin: 22px 0;
      font-size: 14px;
    }
    body.theme-academic-apa th { border-bottom: 1px solid #000000; padding: 8px 10px; text-align: left; background: #F9FAFB; }
    body.theme-academic-apa td { padding: 7px 10px; border-bottom: 1px solid #E5E7EB; }
    body.theme-academic-apa .wikilink { background: #F3F4F6; color: #111827; border: 1px solid #9CA3AF; }

    /* 4. GitHub Clean */
    body.theme-github-clean {
      background: #F6F8FA;
      color: #1F2328;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif;
      line-height: 1.7;
      font-size: 15px;
    }
    body.theme-github-clean .page-sheet {
      background: #FFFFFF;
      border: 1px solid #D0D7DE;
    }
    body.theme-github-clean h1, body.theme-github-clean h2, body.theme-github-clean h3 {
      color: #1F2328;
      border-bottom: 1px solid #D8DEE4;
      padding-bottom: 6px;
    }
    body.theme-github-clean blockquote {
      border-left: 4px solid #D0D7DE;
      color: #59636E;
      padding: 4px 16px;
      margin: 14px 0;
    }
    body.theme-github-clean table { width: 100%; border-collapse: collapse; margin: 16px 0; }
    body.theme-github-clean th, body.theme-github-clean td { border: 1px solid #D0D7DE; padding: 8px 12px; }
    body.theme-github-clean th { background: #F6F8FA; }
    body.theme-github-clean .wikilink { background: #DDF4FF; color: #0969DA; border: 1px solid #54AEFF66; }

    /* 5. Eye-Care Sage Green */
    body.theme-eye-care-green {
      background: #DCEADDF0;
      color: #1B2E20;
      font-family: "Segoe UI", "Microsoft YaHei", sans-serif;
      line-height: 1.85;
      font-size: 15.5px;
    }
    body.theme-eye-care-green .page-sheet {
      background: #EDF5EE;
      border: 1px solid #B7D3BC;
      box-shadow: 0 4px 16px rgba(20, 83, 45, 0.06);
    }
    body.theme-eye-care-green h1, body.theme-eye-care-green h2, body.theme-eye-care-green h3 {
      color: #14532D;
      border-bottom: 1.5px solid #B7D3BC;
      padding-bottom: 6px;
    }
    body.theme-eye-care-green blockquote {
      background: #E1EFE3;
      border-left: 4px solid #15803D;
      padding: 10px 16px;
      color: #166534;
    }
    body.theme-eye-care-green table { width: 100%; border-collapse: collapse; margin: 18px 0; }
    body.theme-eye-care-green th, body.theme-eye-care-green td { border: 1px solid #B7D3BC; padding: 8px 12px; }
    body.theme-eye-care-green th { background: #DDECE0; color: #14532D; }
    body.theme-eye-care-green .wikilink { background: #DCFCE7; color: #166534; border: 1px solid #86EFAC; }
  </style>
</head>
<body class="theme-{{selectedTheme}}">
  <div class="page-sheet" id="content" contenteditable="{{contentEditableAttr}}"></div>
  <script>
    const rawMd = {{mdJson}};
    function simpleMdFallback(t) {
      return '<div style="white-space:pre-wrap;">' + t.replace(/</g,'&lt;') + '</div>';
    }
    function renderMarkdown(md) {
      if (!md || !md.trim()) {
        document.getElementById('content').innerHTML = '<p style="color:#64748B;text-align:center;padding:40px 0;">尚未生成文献综述内容。请先在【1. 语义检索与文献挑选】中挑选论文并运行工作流。</p>';
        return;
      }
      let bodyMd = md;
      let yamlHtml = '';
      if (md.startsWith('---\n') || md.startsWith('---\r\n')) {
        const endIdx = md.indexOf('\n---', 3);
        if (endIdx > 0) {
          const yamlPart = md.substring(3, endIdx).trim();
          bodyMd = md.substring(endIdx + 4).trim();
          yamlHtml = `<div class="yaml-banner"><strong>Obsidian YAML Frontmatter:</strong><br>${yamlPart.replace(/\n/g, ' &nbsp;|&nbsp; ')}</div>`;
        }
      }
      bodyMd = bodyMd.replace(/\[PDF:([A-Za-z0-9]+),\s*p\.?\s*(\d+)\]/gi, '<a class="wikilink" href="zotero://open-pdf/library/items/$1?page=$2" title="在 Zotero 中直接打开 PDF 第 $2 页">📄 p.$2 ↗</a>');
      bodyMd = bodyMd.replace(/\[\[([^\]|]+)\|([^\]]+)\]\]/g, '<span class="wikilink" title="Obsidian Note: $1">🔗 $2</span>');
      bodyMd = bodyMd.replace(/\[\[([^\]]+)\]\]/g, '<span class="wikilink">🔗 $1</span>');
      const parsed = (typeof marked !== 'undefined' && marked.parse) ? marked.parse(bodyMd) : simpleMdFallback(bodyMd);
      document.getElementById('content').innerHTML = yamlHtml + parsed;
    }
    renderMarkdown(rawMd);
  </script>
</body>
</html>
""";
    }

    public static string GenerateInteractiveHtml(
        string query,
        List<ZotSeekResultItem> papers,
        KnowledgeGraphData graph,
        string literatureReviewMarkdown,
        string defaultMdTheme = "warm-paper")
    {
        // Auto-synthesize ResearchClusters if empty so the Research Clusters tab is ALWAYS populated
        if ((graph.ResearchClusters == null || graph.ResearchClusters.Count == 0) && graph.Bibliometrics?.TopicModels != null && graph.Bibliometrics.TopicModels.Count > 0)
        {
            graph.ResearchClusters = graph.Bibliometrics.TopicModels.Select(tm => new KgResearchCluster
            {
                ClusterName = tm.TopicLabel,
                ThemeSummary = $"围绕核心特征术语 [{string.Join(", ", tm.TopTerms)}] 形成的学术研究主题群（占语料库总权重 {tm.WeightPercent}%）。",
                Papers = tm.DominantPapers,
                ConsensusAndDivergence = "该主题群文献在核心术语与研究对象上具有高度共现耦合性，从不同实证情境或方法路径切入同一议题域。"
            }).ToList();
        }

        var graphJson = JsonSerializer.Serialize(graph);
        var papersJson = JsonSerializer.Serialize(papers);

        var html = $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>高阶知识图谱与文献综述交互报告: {{query}}</title>
  <script src="vis-network.min.js"></script>
  <script>if(typeof vis === 'undefined') document.write('<scr'+'ipt src="https://cdn.jsdelivr.net/npm/vis-network@9.1.9/standalone/umd/vis-network.min.js"></scr'+'ipt>');</script>
  <script src="marked.min.js"></script>
  <script>if(typeof marked === 'undefined') document.write('<scr'+'ipt src="https://cdn.jsdelivr.net/npm/marked@12.0.1/marked.min.js"></scr'+'ipt>');</script>
  <style>
    :root {
      --bg: #F8FAFC;
      --card-bg: #FFFFFF;
      --border: #CBD5E1;
      --text: #0F172A;
      --text-muted: #475569;
      --accent: #0284C7;
      --accent-glow: #E0F2FE;
      --success: #059669;
      --warning: #D97706;
      --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background: var(--bg);
      color: var(--text);
      font-family: var(--font-sans);
      display: flex;
      flex-direction: column;
      height: 100vh;
      width: 100vw;
      overflow: hidden;
    }
    header {
      background: var(--card-bg);
      border-bottom: 1px solid var(--border);
      padding: 10px 20px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-shrink: 0;
      flex-wrap: wrap;
      gap: 10px;
    }
    .header-title {
      display: flex;
      align-items: center;
      gap: 10px;
    }
    .badge {
      background: var(--accent-glow);
      color: #0369A1;
      border: 1px solid #7DD3FC;
      padding: 2px 9px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 700;
    }
    .nav-tabs {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }
    .tab-btn {
      background: #F1F5F9;
      border: 1px solid var(--border);
      color: var(--text-muted);
      padding: 7px 14px;
      border-radius: 6px;
      cursor: pointer;
      font-size: 13px;
      font-weight: 600;
      transition: all 0.15s;
    }
    .tab-btn:hover { color: var(--text); background: #E2E8F0; }
    .tab-btn.active {
      background: var(--accent);
      color: #FFFFFF;
      border-color: var(--accent);
    }
    main {
      flex: 1;
      position: relative;
      width: 100%;
      height: calc(100vh - 56px);
      overflow: hidden;
    }
    .tab-pane {
      display: none;
      width: 100%;
      height: calc(100vh - 56px);
    }
    .tab-pane.active {
      display: flex;
    }

    /* Graph Containers with explicit pixel/vh dimensions so vis.Network NEVER collapses to 0px */
    .graph-main-col {
      flex: 1;
      height: calc(100vh - 56px);
      position: relative;
      background: radial-gradient(circle at center, #FFFFFF 0%, #F1F5F9 100%);
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }
    .graph-toolbar {
      background: rgba(255,255,255,0.95);
      border-bottom: 1px solid var(--border);
      padding: 8px 16px;
      height: 44px;
      display: flex;
      align-items: center;
      gap: 14px;
      flex-wrap: wrap;
      font-size: 12.5px;
      flex-shrink: 0;
      z-index: 10;
    }
    .graph-toolbar select, .graph-toolbar input[type="text"] {
      padding: 4px 8px;
      border: 1px solid var(--border);
      border-radius: 5px;
      font-size: 12.5px;
      color: var(--text);
      background: #FFFFFF;
    }
    .graph-canvas-box {
      width: 100%;
      height: calc(100vh - 100px);
      min-height: 480px;
      position: relative;
    }
    .graph-sidebar {
      width: 420px;
      height: calc(100vh - 56px);
      background: var(--card-bg);
      border-left: 1px solid var(--border);
      padding: 18px;
      overflow-y: auto;
      flex-shrink: 0;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }
    .panel-card {
      background: #F8FAFC;
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 15px;
    }
    .panel-card h3 {
      font-size: 14.5px;
      margin-bottom: 10px;
      color: #0369A1;
      border-bottom: 1px solid #E2E8F0;
      padding-bottom: 6px;
    }
    .detail-box {
      background: #FFFFFF;
      border: 1px solid #CBD5E1;
      border-radius: 6px;
      padding: 10px 12px;
      margin-top: 8px;
      font-size: 13px;
      line-height: 1.65;
      color: #0F172A;
    }

    /* Evolution & Clusters */
    .scroll-page {
      flex: 1;
      height: calc(100vh - 56px);
      padding: 28px 44px;
      overflow-y: auto;
      max-width: 1120px;
      margin: 0 auto;
    }
    .timeline-item {
      position: relative;
      padding-left: 30px;
      padding-bottom: 28px;
      border-left: 3px solid var(--accent);
    }
    .timeline-item::before {
      content: '';
      position: absolute;
      left: -9px;
      top: 0;
      width: 15px;
      height: 15px;
      border-radius: 50%;
      background: var(--accent);
      border: 3px solid #FFFFFF;
      box-shadow: 0 0 0 2px var(--accent);
    }
    .evo-card {
      background: #FFFFFF;
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 18px 22px;
      box-shadow: 0 2px 6px rgba(15,23,42,0.04);
    }
    .evo-meta-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      margin: 12px 0;
    }
    .evo-meta-item {
      background: #F8FAFC;
      border: 1px solid #E2E8F0;
      padding: 10px 12px;
      border-radius: 6px;
      font-size: 13px;
      line-height: 1.6;
    }
    .cluster-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(440px, 1fr));
      gap: 20px;
    }
    .cluster-card {
      background: #FFFFFF;
      border: 1px solid var(--border);
      border-top: 4px solid var(--accent);
      border-radius: 8px;
      padding: 18px 20px;
      box-shadow: 0 2px 8px rgba(15,23,42,0.04);
    }

    /* Papers Grid */
    .papers-container {
      flex: 1;
      height: calc(100vh - 56px);
      padding: 24px 36px;
      overflow-y: auto;
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(420px, 1fr));
      gap: 18px;
      align-content: start;
    }
    .paper-card {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }
    .paper-title { font-size: 15.5px; font-weight: 700; color: #0F172A; }
    .paper-meta { font-size: 12.5px; color: var(--text-muted); font-weight: 600; }
    .paper-snippet {
      font-size: 13px;
      color: #334155;
      background: #F8FAFC;
      border: 1px solid #E2E8F0;
      padding: 10px 12px;
      border-radius: 6px;
      max-height: 130px;
      overflow-y: auto;
    }
  </style>
</head>
<body>
  <header>
    <div class="header-title">
      <h2 style="font-size: 17px; color: #0F172A;">⚡ Zotero 学术知识图谱 & 学科演进分析</h2>
      <span class="badge">{{papers.Count}} 篇核心文献</span>
      <span class="badge">{{graph.Nodes?.Count ?? 0}} 个概念实体</span>
      <span style="font-size: 13px; color: var(--text-muted); font-weight: 600;">主题：{{query}}</span>
    </div>
    <div class="nav-tabs">
      <button class="tab-btn active" onclick="switchTab('graph', this)">🌐 概念知识图谱</button>
      <button class="tab-btn" onclick="switchTab('coword', this)">🔤 共现词与主题建模</button>
      <button class="tab-btn" onclick="switchTab('paper-graph', this)">🕸️ 文献互引网络</button>
      <button class="tab-btn" onclick="switchTab('biblio', this)">📈 OpenAlex 文献计量</button>
      <button class="tab-btn" onclick="switchTab('evolution', this)">⏳ 学科演进矩阵</button>
      <button class="tab-btn" onclick="switchTab('clusters', this)">📊 研究流派聚类</button>
      <button class="tab-btn" onclick="switchTab('papers', this)">📚 纳入文献库</button>
    </div>
  </header>

  <main>
    <!-- Tab 1: Concept Knowledge Graph -->
    <div id="pane-graph" class="tab-pane active">
      <div class="graph-main-col">
        <div class="graph-toolbar">
          <label><strong>布局算法:</strong>
            <select id="layout-select" onchange="changeGraphLayout(this.value)">
              <option value="force">力导向网络 (ForceAtlas2)</option>
              <option value="hier-lr">层次演进树 (从左至右 LR)</option>
              <option value="hier-ud">层次演进树 (从上至下 UD)</option>
            </select>
          </label>
          <label><strong>按类别筛选:</strong>
            <select id="category-filter" onchange="filterGraphNodes()">
              <option value="ALL">全部学术类别 (All Categories)</option>
              <option value="TheoreticalFramework">理论框架 (TheoreticalFramework)</option>
              <option value="Methodology">研究方法 (Methodology)</option>
              <option value="ConstructOrVariable">核心构念/变量 (ConstructOrVariable)</option>
              <option value="EmpiricalFinding">实证发现 (EmpiricalFinding)</option>
              <option value="TechnologyOrTool">技术/语料/工具 (TechnologyOrTool)</option>
              <option value="ResearchGap">研究空白/争议 (ResearchGap)</option>
            </select>
          </label>
          <label><strong>搜索节点:</strong>
            <input type="text" id="node-search" placeholder="输入概念或学者..." oninput="filterGraphNodes()" />
          </label>
          <button class="tab-btn" style="padding:4px 10px;font-size:12px;" onclick="if(network) network.fit()">🎯 居中适配</button>
        </div>
        <div id="network-canvas" class="graph-canvas-box"></div>
      </div>
      <div class="graph-sidebar">
        <div class="panel-card">
          <h3>📌 深度概念 / 关系检视器 (Inspector)</h3>
          <div id="inspector-content" style="font-size: 13.5px; color: var(--text-muted); line-height: 1.65;">
            点击左侧画布中的<strong>任意概念节点</strong>或<strong>关系连线</strong>，即可检视其学术定义、来源文献、原文实证证据、理论局限性以及上下游演进关系。
          </div>
        </div>
        <div class="panel-card">
          <h3>🎨 节点图例 (Legend)</h3>
          <div style="display:grid; grid-template-columns: 1fr 1fr; gap:6px; font-size:12.5px;">
            <div><span style="color:#0284C7;font-weight:bold;">●</span> 理论框架 (Theory)</div>
            <div><span style="color:#059669;font-weight:bold;">●</span> 研究方法 (Method)</div>
            <div><span style="color:#D97706;font-weight:bold;">●</span> 构念/变量 (Construct)</div>
            <div><span style="color:#7C3AED;font-weight:bold;">●</span> 实证发现 (Finding)</div>
            <div><span style="color:#0D9488;font-weight:bold;">●</span> 技术/语料 (Tool/Corpus)</div>
            <div><span style="color:#E11D48;font-weight:bold;">●</span> 研究空白 (ResearchGap)</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Tab 1.5: Co-occurrence Word Network & Topic Modeling -->
    <div id="pane-coword" class="tab-pane">
      <div class="graph-main-col">
        <div class="graph-toolbar">
          <span><strong>VOSviewer 风格语料共现词网络 (Co-occurrence Network)：</strong>节点大小=TF-IDF权重，颜色=主题簇(Topic Cluster)，连线粗细=段落共现频次与PMI互信息</span>
          <button class="tab-btn" style="padding:4px 10px;font-size:12px;margin-left:auto;" onclick="if(coNetwork) coNetwork.fit()">🎯 居中适配</button>
        </div>
        <div id="co-network-canvas" class="graph-canvas-box"></div>
      </div>
      <div class="graph-sidebar" style="width:450px;">
        <div class="panel-card">
          <h3>🔤 词汇共现检视面板</h3>
          <div id="co-inspector-content" style="font-size:13px; color:var(--text-muted); line-height:1.6;">
            点击左侧任意高频学术术语节点，查看其词频(TF)、文档频次(DF)、TF-IDF得分及最强搭配共现词。
          </div>
        </div>
        <div class="panel-card">
          <h3>🧩 语料主题建模簇 (Topic Modeling)</h3>
          <div id="topics-sidebar-box" style="display:flex; flex-direction:column; gap:10px; font-size:13px;"></div>
        </div>
      </div>
    </div>

    <!-- Tab 2: Connected Papers Lineage Graph -->
    <div id="pane-paper-graph" class="tab-pane">
      <div class="graph-main-col">
        <div class="graph-toolbar">
          <span><strong>Connected Papers 文献传承与对话网络：</strong>展示所选论文之间的理论继承、方法复现与观点交锋</span>
          <button class="tab-btn" style="padding:4px 10px;font-size:12px;margin-left:auto;" onclick="if(paperNetwork) paperNetwork.fit()">🎯 居中适配</button>
        </div>
        <div id="paper-network-canvas" class="graph-canvas-box"></div>
      </div>
      <div class="graph-sidebar">
        <div class="panel-card">
          <h3>📖 文献对话详情 (Paper Lineage)</h3>
          <div id="paper-inspector-content" style="font-size: 13.5px; color: var(--text-muted); line-height: 1.65;">
            点击画布中的文献节点或文献间的连线，查看两篇论文之间的共享核心构念与具体学术对话关系。
          </div>
        </div>
      </div>
    </div>

    <!-- Tab 2.5: OpenAlex Bibliometrics Dashboard -->
    <div id="pane-biblio" class="tab-pane">
      <div class="scroll-page" style="max-width:1180px;">
        <h2 style="margin-bottom:16px; color:#0F172A;">📈 OpenAlex 文献计量、引文影响力与文献耦合分析 (Bibliometrics)</h2>
        <div id="biblio-kpi-box" style="display:grid; grid-template-columns:repeat(5, 1fr); gap:14px; margin-bottom:22px;"></div>
        <div class="evo-card" style="margin-bottom:22px;">
          <h3 style="color:#0369A1; margin-bottom:12px; font-size:16px;">🏆 纳入文献 OpenAlex 全球被引、FWCI 与 TF-IDF 特征词排行</h3>
          <div style="overflow-x:auto;">
            <table id="biblio-table" style="width:100%; border-collapse:collapse; font-size:13px;"></table>
          </div>
        </div>
        <div class="evo-card" style="margin-bottom:22px;">
          <h3 style="color:#0369A1; margin-bottom:12px; font-size:16px;">🔗 文献耦合度与共同引文网络 (Bibliographic Coupling via OpenAlex Referenced Works)</h3>
          <div id="biblio-coupling-box" style="font-size:13.5px; line-height:1.7;"></div>
        </div>
        <div class="evo-card" style="margin-bottom:22px;">
          <h3 style="color:#0369A1; margin-bottom:12px; font-size:16px;">🎯 领域高频共引奠基文献与待补全缺失文献发现 (Co-Cited Seminal &amp; Missing Papers Snowballing)</h3>
          <div style="overflow-x:auto;">
            <table id="biblio-seminal-table" style="width:100%; border-collapse:collapse; font-size:13px;"></table>
          </div>
        </div>
        <div class="evo-card">
          <h3 style="color:#0369A1; margin-bottom:12px; font-size:16px;">🧠 LLM 文献计量、共现词与主题建模深度解读报告 (由计量 Prompt 驱动)</h3>
          <div id="biblio-llm-box" style="font-size:14.5px; line-height:1.8; color:#0F172A;"></div>
        </div>
      </div>
    </div>

    <!-- Tab 3: Rich Discipline Evolution -->
    <div id="pane-evolution" class="tab-pane">
      <div class="scroll-page" id="timeline-box"></div>
    </div>

    <!-- Tab 4: Research Clusters -->
    <div id="pane-clusters" class="tab-pane">
      <div class="scroll-page">
        <h2 style="margin-bottom: 18px; color: #0F172A;">📊 研究流派与主题聚类分析 (Research Clusters)</h2>
        <div class="cluster-grid" id="clusters-box"></div>
      </div>
    </div>

    <!-- Tab 5: Papers -->
    <div id="pane-papers" class="tab-pane">
      <div class="papers-container" id="papers-grid"></div>
    </div>
  </main>

  <script>
    const graphData = {{graphJson}};
    const papersData = {{papersJson}};
    const bib = graphData.bibliometrics || {};

    let network = null;
    let paperNetwork = null;
    let coNetwork = null;
    let allNodeItems = [];
    let allEdgeItems = [];
    let nodes = null;
    let edges = null;

    function getGraphOptions(mode) {
      if (mode === 'hier-lr' || mode === 'hier-ud') {
        return {
          layout: {
            hierarchical: {
              enabled: true,
              direction: mode === 'hier-lr' ? 'LR' : 'UD',
              sortMethod: 'directed',
              levelSeparation: 180,
              nodeSpacing: 130
            }
          },
          physics: false,
          interaction: { hover: true }
        };
      }
      return {
        layout: { hierarchical: false },
        physics: {
          solver: 'forceAtlas2Based',
          forceAtlas2Based: { gravitationalConstant: -80, centralGravity: 0.008, springLength: 150, springConstant: 0.06 },
          stabilization: { iterations: 120 }
        },
        interaction: { hover: true }
      };
    }

    function switchTab(name, btn) {
      document.querySelectorAll('.nav-tabs .tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
      if (btn) btn.classList.add('active');
      document.getElementById('pane-' + name).classList.add('active');

      setTimeout(() => {
        if (name === 'graph') {
          initConceptGraph();
          if (network) { network.redraw(); network.fit(); }
        } else if (name === 'coword') {
          initCoWordGraph();
          if (coNetwork) { coNetwork.redraw(); coNetwork.fit(); }
        } else if (name === 'paper-graph') {
          initPaperGraph();
          if (paperNetwork) { paperNetwork.redraw(); paperNetwork.fit(); }
        }
      }, 40);
    }

    // 1. Render Evolution Timeline
    try {
      const timelineBox = document.getElementById('timeline-box');
      if (graphData.evolution && graphData.evolution.length > 0) {
        graphData.evolution.forEach(item => {
          const div = document.createElement('div');
          div.className = 'timeline-item';
          const repPapers = (item.representativePapers || []).join('；') || '—';
          div.innerHTML = `
            <div class="evo-card">
              <div style="font-size:18px; font-weight:700; color:#0369A1; margin-bottom:4px;">${item.stage}</div>
              <div style="font-size:14.5px; font-weight:600; color:#059669; margin-bottom:10px;">🧭 主导范式：${item.paradigm}</div>
              <div class="evo-meta-grid">
                <div class="evo-meta-item"><strong>❓ 核心学术追问：</strong><br>${item.drivingQuestion || '详见下方关键突破'}</div>
                <div class="evo-meta-item"><strong>🛠️ 方法与工具变革：</strong><br>${item.methodShift || '详见下方关键突破'}</div>
              </div>
              <div style="font-size:13.5px; margin:10px 0;"><strong>📚 代表性文献：</strong> <span style="color:#0284C7;font-weight:600;">${repPapers}</span></div>
              <div style="font-size:13.5px; margin-top:8px;"><strong>🚀 关键突破与核心进展：</strong></div>
              <ul style="padding-left:20px; margin-top:4px; line-height:1.7; font-size:13.5px; color:#1E293B;">
                ${(item.keyDevelopments || []).map(p => `<li>${p}</li>`).join('')}
              </ul>
              ${item.unresolvedGap ? `<div style="margin-top:10px; background:#FEF2F2; border:1px solid #FECACA; color:#991B1B; padding:8px 12px; border-radius:6px; font-size:13px;"><strong>⚠️ 遗留瓶颈与向下一阶段演进推力：</strong> ${item.unresolvedGap}</div>` : ''}
            </div>
          `;
          timelineBox.appendChild(div);
        });
      } else {
        timelineBox.innerHTML = '<p style="color:var(--text-muted)">未提取出阶段性演进信息。</p>';
      }
    } catch (e) { console.error(e); }

    // 2. Render Research Clusters (with automatic fallback to Topic Models if empty)
    try {
      const clustersBox = document.getElementById('clusters-box');
      let clustersList = graphData.researchClusters || [];
      if (clustersList.length === 0 && bib.topicModels && bib.topicModels.length > 0) {
        clustersList = bib.topicModels.map(tm => ({
          clusterName: tm.topicLabel,
          themeSummary: `围绕核心学术特征词 [${(tm.topTerms || []).join(', ')}] 聚合形成的主题研究群（语料权重占比 ${tm.weightPercent}%）。`,
          papers: tm.dominantPapers || [],
          consensusAndDivergence: '该主题簇内文献高度共享核心术语与理论构念，从不同学习情境与实证设计切入共同的学科核心问题。'
        }));
      }
      if (clustersList.length > 0) {
        clustersList.forEach(c => {
          const div = document.createElement('div');
          div.className = 'cluster-card';
          div.innerHTML = `
            <h3 style="font-size:17px; color:#0369A1; margin-bottom:8px;">📌 ${c.clusterName}</h3>
            <p style="font-size:14px; line-height:1.65; color:#0F172A; margin-bottom:12px;"><strong>核心主张：</strong>${c.themeSummary}</p>
            <div style="font-size:13px; background:#F8FAFC; padding:10px; border-radius:6px; border:1px solid #E2E8F0; margin-bottom:10px;">
              <strong>归属文献：</strong> ${(c.papers || []).join(' ； ')}
            </div>
            <div style="font-size:13px; background:#F0FDF4; padding:10px; border-radius:6px; border:1px solid #BBF7D0; color:#14532D;">
              <strong>⚖️ 共识与内部分歧：</strong> ${c.consensusAndDivergence || '—'}
            </div>
          `;
          clustersBox.appendChild(div);
        });
      } else {
        clustersBox.innerHTML = '<p style="color:var(--text-muted)">暂无流派聚类数据。</p>';
      }
    } catch (e) { console.error(e); }

    // 3. Render Papers Grid
    try {
      const papersGrid = document.getElementById('papers-grid');
      papersData.forEach(p => {
        const card = document.createElement('div');
        card.className = 'paper-card';
        card.innerHTML = `
          <div class="paper-title">${p.title}</div>
          <div class="paper-meta">作者：${p.authors || '未知'} | 年份：${p.Year || p.year || '未知'} | Key: ${p.itemKey}</div>
          <div class="paper-snippet">${p.matchedChunk ? p.matchedChunk.snippet : '无高亮片段'}</div>
          <div style="margin-top: auto; display: flex; gap: 12px;">
            <a href="${p.links ? p.links.select : '#'}" style="font-size:13px; font-weight:600; color:var(--accent);">📌 在 Zotero 中定位</a>
          </div>
        `;
        papersGrid.appendChild(card);
      });
    } catch (e) { console.error(e); }

    // 4. Concept Knowledge Graph (Lazy-safe initialization)
    function initConceptGraph() {
      if (network || typeof vis === 'undefined') return;
      try {
        const categoryColors = {
          TheoreticalFramework: '#0284C7',
          Methodology: '#059669',
          ConstructOrVariable: '#D97706',
          EmpiricalFinding: '#7C3AED',
          TechnologyOrTool: '#0D9488',
          ResearchGap: '#E11D48',
          General: '#0284C7'
        };

        const seenIds = new Set();
        allNodeItems = [];
        (graphData.nodes || []).forEach((n, idx) => {
          const nid = n.id || ('N_' + idx);
          if (seenIds.has(nid)) return;
          seenIds.add(nid);
          allNodeItems.push({
            id: nid,
            label: (n.label || nid) + (n.year ? `\n(${n.year})` : ''),
            shape: 'dot',
            size: 15 + (n.importance || 5) * 2.3,
            color: {
              background: categoryColors[n.category] || '#0284C7',
              border: '#0F172A',
              highlight: { background: '#0F172A', border: categoryColors[n.category] || '#0284C7' }
            },
            borderWidth: 2,
            font: { color: '#0F172A', size: 13, face: 'Microsoft YaHei, Segoe UI', strokeWidth: 3, strokeColor: '#FFFFFF' },
            raw: n
          });
        });

        allEdgeItems = (graphData.edges || []).map((e, idx) => ({
          id: 'e_' + idx,
          from: e.source,
          to: e.target,
          label: e.relation,
          value: e.weight || 2,
          font: { color: '#334155', size: 11.5, align: 'middle', background: '#FFFFFF' },
          arrows: 'to',
          color: { color: '#64748B', highlight: '#0284C7' },
          smooth: { type: 'continuous' },
          raw: e
        }));

        nodes = new vis.DataSet(allNodeItems);
        edges = new vis.DataSet(allEdgeItems);

        const container = document.getElementById('network-canvas');
        network = new vis.Network(container, { nodes, edges }, getGraphOptions('force'));

        network.on('click', function(params) {
          const inspector = document.getElementById('inspector-content');
          if (params.nodes.length > 0) {
            const nodeId = params.nodes[0];
            const n = nodes.get(nodeId).raw;
            const relatedEdges = allEdgeItems.filter(e => e.from === nodeId || e.to === nodeId);
            const relHtml = relatedEdges.map(e => `<li><code>${e.raw.source}</code> ➔ <strong>${e.raw.relation}</strong> ➔ <code>${e.raw.target}</code>: ${e.raw.detail || ''}</li>`).join('');
            const evHtml = (n.evidence || '').replace(/\[PDF:([A-Za-z0-9]+),\s*p\.?\s*(\d+)\]/gi, '<a class="badge" href="zotero://open-pdf/library/items/$1?page=$2" style="text-decoration:none;">📄 打开PDF p.$2 ↗</a>');
            inspector.innerHTML = `
              <h4 style="color:#0F172A; font-size:17px; margin-bottom:8px;">${n.label}</h4>
              <p style="margin-bottom:5px;"><strong>学术分类:</strong> <span class="badge">${n.category}</span> &nbsp; <strong>重要度:</strong> ⭐ ${n.importance || 5}/10</p>
              <p style="margin-bottom:5px;"><strong>来源文献:</strong> ${n.paper || '—'} (${n.year || '—'})</p>
              <div class="detail-box"><strong>📖 学术定义与理论机制：</strong><br>${n.summary || '—'}</div>
              ${evHtml ? `<div class="detail-box" style="border-left:3px solid #059669;"><strong>🔬 原文实证发现 / 证据 (支持点击跳页)：</strong><br>${evHtml}</div>` : ''}
              ${n.limitation ? `<div class="detail-box" style="border-left:3px solid #E11D48;"><strong>⚠️ 理论局限与学术争议：</strong><br>${n.limitation}</div>` : ''}
              ${relHtml ? `<div class="detail-box"><strong>🔗 关联边 (${relatedEdges.length})：</strong><ul style="padding-left:16px;margin-top:4px;">${relHtml}</ul></div>` : ''}
            `;
          } else if (params.edges.length > 0) {
            const edgeId = params.edges[0];
            const e = edges.get(edgeId).raw;
            inspector.innerHTML = `
              <h4 style="color:#0F172A; font-size:16px; margin-bottom:8px;">🔗 概念关系详情</h4>
              <p style="margin-bottom:6px;"><strong>关系性质:</strong> <span class="badge">${e.relation}</span> (强度: ${e.weight || 3}/5)</p>
              <p style="margin-bottom:6px;"><strong>作用方向:</strong> ${e.source} ➔ ${e.target}</p>
              <div class="detail-box">${e.detail || '无详细描述'}</div>
            `;
          }
        });
      } catch (e) { console.error(e); }
    }

    function changeGraphLayout(mode) {
      if (!network) initConceptGraph();
      if (network) {
        network.setOptions(getGraphOptions(mode));
        setTimeout(() => network.fit(), 150);
      }
    }

    function filterGraphNodes() {
      if (!nodes) return;
      const cat = document.getElementById('category-filter').value;
      const q = (document.getElementById('node-search').value || '').toLowerCase();
      nodes.clear();
      const filtered = allNodeItems.filter(item => {
        const matchCat = (cat === 'ALL') || (item.raw.category === cat);
        const matchQ = !q || (item.raw.label && item.raw.label.toLowerCase().includes(q)) ||
                       (item.raw.paper && item.raw.paper.toLowerCase().includes(q)) ||
                       (item.raw.summary && item.raw.summary.toLowerCase().includes(q));
        return matchCat && matchQ;
      });
      nodes.add(filtered);
    }

    // 5. Connected Papers Network (Initialized when tab becomes visible so container size is non-zero)
    function initPaperGraph() {
      if (paperNetwork || typeof vis === 'undefined') return;
      try {
        const pConns = graphData.paperConnections || [];
        const paperTitleSet = new Set();
        pConns.forEach(c => { if(c.sourceTitle) paperTitleSet.add(c.sourceTitle); if(c.targetTitle) paperTitleSet.add(c.targetTitle); });
        if (paperTitleSet.size === 0) {
          papersData.forEach(p => { if(p.title) paperTitleSet.add(p.title); });
        }

        const pNodes = new vis.DataSet(Array.from(paperTitleSet).map((t) => ({
          id: t,
          label: t.length > 38 ? t.substring(0, 38) + '...' : t,
          shape: 'box',
          margin: 10,
          color: { background: '#E0F2FE', border: '#0284C7', highlight: { background: '#0284C7', border: '#0F172A' } },
          font: { color: '#0F172A', size: 12.5, face: 'Microsoft YaHei, Segoe UI' },
          fullTitle: t
        })));

        const pEdges = new vis.DataSet(pConns.map((c, idx) => ({
          id: 'pe_' + idx,
          from: c.sourceTitle,
          to: c.targetTitle,
          label: c.type,
          arrows: 'to',
          color: { color: '#059669', highlight: '#0284C7' },
          font: { color: '#065F46', size: 11, background: '#FFFFFF' },
          raw: c
        })));

        paperNetwork = new vis.Network(
          document.getElementById('paper-network-canvas'),
          { nodes: pNodes, edges: pEdges },
          getGraphOptions('force')
        );

        paperNetwork.on('click', function(params) {
          const box = document.getElementById('paper-inspector-content');
          if (params.edges.length > 0) {
            const c = pEdges.get(params.edges[0]).raw;
            box.innerHTML = `
              <h4 style="color:#0F172A; font-size:15.5px; margin-bottom:8px;">📖 文献对话关系</h4>
              <p style="margin-bottom:6px;"><strong>源文献:</strong> ${c.sourceTitle}</p>
              <p style="margin-bottom:6px;"><strong>目标文献:</strong> ${c.targetTitle}</p>
              <p style="margin-bottom:6px;"><strong>关系类型:</strong> <span class="badge">${c.type}</span></p>
              <p style="margin-bottom:6px;"><strong>共享核心构念:</strong> ${c.sharedConcepts || '—'}</p>
              <div class="detail-box">${c.explanation || '—'}</div>
            `;
          } else if (params.nodes.length > 0) {
            const t = params.nodes[0];
            const related = pConns.filter(c => c.sourceTitle === t || c.targetTitle === t);
            box.innerHTML = `
              <h4 style="color:#0F172A; font-size:15.5px; margin-bottom:8px;">📄 ${t}</h4>
              <div class="detail-box"><strong>直接关联边 (${related.length}):</strong>
                <ul style="padding-left:16px;margin-top:6px;">
                  ${related.map(r => `<li><strong>[${r.type}]</strong> ${r.sourceTitle} ➔ ${r.targetTitle}: ${r.explanation}</li>`).join('')}
                </ul>
              </div>
            `;
          }
        });
      } catch (e) { console.error(e); }
    }

    // 6. Co-occurrence Word Network (Initialized when tab becomes visible)
    const topicPalette = ['#0284C7', '#059669', '#D97706', '#7C3AED', '#E11D48'];

    function initCoWordGraph() {
      if (coNetwork || typeof vis === 'undefined') return;
      try {
        const seenCo = new Set();
        const coList = [];
        (bib.coOccurrenceNodes || []).forEach(n => {
          if (seenCo.has(n.id)) return;
          seenCo.add(n.id);
          coList.push({
            id: n.id,
            label: n.term,
            shape: 'dot',
            size: Math.min(36, 12 + Math.sqrt(n.tfIdf || 4) * 2.2),
            color: {
              background: topicPalette[((n.topicCluster || 1) - 1) % topicPalette.length] || '#0284C7',
              border: '#0F172A'
            },
            font: { color: '#0F172A', size: 13, face: 'Microsoft YaHei, Segoe UI', strokeWidth: 3, strokeColor: '#FFFFFF' },
            raw: n
          });
        });

        const coNodesData = new vis.DataSet(coList);
        const coEdgesData = new vis.DataSet((bib.coOccurrenceEdges || []).map((e, i) => ({
          id: 'coe_' + i,
          from: e.source,
          to: e.target,
          width: Math.min(6, 1 + Math.log2(1 + e.coCount)),
          color: { color: '#94A3B8', highlight: '#0284C7' },
          smooth: { type: 'continuous' },
          raw: e
        })));

        coNetwork = new vis.Network(
          document.getElementById('co-network-canvas'),
          { nodes: coNodesData, edges: coEdgesData },
          getGraphOptions('force')
        );

        coNetwork.on('click', function(params) {
          const box = document.getElementById('co-inspector-content');
          if (params.nodes.length > 0) {
            const nid = params.nodes[0];
            const n = coNodesData.get(nid).raw;
            const collocs = (bib.coOccurrenceEdges || [])
              .filter(e => e.source === nid || e.target === nid)
              .map(e => ({ other: e.source === nid ? e.target : e.source, cnt: e.coCount, pmi: e.pmi }));
            box.innerHTML = `
              <h4 style="color:#0F172A; font-size:16px; margin-bottom:6px;">🔤 术语：${n.term}</h4>
              <p><strong>所属主题簇:</strong> <span class="badge">Topic ${n.topicCluster}</span></p>
              <p style="margin-top:4px;"><strong>总词频 (TF):</strong> ${n.termFreq} 次 | <strong>分布文献数 (DF):</strong> ${n.docFreq} 篇 | <strong>TF-IDF:</strong> ${n.tfIdf}</p>
              <div class="detail-box"><strong>最强共现搭配词 (Collocations):</strong>
                <ul style="padding-left:16px; margin-top:4px;">
                  ${collocs.map(c => `<li><strong>${c.other}</strong> (共现 ${c.cnt} 次, PMI=${c.pmi})</li>`).join('') || '<li>无显著高频共现</li>'}
                </ul>
              </div>
            `;
          }
        });
      } catch (e) { console.error(e); }
    }

    // 7. Topic Modeling Sidebar & Bibliometrics Dashboard
    try {
      const topicsSideBox = document.getElementById('topics-sidebar-box');
      (bib.topicModels || []).forEach(tm => {
        const div = document.createElement('div');
        div.className = 'detail-box';
        div.style.borderLeft = `4px solid ${topicPalette[((tm.topicId || 1) - 1) % topicPalette.length]}`;
        div.innerHTML = `
          <div style="font-weight:700; color:#0F172A; margin-bottom:4px;">${tm.topicLabel} (${tm.weightPercent}%)</div>
          <div style="font-size:12px; color:#0369A1; margin-bottom:4px;"><strong>核心特征词:</strong> ${(tm.topTerms || []).join(', ')}</div>
          <div style="font-size:12px; color:#475569;"><strong>代表文献:</strong> ${(tm.dominantPapers || []).join('；')}</div>
        `;
        topicsSideBox.appendChild(div);
      });

      const kpiBox = document.getElementById('biblio-kpi-box');
      const kpis = [
        { label: '全文语料库总词数', val: (bib.totalCorpusWords || 0).toLocaleString() + ' 词', col: '#0284C7' },
        { label: 'OpenAlex 累计总被引', val: (bib.totalCitations || 0) + ' 次', col: '#059669' },
        { label: '篇均被引频次', val: (bib.meanCitations || 0) + ' 次', col: '#D97706' },
        { label: '平均领域归一化影响力 (FWCI)', val: bib.meanFwci || '—', col: '#7C3AED' },
        { label: '发表年份跨度', val: bib.yearRange || '—', col: '#0D9488' }
      ];
      kpiBox.innerHTML = kpis.map(k => `
        <div class="evo-card" style="text-align:center; padding:14px;">
          <div style="font-size:12.5px; color:#475569; font-weight:600;">${k.label}</div>
          <div style="font-size:22px; font-weight:800; color:${k.col}; margin-top:6px;">${k.val}</div>
        </div>
      `).join('');

      const bibTable = document.getElementById('biblio-table');
      const pMetrics = bib.paperMetrics || [];
      bibTable.innerHTML = `
        <thead>
          <tr style="background:#F1F5F9; border-bottom:2px solid #CBD5E1; text-align:left;">
            <th style="padding:8px;">文献标题</th>
            <th style="padding:8px;">作者 (年份)</th>
            <th style="padding:8px;">期刊/来源</th>
            <th style="padding:8px;">DOI</th>
            <th style="padding:8px;">OpenAlex被引</th>
            <th style="padding:8px;">FWCI</th>
            <th style="padding:8px;">全文词数</th>
            <th style="padding:8px;">TF-IDF 特征词</th>
          </tr>
        </thead>
        <tbody>
          ${pMetrics.map(m => `
            <tr style="border-bottom:1px solid #E2E8F0;">
              <td style="padding:8px; font-weight:600; color:#0F172A;">${m.title}</td>
              <td style="padding:8px; white-space:nowrap;">${m.authors} (${m.year || '—'})</td>
              <td style="padding:8px; color:#0369A1;">${m.venue || '—'}</td>
              <td style="padding:8px; font-family:Consolas; font-size:12px;">${m.doi && m.doi !== '—' ? `<a href="https://doi.org/${m.doi}" target="_blank" style="color:#0284C7;">${m.doi}</a>` : '—'}</td>
              <td style="padding:8px; font-weight:700; color:#059669; text-align:center;">${m.citedByCount}</td>
              <td style="padding:8px; text-align:center;">${m.fwci || '—'}</td>
              <td style="padding:8px; text-align:right;">${(m.wordCount || 0).toLocaleString()}</td>
              <td style="padding:8px; font-size:12px; color:#475569;">${(m.distinctiveKeywords || []).join(', ')}</td>
            </tr>
          `).join('')}
        </tbody>
      `;

      const couplingBox = document.getElementById('biblio-coupling-box');
      const couplings = bib.bibliographicCouplings || [];
      if (couplings.length > 0) {
        couplingBox.innerHTML = '<ul style="padding-left:20px;">' + couplings.map(c => `
          <li style="margin-bottom:6px;">
            <strong>《${c.paperA}》</strong> ⟷ <strong>《${c.paperB}》</strong>：
            共享参考文献 <span class="badge">${c.sharedReferences} 篇</span> (Cosine 耦合强度: <strong>${c.couplingStrength}</strong>)
            ${c.isDirectCitation ? '<span class="badge" style="background:#FEE2E2;color:#991B1B;border-color:#FCA5A5;">存在直接引证关系</span>' : ''}
          </li>
        `).join('') + '</ul>';
      } else {
        couplingBox.innerHTML = '<p style="color:var(--text-muted)">所选文献在 OpenAlex 中暂未检测到重叠的参考文献耦合对。</p>';
      }

      const semTable = document.getElementById('biblio-seminal-table');
      const semWorks = bib.coCitedSeminalWorks || [];
      if (semWorks.length > 0) {
        semTable.innerHTML = `
          <thead>
            <tr style="background:#F1F5F9; border-bottom:2px solid #CBD5E1; text-align:left;">
              <th style="padding:8px;">共引奠基文献标题</th>
              <th style="padding:8px;">作者 (年份)</th>
              <th style="padding:8px;">期刊/来源</th>
              <th style="padding:8px;">DOI直达</th>
              <th style="padding:8px; text-align:center;">被本组共引</th>
              <th style="padding:8px; text-align:center;">全球总被引</th>
              <th style="padding:8px; text-align:center;">Zotero入库状态</th>
            </tr>
          </thead>
          <tbody>
            ${semWorks.map(sw => `
              <tr style="border-bottom:1px solid #E2E8F0;">
                <td style="padding:8px; font-weight:600; color:#0F172A;">${sw.title}</td>
                <td style="padding:8px; white-space:nowrap;">${sw.authors} (${sw.year || '—'})</td>
                <td style="padding:8px; color:#0369A1;">${sw.venue || '—'}</td>
                <td style="padding:8px; font-family:Consolas; font-size:12px;">${sw.doi && sw.doi !== '—' ? `<a href="https://doi.org/${sw.doi}" target="_blank" style="color:#0284C7;">${sw.doi} ↗</a>` : '—'}</td>
                <td style="padding:8px; text-align:center;"><span class="badge">${sw.coCitedCount} 篇共引</span></td>
                <td style="padding:8px; font-weight:700; color:#059669; text-align:center;">${sw.globalCitations}</td>
                <td style="padding:8px; text-align:center;">
                  ${sw.inLocalZotero
                    ? '<span class="badge" style="background:#D1FAE5;color:#065F46;border-color:#6EE7B7;">✅ 已在本地Zotero</span>'
                    : '<span class="badge" style="background:#FEF3C7;color:#92400E;border-color:#FCD34D;">🔍 库中缺失 · 建议补全</span>'}
                </td>
              </tr>
            `).join('')}
          </tbody>
        `;
      } else {
        semTable.innerHTML = '<tr><td style="padding:10px; color:var(--text-muted);">运行新一轮工作流后，将自动通过 OpenAlex 参考文献共引网络挖掘本领域的奠基文献与未入库缺失文献。</td></tr>';
      }

      const bibLlmBox = document.getElementById('biblio-llm-box');
      if (bib.llmInterpretationMarkdown) {
        let mdText = bib.llmInterpretationMarkdown
          .replace(/\[PDF:([A-Za-z0-9]+),\s*p\.?\s*(\d+)\]/gi, '<a class="badge" href="zotero://open-pdf/library/items/$1?page=$2" style="text-decoration:none;">📄 p.$2 ↗</a>')
          .replace(/\[\[([^\]]+)\]\]/g, '<span class="badge">🔗 $1</span>');
        bibLlmBox.innerHTML = (typeof marked !== 'undefined' && marked.parse) ? marked.parse(mdText) : ('<div style="white-space:pre-wrap">' + mdText + '</div>');
      } else {
        bibLlmBox.innerHTML = '<p style="color:var(--text-muted)">暂无 LLM 文献计量深度解读。</p>';
      }
    } catch (e) { console.error(e); }

    // Initialize Concept Graph immediately on load & observe resize
    window.addEventListener('DOMContentLoaded', () => {
      initConceptGraph();
      setTimeout(() => { if(network) { network.redraw(); network.fit(); } }, 150);
    });
    initConceptGraph();
  </script>
</body>
</html>
""";

        return html;
    }

    private static string EscapeYaml(string val)
    {
        return val.Replace("\"", "\\\"");
    }

    private static string EscapeTable(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return "—";
        return val.Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
    }

    public static string GenerateCorpusChatHtml(List<CorpusChatMessage> messages)
    {
        var msgsJson = JsonSerializer.Serialize(messages);
        return $$"""
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <script src="marked.min.js"></script>
  <script>if(typeof marked === 'undefined') document.write('<scr'+'ipt src="https://cdn.jsdelivr.net/npm/marked@12.0.1/marked.min.js"></scr'+'ipt>');</script>
  <style>
    * { box-sizing: border-box; }
    body {
      background: #F8FAFC;
      color: #0F172A;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif;
      margin: 0;
      padding: 20px 28px;
      line-height: 1.75;
      font-size: 14.5px;
    }
    .msg-card {
      background: #FFFFFF;
      border: 1px solid #CBD5E1;
      border-radius: 10px;
      padding: 16px 22px;
      margin-bottom: 16px;
      box-shadow: 0 2px 8px rgba(15, 23, 42, 0.04);
    }
    .msg-user {
      border-left: 4px solid #0284C7;
      background: #F0F9FF;
    }
    .msg-assistant {
      border-left: 4px solid #059669;
    }
    .msg-header {
      font-size: 12.5px;
      font-weight: 700;
      color: #475569;
      margin-bottom: 8px;
      display: flex;
      justify-content: space-between;
    }
    .page-link {
      display: inline-block;
      background: #D1FAE5;
      color: #065F46;
      border: 1px solid #6EE7B7;
      padding: 1px 8px;
      border-radius: 10px;
      font-size: 12px;
      font-weight: 700;
      text-decoration: none;
      margin: 0 3px;
    }
    .page-link:hover { background: #A7F3D0; }
    .wikilink {
      display: inline-block;
      background: #E0F2FE;
      color: #0369A1;
      border: 1px solid #7DD3FC;
      padding: 1px 7px;
      border-radius: 6px;
      font-size: 12.5px;
      font-weight: 600;
    }
    table { width: 100%; border-collapse: collapse; margin: 14px 0; font-size: 13.5px; }
    th, td { border: 1px solid #CBD5E1; padding: 8px 10px; text-align: left; }
    th { background: #F1F5F9; color: #0F172A; }
  </style>
</head>
<body>
  <div id="chat-box"></div>
  <script>
    const msgs = {{msgsJson}};
    const box = document.getElementById('chat-box');
    if (!msgs || msgs.length === 0) {
      box.innerHTML = `
        <div class="msg-card" style="text-align:center; padding:36px; color:#64748B; border:2px dashed #CBD5E1;">
          <h3 style="color:#0284C7; margin-bottom:8px;">💬 已选文献库原文页码级溯源问答 (Page-Grounded Corpus Q&A)</h3>
          <p>基于已提取的全文（含 <code>[Page N]</code> 页码索引），您可以直接向当前勾选的文献集提问，或点击上方快捷学术追问模板。<br>AI 回答中的每一个实证论据都会附带 <strong>📄 p.N ↗</strong> 按钮，点击即可直接唤醒 Zotero 并翻到该 PDF 的对应页码！</p>
        </div>
      `;
    } else {
      msgs.forEach(m => {
        const div = document.createElement('div');
        div.className = 'msg-card ' + (m.Role === 'user' ? 'msg-user' : 'msg-assistant');
        let body = m.ContentMarkdown || '';
        body = body.replace(/\[PDF:([A-Za-z0-9]+),\s*p\.?\s*(\d+)\]/gi, '<a class="page-link" href="zotero://open-pdf/library/items/$1?page=$2" title="点击在 Zotero 中直接打开 PDF 第 $2 页">📄 p.$2 ↗</a>');
        body = body.replace(/\[\[([^\]]+)\]\]/g, '<span class="wikilink">🔗 $1</span>');
        const htmlBody = (typeof marked !== 'undefined' && marked.parse) ? marked.parse(body) : ('<div style="white-space:pre-wrap">' + body + '</div>');
        div.innerHTML = `
          <div class="msg-header">
            <span>${m.Role === 'user' ? '🧑‍🎓 研究者提问 (Researcher)' : '🤖 文献库溯源助手 (Page-Grounded AI)'}</span>
            <span>${m.Timestamp || ''}</span>
          </div>
          <div>${htmlBody}</div>
        `;
        box.appendChild(div);
      });
      window.scrollTo(0, document.body.scrollHeight);
    }
  </script>
</body>
</html>
""";
    }
}
