using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZoteroWorkflowApp.Models;

public class LlmSettings
{
    // LLM Connection & Hyperparameters
    public string ApiUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = "";
    public string ModelName { get; set; } = "gpt-4o";
    public double Temperature { get; set; } = 0.3;
    public double TopP { get; set; } = 0.95;
    public int MaxTokens { get; set; } = 8192;
    public double PresencePenalty { get; set; } = 0.0;
    public double FrequencyPenalty { get; set; } = 0.0;
    public string ReasoningEffort { get; set; } = "none"; // none, low, medium, high
    public int RequestTimeoutSeconds { get; set; } = 300;

    // Context & Output Controls
    public int MaxCharsPerPaper { get; set; } = 25000;
    public string OutputLanguage { get; set; } = "中文 (学术规范，核心术语附英文)";

    // ZotSeek Search Defaults & Endpoints
    public string ZotSeekBaseUrl { get; set; } = "http://localhost:23119/zotseek";
    public int DefaultSearchTopK { get; set; } = 30;
    public double DefaultMinSimilarity { get; set; } = 0.25;
    public string DefaultGranularity { get; set; } = "papers"; // papers, passages

    // PDF Engine & Local Paths
    public string PdfExtractionEngine { get; set; } = "PdfPig"; // PdfPig, Anydoc, Auto
    public int MaxParallelExtractions { get; set; } = 4;
    public string ZoteroSqlitePath { get; set; } = "";
    public string ZoteroLinkedAttachmentDir { get; set; } = "";
    public string MarkdownVaultPath { get; set; } = "";

    // Markdown WYSIWYG Theme
    public string SelectedMarkdownTheme { get; set; } = "warm-paper";

    // Prompts
    public string PromptKnowledgeGraph { get; set; } = GetDefaultKnowledgeGraphPrompt();
    public string PromptBibliometricAnalysis { get; set; } = GetDefaultBibliometricPrompt();
    public string PromptLiteratureReview { get; set; } = GetDefaultLiteratureReviewPrompt();

