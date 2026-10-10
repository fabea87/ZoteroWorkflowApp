using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public class BibliometricAndTextMiningService
{
    private readonly HttpClient _httpClient;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","and","for","that","this","with","from","are","was","were","have","has","had","been",
        "not","but","what","all","when","we","there","can","more","their","if","will","one","about",
        "which","other","into","than","them","some","could","time","these","two","may","then","do",
        "first","any","my","now","such","like","our","over","man","me","even","most","made","after",
        "also","did","many","before","must","through","back","years","where","much","your","way","well",
        "down","should","because","each","just","those","people","how","too","little","state","good",
        "very","make","world","still","own","see","men","work","long","get","here","between","both",
        "life","being","under","never","day","same","another","know","while","last","might","us","great",
        "old","year","off","come","since","against","go","came","right","used","take","three","use",
        "study","studies","paper","papers","article","research","results","result","table","figure",
        "section","page","pages","journal","university","press","doi","http","https","www","org","com",
        "vol","volume","issue","pp","et","al","based","using","used","however","therefore","thus",
        "although","within","across","among","whether","towards","toward","during","including","related",
        "extracted","full","text","authors","zotero","open","key","extraction","engine","markdown","pdf",
        "four","five","six","seven","eight","nine","ten","high","low","different","new","important",
        "number","part","parts","level","levels","type","types","form","forms","case","cases","group",
        "groups","data","analysis","method","methods","approach","approaches","model","models","system"
    };

    public BibliometricAndTextMiningService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ZoteroWorkflowApp/2.0 (mailto:scholar@academic.edu)");
    }

    public async Task<BibliometricAndTextReport> RunConcurrentAnalysisAsync(
        List<ZotSeekResultItem> papers,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        ZoteroStorageResolver? resolver = null)
    {
        progress?.Report("[并发分析] 正在同步启动 OpenAlex 文献计量抓取 与 全文语料共现词/主题建模...");

        // Run OpenAlex network fetching and CPU-bound Corpus Text Mining concurrently
        var openAlexTask = FetchOpenAlexBatchAsync(papers, progress, ct);
        var textMiningTask = Task.Run(() => PerformCorpusTextMining(papers, ct), ct);

        await Task.WhenAll(openAlexTask, textMiningTask);

        var (coNodes, coEdges, topics, paperKeywords, paperWordCounts, totalWords) = await textMiningTask;

        var report = new BibliometricAndTextReport
        {
            TotalCorpusWords = totalWords,
            CoOccurrenceNodes = coNodes,
            CoOccurrenceEdges = coEdges,
            TopicModels = topics
        };

        // Discover Co-Cited Seminal & Missing Papers via OpenAlex referenced_works
        try
        {
            report.CoCitedSeminalWorks = await DiscoverCoCitedSeminalWorksAsync(papers, resolver, ct);
        }
        catch { }

        // Compile Paper Bibliometric Rows
        foreach (var p in papers)
        {
            var row = new PaperBibliometricRow
            {
                ItemKey = p.ItemKey,
                Title = p.Title,
                Authors = p.Authors ?? "Unknown",
                Year = p.Year,
                Venue = p.PublicationTitle ?? "—",
                Doi = p.Doi ?? "—",
                CitedByCount = p.CitedByCount ?? 0,
                Fwci = Math.Round(p.Fwci ?? 0.0, 2),
                RefCount = p.ReferencedWorksCount ?? p.ReferencedWorkIds.Count,
                OpenAlexTopics = p.OpenAlexTopics.Take(4).ToList(),
                DistinctiveKeywords = paperKeywords.TryGetValue(p.ItemKey, out var kws) ? kws : new List<string>(),
                WordCount = paperWordCounts.TryGetValue(p.ItemKey, out var wc) ? wc : 0
            };
            report.PaperMetrics.Add(row);

            if (p.Year.HasValue)
            {
                var yKey = p.Year.Value.ToString();
                report.YearCounts[yKey] = report.YearCounts.GetValueOrDefault(yKey, 0) + 1;
            }
            if (!string.IsNullOrWhiteSpace(p.PublicationTitle))
            {
                report.VenueCounts[p.PublicationTitle] = report.VenueCounts.GetValueOrDefault(p.PublicationTitle, 0) + 1;
            }
        }

        report.PaperMetrics = report.PaperMetrics.OrderByDescending(x => x.CitedByCount).ToList();
        report.TotalCitations = report.PaperMetrics.Sum(x => x.CitedByCount);
        report.MeanCitations = report.PaperMetrics.Count > 0 ? Math.Round(report.PaperMetrics.Average(x => x.CitedByCount), 1) : 0;
        var fwciList = report.PaperMetrics.Where(x => x.Fwci > 0).Select(x => x.Fwci).ToList();
        report.MeanFwci = fwciList.Count > 0 ? Math.Round(fwciList.Average(), 2) : 0;

        var validYears = papers.Where(p => p.Year.HasValue).Select(p => p.Year!.Value).ToList();
        if (validYears.Count > 0)
        {
            report.YearRange = $"{validYears.Min()} – {validYears.Max()}";
        }

        // Compute Bibliographic Coupling & Direct Citations from OpenAlex referenced_works
        for (int i = 0; i < papers.Count; i++)
        {
            for (int j = i + 1; j < papers.Count; j++)
            {
                var pA = papers[i];
                var pB = papers[j];

                int shared = 0;
                if (pA.ReferencedWorkIds.Count > 0 && pB.ReferencedWorkIds.Count > 0)
                {
                    var setA = new HashSet<string>(pA.ReferencedWorkIds, StringComparer.OrdinalIgnoreCase);
                    shared = pB.ReferencedWorkIds.Count(id => setA.Contains(id));
                }

                bool directCite = (!string.IsNullOrEmpty(pA.OpenAlexId) && pB.ReferencedWorkIds.Contains(pA.OpenAlexId)) ||
                                  (!string.IsNullOrEmpty(pB.OpenAlexId) && pA.ReferencedWorkIds.Contains(pB.OpenAlexId));

                if (shared > 0 || directCite)
                {
                    double denom = Math.Sqrt(Math.Max(1, pA.ReferencedWorkIds.Count) * Math.Max(1, pB.ReferencedWorkIds.Count));
                    double strength = Math.Round(shared / denom, 3);
                    report.BibliographicCouplings.Add(new BibliographicCouplingEdge
                    {
                        PaperA = pA.Title,
                        PaperB = pB.Title,
                        SharedReferences = shared,
                        CouplingStrength = strength,
                        IsDirectCitation = directCite
                    });
                }
            }
        }

        report.BibliographicCouplings = report.BibliographicCouplings
            .OrderByDescending(x => x.IsDirectCitation)
            .ThenByDescending(x => x.SharedReferences)
            .ToList();

        progress?.Report($"[计量与文本挖掘完成] OpenAlex 总被引: {report.TotalCitations} 次 | 提炼高频学术共现词: {report.CoOccurrenceNodes.Count} 个 | 主题簇: {report.TopicModels.Count} 个");
        return report;
    }

    private async Task FetchOpenAlexBatchAsync(List<ZotSeekResultItem> papers, IProgress<string>? progress, CancellationToken ct)
    {
        using var sem = new SemaphoreSlim(5);
        var tasks = papers.Select(async paper =>
        {
            await sem.WaitAsync(ct);
            try
            {
                await EnrichPaperFromOpenAlexAsync(paper, ct);
            }
            catch
            {
                // Ignore individual network failures gracefully
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task EnrichPaperFromOpenAlexAsync(ZotSeekResultItem paper, CancellationToken ct)
    {
        string url;
        if (!string.IsNullOrWhiteSpace(paper.Doi))
        {
            var cleanDoi = ZoteroStorageResolver.CleanDoi(paper.Doi);
            url = $"https://api.openalex.org/works/https://doi.org/{Uri.EscapeDataString(cleanDoi!)}";
        }
        else if (!string.IsNullOrWhiteSpace(paper.Title))
        {
            url = $"https://api.openalex.org/works?search={Uri.EscapeDataString(paper.Title)}&per-page=1";
        }
        else
        {
            return;
        }

        using var resp = await _httpClient.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return;

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement workElem = root;
        if (root.TryGetProperty("results", out var resultsArr))
        {
            if (resultsArr.GetArrayLength() == 0) return;
            workElem = resultsArr[0];
        }

        if (workElem.TryGetProperty("id", out var idProp))
            paper.OpenAlexId = idProp.GetString();

        if (workElem.TryGetProperty("doi", out var doiProp) && string.IsNullOrEmpty(paper.Doi))
        {
            var oaDoi = doiProp.GetString();
            if (!string.IsNullOrEmpty(oaDoi))
                paper.Doi = oaDoi.Replace("https://doi.org/", "", StringComparison.OrdinalIgnoreCase);
        }

        if (workElem.TryGetProperty("cited_by_count", out var citedProp) && citedProp.ValueKind == JsonValueKind.Number)
            paper.CitedByCount = citedProp.GetInt32();

        if (workElem.TryGetProperty("fwci", out var fwciProp) && fwciProp.ValueKind == JsonValueKind.Number)
            paper.Fwci = fwciProp.GetDouble();

        if (workElem.TryGetProperty("referenced_works_count", out var refCntProp) && refCntProp.ValueKind == JsonValueKind.Number)
            paper.ReferencedWorksCount = refCntProp.GetInt32();

        if (string.IsNullOrEmpty(paper.PublicationTitle) &&
            workElem.TryGetProperty("primary_location", out var loc) &&
            loc.ValueKind == JsonValueKind.Object &&
            loc.TryGetProperty("source", out var src) &&
            src.ValueKind == JsonValueKind.Object &&
            src.TryGetProperty("display_name", out var venueName))
        {
            paper.PublicationTitle = venueName.GetString();
        }

        if (workElem.TryGetProperty("referenced_works", out var refArr) && refArr.ValueKind == JsonValueKind.Array)
        {
            paper.ReferencedWorkIds.Clear();
            foreach (var r in refArr.EnumerateArray())
            {
                var rStr = r.GetString();
                if (!string.IsNullOrEmpty(rStr)) paper.ReferencedWorkIds.Add(rStr);
            }
        }

        if (workElem.TryGetProperty("topics", out var topicsArr) && topicsArr.ValueKind == JsonValueKind.Array)
        {
            paper.OpenAlexTopics.Clear();
            foreach (var t in topicsArr.EnumerateArray().Take(4))
            {
                if (t.TryGetProperty("display_name", out var dn))
                {
                    var name = dn.GetString();
                    if (!string.IsNullOrEmpty(name)) paper.OpenAlexTopics.Add(name);
                }
            }
        }
    }

    private (
        List<CoOccurrenceNode> nodes,
        List<CoOccurrenceEdge> edges,
        List<TopicClusterModel> topics,
        Dictionary<string, List<string>> paperKeywords,
        Dictionary<string, int> paperWordCounts,
        int totalWords
    ) PerformCorpusTextMining(List<ZotSeekResultItem> papers, CancellationToken ct)
    {
        int docCount = Math.Max(1, papers.Count);
        var globalTermFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var docFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var perDocTermFreq = new Dictionary<string, Dictionary<string, int>>();
        var paperWordCounts = new Dictionary<string, int>();
        var windowTermSets = new List<HashSet<string>>();
        int totalCorpusWords = 0;

        foreach (var p in papers)
        {
            ct.ThrowIfCancellationRequested();
            var rawText = !string.IsNullOrWhiteSpace(p.AnydocMarkdown)
                ? StripYamlAndMarkdownSyntax(p.AnydocMarkdown!)
                : $"{p.Title}\n{p.AbstractNote}\n{p.MatchedChunk?.Snippet}";

            var docTerms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var seenInDoc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Split into paragraphs/windows for co-occurrence
            var paragraphs = rawText.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            int docWordCount = 0;

            foreach (var para in paragraphs)
            {
                if (para.Length < 40) continue;
                var tokens = TokenizeAcademicTerms(para, out int rawTokensCount);
                docWordCount += rawTokensCount;

                var windowSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var term in tokens)
                {
                    docTerms[term] = docTerms.GetValueOrDefault(term, 0) + 1;
                    globalTermFreq[term] = globalTermFreq.GetValueOrDefault(term, 0) + 1;
                    seenInDoc.Add(term);
                    windowSet.Add(term);
                }

                if (windowSet.Count >= 2)
                {
                    windowTermSets.Add(windowSet);
                }
            }

            foreach (var t in seenInDoc)
            {
                docFreq[t] = docFreq.GetValueOrDefault(t, 0) + 1;
            }

            perDocTermFreq[p.ItemKey] = docTerms;
            paperWordCounts[p.ItemKey] = docWordCount;
            totalCorpusWords += docWordCount;
        }

        // Compute TF-IDF and select Top 36 Academic Terms (giving a boost to multi-word bigrams)
        var scoredTerms = globalTermFreq
            .Where(kv => kv.Value >= 2)
            .Select(kv =>
            {
                var term = kv.Key;
                int tf = kv.Value;
                int df = docFreq.GetValueOrDefault(term, 1);
                double idf = Math.Log(1.0 + (double)docCount / df);
                double bigramBoost = term.Contains(' ') ? 1.45 : 1.0;
                double score = tf * idf * bigramBoost;
                return new { Term = term, Tf = tf, Df = df, Score = Math.Round(score, 2) };
            })
            .OrderByDescending(x => x.Score)
            .Take(36)
            .ToList();

        var topTermSet = new HashSet<string>(scoredTerms.Select(x => x.Term), StringComparer.OrdinalIgnoreCase);

        // Compute Co-occurrence Matrix among Top 36 Terms
        var pairCounts = new Dictionary<(string, string), int>();
        foreach (var win in windowTermSets)
        {
            var present = win.Where(t => topTermSet.Contains(t)).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < present.Count; i++)
            {
                for (int j = i + 1; j < present.Count; j++)
                {
                    var key = (present[i], present[j]);
                    pairCounts[key] = pairCounts.GetValueOrDefault(key, 0) + 1;
                }
            }
        }

        int totalWindows = Math.Max(1, windowTermSets.Count);
        var coEdges = pairCounts
            .Where(kv => kv.Value >= 2)
            .Select(kv =>
            {
                var (t1, t2) = kv.Key;
                int c12 = kv.Value;
                int c1 = globalTermFreq.GetValueOrDefault(t1, 1);
                int c2 = globalTermFreq.GetValueOrDefault(t2, 1);
                double pmi = Math.Log2(((double)c12 * totalWindows) / Math.Max(1.0, (double)c1 * c2));
                return new CoOccurrenceEdge
                {
                    Source = t1,
                    Target = t2,
                    CoCount = c12,
                    Pmi = Math.Round(Math.Max(0.1, pmi), 2)
                };
            })
            .OrderByDescending(e => e.CoCount)
            .Take(65)
            .ToList();

        // Assign Topic Clusters (1..4) via greedy co-occurrence affinity
        int numTopics = Math.Min(4, Math.Max(2, scoredTerms.Count / 6));
        var termClusterMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < scoredTerms.Count; i++)
        {
            termClusterMap[scoredTerms[i].Term] = (i % numTopics) + 1;
        }
        // Refine clusters by neighbor majority vote (2 passes)
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var st in scoredTerms.Skip(numTopics))
            {
                var t = st.Term;
                var neighborWeights = new Dictionary<int, int>();
                foreach (var edge in coEdges)
                {
                    if (string.Equals(edge.Source, t, StringComparison.OrdinalIgnoreCase) && termClusterMap.TryGetValue(edge.Target, out int cTarget))
                        neighborWeights[cTarget] = neighborWeights.GetValueOrDefault(cTarget, 0) + edge.CoCount;
                    else if (string.Equals(edge.Target, t, StringComparison.OrdinalIgnoreCase) && termClusterMap.TryGetValue(edge.Source, out int cSource))
                        neighborWeights[cSource] = neighborWeights.GetValueOrDefault(cSource, 0) + edge.CoCount;
                }
                if (neighborWeights.Count > 0)
                {
                    termClusterMap[t] = neighborWeights.OrderByDescending(kv => kv.Value).First().Key;
                }
            }
        }

        var coNodes = scoredTerms.Select(x => new CoOccurrenceNode
        {
            Id = x.Term,
            Term = x.Term,
            TermFreq = x.Tf,
            DocFreq = x.Df,
            TfIdf = x.Score,
            TopicCluster = termClusterMap.GetValueOrDefault(x.Term, 1)
        }).ToList();

        // Build TopicClusterModels
        var topicModels = new List<TopicClusterModel>();
        double totalTfSum = Math.Max(1, coNodes.Sum(n => n.TermFreq));
        for (int tid = 1; tid <= numTopics; tid++)
        {
            var clusterNodes = coNodes.Where(n => n.TopicCluster == tid).OrderByDescending(n => n.TfIdf).ToList();
            if (clusterNodes.Count == 0) continue;

            var topTerms = clusterNodes.Take(8).Select(n => n.Term).ToList();
            var label = $"Topic {tid}: {string.Join(" / ", clusterNodes.Take(3).Select(n => n.Term))}";

            // Find papers with highest frequency of this topic's terms
            var domPapers = papers
                .Select(p =>
                {
                    var dMap = perDocTermFreq.GetValueOrDefault(p.ItemKey);
                    int score = dMap == null ? 0 : topTerms.Sum(t => dMap.GetValueOrDefault(t, 0));
                    return new { Paper = $"{p.Authors ?? "Unknown"} ({p.Year?.ToString() ?? "N/A"}): {p.Title}", Score = score };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(3)
                .Select(x => x.Paper)
                .ToList();

            topicModels.Add(new TopicClusterModel
            {
                TopicId = tid,
                TopicLabel = label,
                TopTerms = topTerms,
                DominantPapers = domPapers,
                WeightPercent = Math.Round(clusterNodes.Sum(n => n.TermFreq) * 100.0 / totalTfSum, 1)
            });
        }

        // Per-Paper Distinctive TF-IDF Keywords
        var paperKeywords = new Dictionary<string, List<string>>();
        foreach (var p in papers)
        {
            if (!perDocTermFreq.TryGetValue(p.ItemKey, out var dMap)) continue;
            var topForPaper = dMap
                .Where(kv => kv.Value >= 2)
                .Select(kv =>
                {
                    int df = docFreq.GetValueOrDefault(kv.Key, 1);
                    double idf = Math.Log(1.2 + (double)docCount / df);
                    double boost = kv.Key.Contains(' ') ? 1.5 : 1.0;
                    return new { Term = kv.Key, Val = kv.Value * idf * boost };
                })
                .OrderByDescending(x => x.Val)
                .Take(6)
                .Select(x => x.Term)
                .ToList();
            paperKeywords[p.ItemKey] = topForPaper;
        }

        return (coNodes, coEdges, topicModels, paperKeywords, paperWordCounts, totalCorpusWords);
    }

    private static List<string> TokenizeAcademicTerms(string paragraph, out int rawWordCount)
    {
        var results = new List<string>();
        var matches = Regex.Matches(paragraph.ToLowerInvariant(), @"\b[a-z][a-z\-]{3,24}\b|[\u4e00-\u9fa5]{2,6}");
        rawWordCount = matches.Count;

        var validUnigrams = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            var w = m.Value.Trim('-');
            if (w.Length < 4 && w[0] <= 127) continue;
            if (StopWords.Contains(w)) continue;
            validUnigrams.Add(w);
            results.Add(w);
        }

        // Also extract bigrams of adjacent valid academic words
        for (int i = 0; i < validUnigrams.Count - 1; i++)
        {
            var w1 = validUnigrams[i];
            var w2 = validUnigrams[i + 1];
            if (w1[0] <= 127 && w2[0] <= 127 && !string.Equals(w1, w2, StringComparison.OrdinalIgnoreCase))
            {
                results.Add($"{w1} {w2}");
            }
        }

        return results;
    }

    private async Task<List<CoCitedSeminalWork>> DiscoverCoCitedSeminalWorksAsync(
        List<ZotSeekResultItem> papers,
        ZoteroStorageResolver? resolver,
        CancellationToken ct)
    {
        var selectedOaIds = new HashSet<string>(
            papers.Where(p => !string.IsNullOrEmpty(p.OpenAlexId)).Select(p => p.OpenAlexId!),
            StringComparer.OrdinalIgnoreCase);

        var refCounter = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in papers)
        {
            foreach (var rId in p.ReferencedWorkIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (selectedOaIds.Contains(rId)) continue;
                refCounter[rId] = refCounter.GetValueOrDefault(rId, 0) + 1;
            }
        }

        int minCoCite = papers.Count >= 4 ? 2 : 1;
        var topRefIds = refCounter
            .Where(kv => kv.Value >= minCoCite)
            .OrderByDescending(kv => kv.Value)
            .Take(8)
            .ToList();

        if (topRefIds.Count == 0) return new List<CoCitedSeminalWork>();

        var shortIds = topRefIds
            .Select(kv => kv.Key.Replace("https://openalex.org/", "", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var filterUrl = $"https://api.openalex.org/works?filter=openalex_id:{string.Join("|", shortIds)}&per-page=10";
        using var resp = await _httpClient.GetAsync(filterUrl, ct);
        if (!resp.IsSuccessStatusCode) return new List<CoCitedSeminalWork>();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var resultsArr) || resultsArr.ValueKind != JsonValueKind.Array)
            return new List<CoCitedSeminalWork>();

        var seminalList = new List<CoCitedSeminalWork>();
        foreach (var item in resultsArr.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
            var title = item.TryGetProperty("display_name", out var dnP) ? dnP.GetString() ?? "Unknown Title" : "Unknown Title";
            int? year = item.TryGetProperty("publication_year", out var yrP) && yrP.ValueKind == JsonValueKind.Number ? yrP.GetInt32() : null;
            int globalCited = item.TryGetProperty("cited_by_count", out var cbP) && cbP.ValueKind == JsonValueKind.Number ? cbP.GetInt32() : 0;
            var doiRaw = item.TryGetProperty("doi", out var doiP) ? doiP.GetString() : null;
            var doiClean = !string.IsNullOrEmpty(doiRaw) ? doiRaw.Replace("https://doi.org/", "", StringComparison.OrdinalIgnoreCase) : "—";

            string venue = "—";
            if (item.TryGetProperty("primary_location", out var loc) && loc.ValueKind == JsonValueKind.Object &&
                loc.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object &&
                src.TryGetProperty("display_name", out var vName))
            {
                venue = vName.GetString() ?? "—";
            }

            var authNames = new List<string>();
            if (item.TryGetProperty("authorships", out var authArr) && authArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in authArr.EnumerateArray().Take(3))
                {
                    if (a.TryGetProperty("author", out var auObj) && auObj.TryGetProperty("display_name", out var auName))
                    {
                        var n = auName.GetString();
                        if (!string.IsNullOrEmpty(n)) authNames.Add(n);
                    }
                }
            }
            string authorsStr = authNames.Count switch
            {
                0 => "Unknown",
                1 => authNames[0],
                2 => $"{authNames[0]} & {authNames[1]}",
                _ => $"{authNames[0]} et al."
            };

            int coCount = refCounter.GetValueOrDefault(id, 1);
            bool inZotero = resolver != null && resolver.IsDoiInLocalZotero(doiClean);

            seminalList.Add(new CoCitedSeminalWork
            {
                OpenAlexId = id,
                Title = title,
                Authors = authorsStr,
                Year = year,
                Venue = venue,
                Doi = doiClean,
                CoCitedCount = coCount,
                GlobalCitations = globalCited,
                InLocalZotero = inZotero
            });
        }

        return seminalList.OrderByDescending(x => x.CoCitedCount).ThenByDescending(x => x.GlobalCitations).ToList();
    }

    private static string StripYamlAndMarkdownSyntax(string md)
    {
        if (md.StartsWith("---"))
        {
            var idx = md.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (idx > 0) md = md.Substring(idx + 4);
        }
        return Regex.Replace(md, @"<!--.*?-->|\[Page\s+\d+\]|https?://\S+", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    }
}
