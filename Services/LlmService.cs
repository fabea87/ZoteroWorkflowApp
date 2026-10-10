using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public class LlmService
{
    public async Task<string> TestConnectionAsync(LlmSettings settings, CancellationToken ct = default)
    {
        var resp = await CallChatCompletionAsync(
            settings,
            "You are a helpful academic assistant.",
            "Reply with one short sentence confirming the connection and your model name.",
            null,
            ct);
        return resp;
    }

    public async Task<string> CallChatCompletionAsync(
        LlmSettings settings,
        string systemPrompt,
        string userPrompt,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        bool forceDisableReasoning = false)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey) && !settings.ApiUrl.Contains("localhost") && !settings.ApiUrl.Contains("127.0.0.1"))
        {
            throw new InvalidOperationException("请先在设置中填写有效的 LLM API Key！");
        }

        var baseUrl = settings.ApiUrl.TrimEnd('/');
        var endpoint = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions";

        var activeReasoning = forceDisableReasoning ? "none" : settings.ReasoningEffort;
        progress?.Report($"[LLM] 正在请求模型 {settings.ModelName} (Temp={settings.Temperature}, Reasoning={activeReasoning}) ...");

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 60, 1800))
        };

        var payloadDict = new Dictionary<string, object>
        {
            ["model"] = settings.ModelName,
            ["temperature"] = settings.Temperature,
            ["top_p"] = settings.TopP,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        bool isReasoningEnabled = !string.IsNullOrWhiteSpace(activeReasoning) &&
                                  !string.Equals(activeReasoning, "none", StringComparison.OrdinalIgnoreCase);

        if (isReasoningEnabled)
        {
            payloadDict["reasoning_effort"] = activeReasoning.ToLowerInvariant();
            // Do NOT cap max_tokens to a small value like 8192 when reasoning_effort is active,
            // because reasoning models count thinking tokens against max_tokens and will return empty content!
            if (settings.MaxTokens >= 16384)
            {
                payloadDict["max_tokens"] = settings.MaxTokens;
            }
        }
        else if (settings.MaxTokens > 0)
        {
            payloadDict["max_tokens"] = settings.MaxTokens;
        }

        if (Math.Abs(settings.PresencePenalty) > 0.001)
        {
            payloadDict["presence_penalty"] = settings.PresencePenalty;
        }
        if (Math.Abs(settings.FrequencyPenalty) > 0.001)
        {
            payloadDict["frequency_penalty"] = settings.FrequencyPenalty;
        }

        var responseBody = await SendRequestWithFallbackAsync(httpClient, endpoint, settings.ApiKey, payloadDict, ct);

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            if (!forceDisableReasoning)
            {
                progress?.Report("[LLM 自动重试] 收到空响应，正在关闭高耗推理模式重试...");
                return await CallChatCompletionAsync(settings, systemPrompt, userPrompt, progress, ct, forceDisableReasoning: true);
            }
            throw new Exception("LLM 服务端返回了空响应体 (Empty HTTP Body)。");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var msgElem = choices[0].GetProperty("message");
            string? content = null;
            if (msgElem.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                content = contentProp.GetString();
            }

            // Strip <think>...</think> blocks if the model embeds thinking inside content
            if (!string.IsNullOrWhiteSpace(content))
            {
                content = Regex.Replace(content, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
            }

            // If content is empty (e.g. reasoning_content consumed all max_tokens!), check reasoning_content or retry without reasoning_effort
            if (string.IsNullOrWhiteSpace(content))
            {
                if (!forceDisableReasoning)
                {
                    progress?.Report("[LLM 自动调优重试] 思考 Token 耗尽导致正文为空，正在自动切换为直接输出模式重试...");
                    return await CallChatCompletionAsync(settings, systemPrompt, userPrompt, progress, ct, forceDisableReasoning: true);
                }

                if (msgElem.TryGetProperty("reasoning_content", out var rcProp) && rcProp.ValueKind == JsonValueKind.String)
                {
                    var rc = rcProp.GetString();
                    if (!string.IsNullOrWhiteSpace(rc)) return rc;
                }
            }

            return content ?? string.Empty;
        }

        throw new Exception($"LLM 响应格式异常: {responseBody}");
    }

    private static async Task<string> SendRequestWithFallbackAsync(
        HttpClient httpClient,
        string endpoint,
        string apiKey,
        Dictionary<string, object> payloadDict,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            var jsonPayload = JsonSerializer.Serialize(payloadDict);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(responseBody))
            {
                return responseBody;
            }

            // Fallback if a reasoning model rejects extra parameters or returns empty on attempt 1
            if (attempt == 1)
            {
                payloadDict = new Dictionary<string, object>
                {
                    ["model"] = payloadDict["model"],
                    ["messages"] = payloadDict["messages"]
                };
                await Task.Delay(500, ct);
                continue;
            }

            throw new Exception($"LLM 请求失败 ({response.StatusCode}): {responseBody}");
        }

        return string.Empty;
    }

    public async Task<KnowledgeGraphData> GenerateKnowledgeGraphAsync(
        LlmSettings settings,
        List<ZotSeekResultItem> papers,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // Dynamically balance per-paper character limit so total prompt stays within ~100,000 chars even when 30 papers are selected
        int dynamicPerPaperCap = Math.Clamp(100000 / Math.Max(1, papers.Count), 3500, settings.MaxCharsPerPaper);

        var sb = new StringBuilder();
        sb.AppendLine($"【输出语言要求】：请使用 {settings.OutputLanguage} 输出所有描述、演进分析与流派阐述。请务必只输出合法的 JSON 对象，不要输出任何额外解释。\n");
        sb.AppendLine("以下是用户挑选出的核心研究文献集（含 Markdown 全文提取内容）：\n");

        for (int i = 0; i < papers.Count; i++)
        {
            var p = papers[i];
            var pdfKey = !string.IsNullOrEmpty(p.PdfAttachmentKey) ? p.PdfAttachmentKey : p.ItemKey;
            sb.AppendLine($"=== 文献 [{i + 1}] ===");
            sb.AppendLine($"标题: {p.Title}");
            sb.AppendLine($"作者: {p.Authors ?? "未知"}");
            sb.AppendLine($"年份: {p.Year?.ToString() ?? "未知"}");
            sb.AppendLine($"Zotero Key: {p.ItemKey} | PDF跳页引用Key: {pdfKey} (引用特定页格式: [PDF:{pdfKey}, p.页码])");
            if (!string.IsNullOrEmpty(p.MatchedChunk?.Snippet))
            {
                sb.AppendLine($"检索高亮摘要/片段: {p.MatchedChunk.Snippet}");
            }
            sb.AppendLine("全文核心内容提取 (Markdown，含 [Page N] 页码标记):");

            var mdContent = p.AnydocMarkdown ?? string.Empty;
            if (mdContent.Length > dynamicPerPaperCap)
            {
                mdContent = mdContent.Substring(0, dynamicPerPaperCap) + $"\n\n...[已截取核心前段 {dynamicPerPaperCap} 字符]...";
            }
            sb.AppendLine(string.IsNullOrWhiteSpace(mdContent) ? "(未获取到全文，请结合标题与检索摘要分析)" : mdContent);
            sb.AppendLine();
        }

        for (int tryIdx = 0; tryIdx < 2; tryIdx++)
        {
            var rawResponse = await CallChatCompletionAsync(
                settings,
                settings.PromptKnowledgeGraph,
                sb.ToString(),
                progress,
                ct,
                forceDisableReasoning: tryIdx == 1);

            progress?.Report("[LLM] 知识图谱响应已返回，正在解析高维 JSON 拓扑结构...");

            var jsonCleaned = ExtractJsonFromMarkdown(rawResponse);
            if (!string.IsNullOrWhiteSpace(jsonCleaned) && jsonCleaned.StartsWith("{"))
            {
                try
                {
                    var graph = JsonSerializer.Deserialize<KnowledgeGraphData>(jsonCleaned, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
                    if (graph != null && (graph.Nodes.Count > 0 || graph.Evolution.Count > 0))
                    {
                        return graph;
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report($"[JSON 解析重试 ({tryIdx + 1}/2)] {ex.Message}");
                }
            }
            else
            {
                progress?.Report($"[图谱重试 ({tryIdx + 1}/2)] 模型返回内容未包含 JSON 对象，正在自动调整推理参数重试...");
            }
        }

        progress?.Report("[图谱智能兜底] 正在基于文献元数据构建基础图谱结构...");
        return BuildFallbackKnowledgeGraph(papers);
    }

    private static KnowledgeGraphData BuildFallbackKnowledgeGraph(List<ZotSeekResultItem> papers)
    {
        var g = new KnowledgeGraphData();
        for (int i = 0; i < Math.Min(15, papers.Count); i++)
        {
            var p = papers[i];
            g.Nodes.Add(new KgNode
            {
                Id = $"P{i + 1}",
                Label = p.Title.Length > 28 ? p.Title.Substring(0, 28) + "..." : p.Title,
                Category = "EmpiricalFinding",
                Paper = $"{p.Authors} ({p.Year})",
                Year = p.Year?.ToString(),
                Importance = 7,
                Summary = p.AbstractNote ?? p.MatchedChunk?.Snippet ?? p.Title
            });
            if (i > 0)
            {
                g.Edges.Add(new KgEdge
                {
                    Source = $"P{i}",
                    Target = $"P{i + 1}",
                    Relation = "Extends",
                    Weight = 3,
                    Detail = "研究主题演进关联"
                });
            }
        }
        return g;
    }

    public async Task<string> GenerateLiteratureReviewAsync(
        LlmSettings settings,
        List<ZotSeekResultItem> papers,
        KnowledgeGraphData graphData,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        int dynamicPerPaperCap = Math.Clamp(80000 / Math.Max(1, papers.Count), 3000, settings.MaxCharsPerPaper / 2);
        var sb = new StringBuilder();
        sb.AppendLine($"【输出语言要求】：请使用 {settings.OutputLanguage} 撰写文献综述。\n");
        sb.AppendLine("以下是已抽取的高阶知识图谱结构、研究流派聚类与学科演进脉络：\n");
        sb.AppendLine(JsonSerializer.Serialize(graphData, new JsonSerializerOptions { WriteIndented = true }));
        sb.AppendLine("\n以下是已挑选文献的全文核心内容：\n");

        for (int i = 0; i < papers.Count; i++)
        {
            var p = papers[i];
            var pdfKey = !string.IsNullOrEmpty(p.PdfAttachmentKey) ? p.PdfAttachmentKey : p.ItemKey;
            sb.AppendLine($"### [{i + 1}] {p.Title} ({p.Authors}, {p.Year?.ToString() ?? "未知"}) [PDF跳页引用格式: [PDF:{pdfKey}, p.页码]]");
            var mdContent = p.AnydocMarkdown ?? string.Empty;
            if (mdContent.Length > dynamicPerPaperCap)
            {
                mdContent = mdContent.Substring(0, dynamicPerPaperCap) + "...";
            }
            sb.AppendLine(string.IsNullOrWhiteSpace(mdContent) ? "(全文未提供，见标题与摘要)" : mdContent);
            sb.AppendLine();
        }

        var reviewMarkdown = await CallChatCompletionAsync(
            settings,
            settings.PromptLiteratureReview,
            sb.ToString(),
            progress,
            ct);

        return reviewMarkdown;
    }

    public async Task<string> GenerateBibliometricInterpretationAsync(
        LlmSettings settings,
        List<ZotSeekResultItem> papers,
        BibliometricAndTextReport bibReport,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("[LLM 计量解读] 正在依据您的文献计量 Prompt 对 OpenAlex 引文、共现词网络与主题建模进行深度解读...");

        var sb = new StringBuilder();
        sb.AppendLine($"【输出语言要求】：请使用 {settings.OutputLanguage} 撰写文献计量与语料文本挖掘深度解读报告。\n");
        sb.AppendLine("=== 一、OpenAlex 文献计量与全文语料挖掘定量统计数据 ===");
        sb.AppendLine($"- 纳入文献总数: {papers.Count} 篇");
        sb.AppendLine($"- 全文语料库总词数: {bibReport.TotalCorpusWords:N0} 词");
        sb.AppendLine($"- OpenAlex 累计总被引频次: {bibReport.TotalCitations} 次 (篇均 {bibReport.MeanCitations} 次)");
        sb.AppendLine($"- 平均领域归一化引文影响力 (FWCI): {bibReport.MeanFwci}");
        sb.AppendLine($"- 发表年份跨度: {bibReport.YearRange}");
        sb.AppendLine();

        sb.AppendLine("=== 二、各篇文献 OpenAlex 引文指标与 TF-IDF 特征词 ===");
        foreach (var pm in bibReport.PaperMetrics)
        {
            sb.AppendLine($"- 《{pm.Title}》 ({pm.Authors}, {pm.Year?.ToString() ?? "N/A"}) | 期刊: {pm.Venue} | DOI: {pm.Doi} | OpenAlex被引: {pm.CitedByCount} | FWCI: {pm.Fwci} | 参考文献数: {pm.RefCount} | OpenAlex学科主题: {string.Join(", ", pm.OpenAlexTopics)} | 全文TF-IDF特征词: {string.Join(", ", pm.DistinctiveKeywords)}");
        }
        sb.AppendLine();

        sb.AppendLine("=== 三、文献耦合度与直接互引关系 (Bibliographic Coupling) ===");
        if (bibReport.BibliographicCouplings.Count > 0)
        {
            foreach (var bc in bibReport.BibliographicCouplings.Take(20))
            {
                sb.AppendLine($"- 《{bc.PaperA}》 <-> 《{bc.PaperB}》: 共享参考文献 {bc.SharedReferences} 篇, Cosine耦合强度={bc.CouplingStrength}, 是否直接引证={bc.IsDirectCitation}");
            }
        }
        else
        {
            sb.AppendLine("- (暂未检出显著共享参考文献对)");
        }
        sb.AppendLine();

        sb.AppendLine("=== 四、语料库算法主题建模簇 (Topic Modeling Clusters) ===");
        foreach (var tm in bibReport.TopicModels)
        {
            sb.AppendLine($"- {tm.TopicLabel} (语料权重占比: {tm.WeightPercent}%):");
            sb.AppendLine($"  核心高频词: {string.Join(", ", tm.TopTerms)}");
            sb.AppendLine($"  高相关代表文献: {string.Join("；", tm.DominantPapers)}");
        }
        sb.AppendLine();

        sb.AppendLine("=== 五、高频学术术语 (Top TF-IDF Nodes) 与强共现搭配对 (Top PMI Co-occurrences) ===");
        sb.AppendLine("Top Terms: " + string.Join(", ", bibReport.CoOccurrenceNodes.Take(25).Select(n => $"{n.Term}(TF={n.TermFreq}, DF={n.DocFreq}, TFIDF={n.TfIdf}, Topic={n.TopicCluster})")));
        sb.AppendLine("Top Co-occurring Pairs: " + string.Join(" | ", bibReport.CoOccurrenceEdges.Take(25).Select(e => $"[{e.Source} + {e.Target}: 共现{e.CoCount}次, PMI={e.Pmi}]")));
        sb.AppendLine();

        var sysPrompt = string.IsNullOrWhiteSpace(settings.PromptBibliometricAnalysis)
            ? LlmSettings.GetDefaultBibliometricPrompt()
            : settings.PromptBibliometricAnalysis;

        var resultMd = await CallChatCompletionAsync(
            settings,
            sysPrompt,
            sb.ToString(),
            progress,
            ct);

        bibReport.LlmInterpretationMarkdown = resultMd;
        return resultMd;
    }

    public async Task<string> AskCorpusQuestionAsync(
        LlmSettings settings,
        List<ZotSeekResultItem> papers,
        KnowledgeGraphData? graphData,
        string userQuestion,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"[文献库追问] 正在基于 {papers.Count} 篇文献全文与页码索引回答追问...");

        var sysPrompt =
            "你是一位严谨的应用语言学与跨学科研究专家，正在协助学者对已选定的文献语料库进行【原文页码级深度溯源问答（Page-Grounded Corpus Q&A）】。\n" +
            "回答要求：\n" +
            "1. 严格基于所提供的文献全文（含 `[Page N]` 页码标记）与知识图谱数据作答，切勿编造文献中不存在的数据。\n" +
            "2. **必须提供精确到页码的溯源标记**：每当引用某篇文献的具体论断、研究方法、样本数据或实证发现时，除了给出 `(Author, Year)`，必须紧跟该文献对应的 `[PDF:附件Key, p.页码]` 标记（例如：`(Brookhart, 2018) [PDF:NT62VTVN, p.3]`）。系统会将 `[PDF:Key, p.N]` 自动渲染为点击直达 Zotero PDF 第 N 页的交互按钮。\n" +
            "3. 善用 Markdown 表格、分点列表与 `[[核心概念]]` 双链标记，使回答清晰、结构化、可直接用于论文写作。";

        int dynamicPerPaperCap = Math.Clamp(90000 / Math.Max(1, papers.Count), 3000, settings.MaxCharsPerPaper / 2);
        var sb = new StringBuilder();
        sb.AppendLine($"【用户追问问题】：\n{userQuestion}\n");
        sb.AppendLine($"【输出语言要求】：{settings.OutputLanguage}\n");
        sb.AppendLine("=== 纳入文献库（含 PDF 跳页 Key 与 [Page N] 原文内容）===\n");

        for (int i = 0; i < papers.Count; i++)
        {
            var p = papers[i];
            var pdfKey = !string.IsNullOrEmpty(p.PdfAttachmentKey) ? p.PdfAttachmentKey : p.ItemKey;
            sb.AppendLine($"--- 文献 [{i + 1}] ---");
            sb.AppendLine($"标题: {p.Title}");
            sb.AppendLine($"作者与年份: {p.Authors ?? "Unknown"} ({p.Year?.ToString() ?? "N/A"})");
            sb.AppendLine($"期刊: {p.PublicationTitle ?? "—"} | DOI: {p.Doi ?? "—"}");
            sb.AppendLine($"Zotero 跳页引用标记模板: [PDF:{pdfKey}, p.页码]");
            var mdContent = p.AnydocMarkdown ?? p.AbstractNote ?? p.MatchedChunk?.Snippet ?? "(无正文)";
            if (mdContent.Length > dynamicPerPaperCap)
            {
                mdContent = mdContent.Substring(0, dynamicPerPaperCap) + "...";
            }
            sb.AppendLine(mdContent);
            sb.AppendLine();
        }

        return await CallChatCompletionAsync(settings, sysPrompt, sb.ToString(), progress, ct);
    }

    private static string ExtractJsonFromMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var match = Regex.Match(text, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Multiline);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }
        int firstBrace = text.IndexOf('{');
        int lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }
        return text.Trim();
    }
}