    public static string GetDefaultKnowledgeGraphPrompt() =>
        "你是一位顶尖的应用语言学、教育技术与跨学科研究学者，擅长绘制高分辨率的领域知识图谱（参考 Connected Papers、ResearchRabbit 与 CiteSpace 的学术分析逻辑）。\n" +
        "请深度研读以下用户挑选的学术文献（包含元数据与提取出的 Markdown 全文），构建全景式的【高阶学术知识图谱与学科演进拓扑】：\n\n" +
        "1. **核心概念与实证节点（nodes，建议提取 12-25 个高价值节点）**：\n" +
        "   - `category` 请严格从以下学术分类中选择：`TheoreticalFramework`（理论框架）、`Methodology`（研究方法/范式）、`ConstructOrVariable`（核心构念/变量）、`EmpiricalFinding`（核心实证发现）、`TechnologyOrTool`（技术/测量工具/语料库）、`ResearchGap`（研究空白/争议焦点）。\n" +
        "   - 包含 `id`、`label`、`category`、`paper`（主要来源文献 Author, Year）、`year`（提出或应用年份）、`importance`（1-10）、`summary`（深入的学术定义与机制阐释）、`evidence`（原文关键证据、统计指标或典型论断，若有 `[Page N]` 页码请附注 `[PDF:附件Key, p.N]` 以支持一键跳页）、`limitation`（该概念/方法面临的批评或边界条件）。\n" +
        "2. **概念间语义与逻辑边（edges，建议 15-35 条连线）**：\n" +
        "   - `relation` 例如：`Extends`（理论扩展）、`Operationalizes`（操作化测量）、`EmpiricallyValidates`（实证支持）、`Critiques`（批判/修正）、`Synthesizes`（理论融合）、`Contradicts`（结论冲突）、`PrecursorTo`（先导基础）。\n" +
        "   - 包含 `source`、`target`、`relation`、`weight`（关系强度 1-5）、`detail`（具体学术作用机制说明）。\n" +
        "3. **学科演进历时阶段（evolution，按时间脉络划分为 3-5 个演进时期）**：\n" +
        "   - 包含 `stage`（时期与阶段名）、`paradigm`（主导研究范式）、`drivingQuestion`（该阶段的核心学术追问）、`methodShift`（研究方法与测量工具的变革）、`representativePapers`（代表性文献列表）、`keyDevelopments`（关键突破点数组）、`unresolvedGap`（遗留的方法论或理论瓶颈，如何推动下一阶段）。\n" +
        "4. **研究流派聚类（researchClusters，将所选文献归入 2-4 个主题流派）**：\n" +
        "   - 包含 `clusterName`（流派/主题群名称）、`themeSummary`（核心主张）、`papers`（归属文献列表）、`consensusAndDivergence`（内部共识与分歧点）。\n" +
        "5. **文献间互引与学术传承网络（paperConnections，Connected Papers 风格文献连线）**：\n" +
        "   - 包含 `sourceTitle`、`targetTitle`、`type`（`TheoreticalLineage` 理论传承 / `MethodologicalReplication` 方法沿用与改进 / `CounterArgument` 观点交锋 / `ComplementaryExtension` 互补拓展）、`sharedConcepts`（共享核心概念）、`explanation`（两篇文献的具体对话逻辑）。\n\n" +
        "【必须严格输出为如下 JSON 格式，不要输出 JSON 代码块以外的任何冗余解释】：\n" +
        "```json\n" +
        "{\n" +
        "  \"nodes\": [\n" +
        "    {\"id\": \"C1\", \"label\": \"概念名称\", \"category\": \"TheoreticalFramework\", \"paper\": \"Author (Year)\", \"year\": \"2018\", \"importance\": 9, \"summary\": \"学术定义与机制\", \"evidence\": \"实证发现或原文论据\", \"limitation\": \"局限性或争议\"}\n" +
        "  ],\n" +
        "  \"edges\": [\n" +
        "    {\"source\": \"C1\", \"target\": \"C2\", \"relation\": \"Operationalizes\", \"weight\": 4, \"detail\": \"具体关系阐述\"}\n" +
        "  ],\n" +
        "  \"evolution\": [\n" +
        "    {\"stage\": \"阶段一 (2005-2014): 奠基期\", \"paradigm\": \"主导范式\", \"drivingQuestion\": \"核心追问\", \"methodShift\": \"方法演变\", \"representativePapers\": [\"Author (Year)\"], \"keyDevelopments\": [\"进展1\", \"进展2\"], \"unresolvedGap\": \"遗留局限\"}\n" +
        "  ],\n" +
        "  \"researchClusters\": [\n" +
        "    {\"clusterName\": \"流派名称\", \"themeSummary\": \"核心议题阐述\", \"papers\": [\"论文A\", \"论文B\"], \"consensusAndDivergence\": \"共识与分歧\"}\n" +
        "  ],\n" +
        "  \"paperConnections\": [\n" +
        "    {\"sourceTitle\": \"论文A (Author, Year)\", \"targetTitle\": \"论文B (Author, Year)\", \"type\": \"TheoreticalLineage\", \"sharedConcepts\": \"共享构念\", \"explanation\": \"传承或对话逻辑\"}\n" +
        "  ]\n" +
        "}\n" +
        "```";

    public static string GetDefaultBibliometricPrompt() =>
        "你是一位精通科学计量学（Scientometrics / Bibliometrics）、语料库语言学（Corpus Linguistics）与文本挖掘（Text Mining）的学术分析专家。\n" +
        "系统已通过 OpenAlex API 与本地全文语料引擎计算出所选文献集的【定量文献计量数据、引文耦合网络、TF-IDF 特征词、PMI 共现词网络与主题建模簇】。\n" +
        "请你根据用户指定的以下【分析维度、分析方法与呈现规范】，撰写一份专业、深入的《文献计量与语料文本挖掘深度分析报告》：\n\n" +
        "### 一、分析什么（What to Analyze）\n" +
        "1. **引文影响力格局与高被引经典文献（Citation Impact & Seminal Works）**：分析总被引频次、篇均被引、领域归一化引文影响力（FWCI），识别其中的奠基性高被引文献（High-Impact Anchors）与近期高潜力前沿文献。\n" +
        "2. **文献耦合与知识基础结构（Bibliographic Coupling & Intellectual Base）**：基于共同参考文献数量与 Cosine 耦合强度，剖析哪些文献共享相同的知识基础，形成了怎样的核心引证社群。\n" +
        "3. **主题建模簇的学术命名与深度阐释（Topic Modeling Interpretation）**：对算法聚类出的各个 Topic Cluster（高频特征词簇）赋予精准的“学术主题名称”，并解读各主题簇的内涵、语料占比及代表性文献。\n" +
        "4. **高频共现词与 PMI 强搭配分析（Co-occurrence & Collocation Network）**：结合 TF-IDF 权重与 PMI（点互信息）强共现词对，揭示该领域的核心研究抓手、高频学术搭配与潜在热点交叉点。\n" +
        "5. **期刊阵地与年代演进趋势（Publication Venues & Temporal Trends）**：分析文献发表的时间分布特征与核心期刊阵地（Journals/Venues）偏好。\n\n" +
        "### 二、怎么分析（How to Analyze）\n" +
        "- **定量与定性三角互证（Quantitative-Qualitative Triangulation）**：切勿机械复述数字，必须将具体计量指标（如 CitedBy, FWCI, TF-IDF, PMI, 耦合度）与论文的具体学术理论、研究方法结合起来解释“为什么该文献/该词簇呈现出这一计量特征”。\n" +
        "- 对关键概念与核心词使用 `[[概念或术语]]` 双链格式，引用文献使用 `(Author, Year)` 格式。\n\n" +
        "### 三、怎么呈现（How to Present）\n" +
        "- 采用清晰的 Markdown 学术报告排版，包含：\n" +
        "  1. **核心计量洞察摘要（Executive Bibliometric Insights，3-5条要点）**\n" +
        "  2. **OpenAlex 引文影响力与文献耦合深度剖析**\n" +
        "  3. **主题建模命名与特征词簇解读表（Markdown Table：算法主题ID | 建议学术命名 | 核心特征词 | 理论/方法解读 | 代表文献）**\n" +
        "  4. **共现词网络热点与搭配机制分析**\n" +
        "  5. **基于计量与文本挖掘的选题建议（Data-Driven Research Opportunities）**";

    public static string GetDefaultLiteratureReviewPrompt() =>
        "你是一位严谨、博学且熟悉国际顶刊（SSCI/A&HCI）发表规范的学术综述专家。\n" +
        "请基于提供的论文全文 Markdown 提取内容以及前面构建的【高阶知识图谱与学科演进拓扑】，撰写一份可直接用于学术论文或研究计划书的【深度文献综述（Comprehensive Literature Review Synthesis）】。\n\n" +
        "写作与格式规范：\n" +
        "1. 采用批判性综合（Critical Synthesis）而非简单的逐篇罗列（Annotated Bibliography），强调文献之间的对话、交锋、方法论承接与理论递进。\n" +
        "2. 严格使用 APA 第7版行内引用格式，例如 `(Brookhart, 2018; Gries & Ellis, 2015)`；当引用具体论断、研究设计或实证数据且原文含有 `[Page N]` 标记时，请附上精确页码跳转标记 `[PDF:附件Key, p.N]`（如 `(Brookhart, 2018) [PDF:NT62VTVN, p.2]`），系统会将其自动渲染为点击直达 Zotero PDF 对应页码的超链接。\n" +
        "3. 对关键学术构念使用 `[[概念名称]]` 双链标记（以便与 Obsidian 知识图谱联动），但保持行文流畅自然。\n" +
        "4. 结构必须包含以下六个核心章节：\n" +
        "   - **1. 研究背景与核心问题域（Research Context & Problem Domain）**：界定核心概念、学术重要性及本综述涵盖的议题边界。\n" +
        "   - **2. 理论谱系与学科范式演进（Theoretical Genealogy & Paradigm Shifts）**：梳理从早期奠基到近期前沿的理论演化路径及驱动力。\n" +
        "   - **3. 主题流派与核心实证发现（Thematic Synthesis & Empirical Evidence）**：按主题聚类深入对比不同学者的一致结论与情境化差异。\n" +
        "   - **4. 研究方法、测量工具与语料/数据审视（Methodological & Analytical Critique）**：剖析现有研究在研究设计、样本、统计/计算模型或评估工具上的优劣。\n" +
        "   - **5. 关键学术争鸣与未解之谜（Key Controversies, Tensions & Research Gaps）**：提炼3-4个具体的理论或方法论空白（Research Gaps），并提出可操作化的未来研究问题（Future Research Questions）。\n" +
        "   - **6. 纳入文献多维综合对照矩阵（Systematic Synthesis Matrix）**：使用 Markdown 表格，包含列：`文献 (Author, Year)`、`研究问题/目的`、`理论框架`、`研究方法与样本/数据`、`核心实证发现`、`局限与启示`。";
}

public class KnowledgeGraphData
{
    [JsonPropertyName("nodes")]
    public List<KgNode> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<KgEdge> Edges { get; set; } = new();

    [JsonPropertyName("evolution")]
    public List<KgEvolutionStage> Evolution { get; set; } = new();

    [JsonPropertyName("researchClusters")]
    public List<KgResearchCluster> ResearchClusters { get; set; } = new();

    [JsonPropertyName("paperConnections")]
    public List<KgPaperConnection> PaperConnections { get; set; } = new();

    [JsonPropertyName("bibliometrics")]
    public BibliometricAndTextReport? Bibliometrics { get; set; }
}

public class KgNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "General";

    [JsonPropertyName("paper")]
    public string? Paper { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("importance")]
    public int Importance { get; set; } = 5;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("evidence")]
    public string? Evidence { get; set; }

    [JsonPropertyName("limitation")]
    public string? Limitation { get; set; }
}

public class KgEdge
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 3;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

public class KgEvolutionStage
{
    [JsonPropertyName("stage")]
    public string Stage { get; set; } = string.Empty;

    [JsonPropertyName("paradigm")]
    public string Paradigm { get; set; } = string.Empty;

    [JsonPropertyName("drivingQuestion")]
    public string? DrivingQuestion { get; set; }

    [JsonPropertyName("methodShift")]
    public string? MethodShift { get; set; }

    [JsonPropertyName("representativePapers")]
    public List<string> RepresentativePapers { get; set; } = new();

    [JsonPropertyName("keyDevelopments")]
    public List<string> KeyDevelopments { get; set; } = new();

    [JsonPropertyName("unresolvedGap")]
    public string? UnresolvedGap { get; set; }
}

public class KgResearchCluster
{
    [JsonPropertyName("clusterName")]
    public string ClusterName { get; set; } = string.Empty;

    [JsonPropertyName("themeSummary")]
    public string ThemeSummary { get; set; } = string.Empty;

    [JsonPropertyName("papers")]
    public List<string> Papers { get; set; } = new();

    [JsonPropertyName("consensusAndDivergence")]
    public string? ConsensusAndDivergence { get; set; }
}

public class KgPaperConnection
{
    [JsonPropertyName("sourceTitle")]
    public string SourceTitle { get; set; } = string.Empty;

    [JsonPropertyName("targetTitle")]
    public string TargetTitle { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sharedConcepts")]
    public string? SharedConcepts { get; set; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; set; }
}

public class BibliometricAndTextReport
{
    [JsonPropertyName("totalCitations")]
    public int TotalCitations { get; set; }

    [JsonPropertyName("meanCitations")]
    public double MeanCitations { get; set; }

    [JsonPropertyName("meanFwci")]
    public double MeanFwci { get; set; }

    [JsonPropertyName("totalCorpusWords")]
    public int TotalCorpusWords { get; set; }

    [JsonPropertyName("yearRange")]
    public string YearRange { get; set; } = "";

    [JsonPropertyName("coOccurrenceNodes")]
    public List<CoOccurrenceNode> CoOccurrenceNodes { get; set; } = new();

    [JsonPropertyName("coOccurrenceEdges")]
    public List<CoOccurrenceEdge> CoOccurrenceEdges { get; set; } = new();

    [JsonPropertyName("topicModels")]
    public List<TopicClusterModel> TopicModels { get; set; } = new();

    [JsonPropertyName("paperMetrics")]
    public List<PaperBibliometricRow> PaperMetrics { get; set; } = new();

    [JsonPropertyName("bibliographicCouplings")]
    public List<BibliographicCouplingEdge> BibliographicCouplings { get; set; } = new();

    [JsonPropertyName("coCitedSeminalWorks")]
    public List<CoCitedSeminalWork> CoCitedSeminalWorks { get; set; } = new();

    [JsonPropertyName("yearCounts")]
    public Dictionary<string, int> YearCounts { get; set; } = new();

    [JsonPropertyName("venueCounts")]
    public Dictionary<string, int> VenueCounts { get; set; } = new();

    [JsonPropertyName("llmInterpretationMarkdown")]
    public string LlmInterpretationMarkdown { get; set; } = string.Empty;
}

public class CoOccurrenceNode
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("term")]
    public string Term { get; set; } = string.Empty;

    [JsonPropertyName("termFreq")]
    public int TermFreq { get; set; }

    [JsonPropertyName("docFreq")]
    public int DocFreq { get; set; }

    [JsonPropertyName("tfIdf")]
    public double TfIdf { get; set; }

    [JsonPropertyName("topicCluster")]
    public int TopicCluster { get; set; }
}

public class CoOccurrenceEdge
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("coCount")]
    public int CoCount { get; set; }

    [JsonPropertyName("pmi")]
    public double Pmi { get; set; }
}

public class TopicClusterModel
{
    [JsonPropertyName("topicId")]
    public int TopicId { get; set; }

    [JsonPropertyName("topicLabel")]
    public string TopicLabel { get; set; } = string.Empty;

    [JsonPropertyName("topTerms")]
    public List<string> TopTerms { get; set; } = new();

    [JsonPropertyName("dominantPapers")]
    public List<string> DominantPapers { get; set; } = new();

    [JsonPropertyName("weightPercent")]
    public double WeightPercent { get; set; }
}

public class PaperBibliometricRow
{
    [JsonPropertyName("itemKey")]
    public string ItemKey { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public string Authors { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("venue")]
    public string Venue { get; set; } = string.Empty;

    [JsonPropertyName("doi")]
    public string Doi { get; set; } = string.Empty;

    [JsonPropertyName("citedByCount")]
    public int CitedByCount { get; set; }

    [JsonPropertyName("fwci")]
    public double Fwci { get; set; }

    [JsonPropertyName("refCount")]
    public int RefCount { get; set; }

    [JsonPropertyName("openAlexTopics")]
    public List<string> OpenAlexTopics { get; set; } = new();

    [JsonPropertyName("distinctiveKeywords")]
    public List<string> DistinctiveKeywords { get; set; } = new();

    [JsonPropertyName("wordCount")]
    public int WordCount { get; set; }
}

public class BibliographicCouplingEdge
{
    [JsonPropertyName("paperA")]
    public string PaperA { get; set; } = string.Empty;

    [JsonPropertyName("paperB")]
    public string PaperB { get; set; } = string.Empty;

    [JsonPropertyName("sharedReferences")]
    public int SharedReferences { get; set; }

    [JsonPropertyName("couplingStrength")]
    public double CouplingStrength { get; set; }

    [JsonPropertyName("isDirectCitation")]
    public bool IsDirectCitation { get; set; }
}

public class CoCitedSeminalWork
{
    [JsonPropertyName("openAlexId")]
    public string OpenAlexId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public string Authors { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("venue")]
    public string Venue { get; set; } = string.Empty;

    [JsonPropertyName("doi")]
    public string Doi { get; set; } = string.Empty;

    [JsonPropertyName("coCitedCount")]
    public int CoCitedCount { get; set; }

    [JsonPropertyName("globalCitations")]
    public int GlobalCitations { get; set; }

    [JsonPropertyName("inLocalZotero")]
    public bool InLocalZotero { get; set; }
}

public class CorpusChatMessage
{
    public string Role { get; set; } = "user"; // user or assistant
    public string Header { get; set; } = "";
    public string ContentMarkdown { get; set; } = "";
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");
}

public class ResearchSessionEntry
{
    public string SessionId { get; set; } = "";
    public string DisplayTitle { get; set; } = "";
    public string QueryTopic { get; set; } = "";
    public string CreatedTime { get; set; } = "";
    public string SynthesisMdPath { get; set; } = "";
    public string InteractiveHtmlPath { get; set; } = "";
}
