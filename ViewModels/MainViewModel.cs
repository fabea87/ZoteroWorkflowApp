using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZoteroWorkflowApp.Models;
using ZoteroWorkflowApp.Services;

namespace ZoteroWorkflowApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ZotSeekClient _zotSeekClient;
    private readonly AnydocService _anydocService;
    private readonly LlmService _llmService;
    private readonly BibliometricAndTextMiningService _biblioService;
    private CancellationTokenSource? _pipelineCts;

    // Platform UI Dialog Delegates (wired by MainWindow)
    public Func<string, string, Task>? ShowMessageDialogAsync { get; set; }
    public Func<string, string, string, Task<string?>>? SaveFileDialogAsync { get; set; }
    public Func<string, string?, Task<string?>>? OpenFileDialogAsync { get; set; }
    public Func<string, string?, Task<string?>>? OpenFolderDialogAsync { get; set; }

    // Services status
    [ObservableProperty]
    private string _zotSeekStatusText = "正在检测 ZotSeek ...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZotSeekDotColor))]
    private bool _isZotSeekOnline;

    [ObservableProperty]
    private string _zotSeekModelInfo = "";

    // Search parameters
    [ObservableProperty]
    private string _searchQuery = "corpus linguistics";

    [ObservableProperty]
    private int _topK = 30;

    [ObservableProperty]
    private double _minSimilarity = 0.25;

    [ObservableProperty]
    private string _selectedSearchMode = "hybrid";

    [ObservableProperty]
    private string _selectedGranularity = "papers";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<ZotSeekResultItem> _searchResults = new();

    [ObservableProperty]
    private ZotSeekResultItem? _selectedItem;

    [ObservableProperty]
    private string _selectedPaperDetailMarkdown = "请在左侧列表中点击任意一篇文献，此处将展示其完整元数据、摘要、DOI、本地 PDF 路径及全文 Markdown 预览。";

    [ObservableProperty]
    private string _selectionStatsText = "已挑选 0 / 0 篇文献 (含PDF: 0)";

    // 5-Stage Visual Pipeline Stepper ("Idle", "Running", "Done")
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stage1Bg))]
    private string _stage1State = "Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stage2Bg))]
    private string _stage2State = "Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stage3Bg))]
    private string _stage3State = "Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stage4Bg))]
    private string _stage4State = "Idle";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stage5Bg))]
    private string _stage5State = "Idle";

    // Session History Drawer
    [ObservableProperty]
    private ObservableCollection<ResearchSessionEntry> _historySessions = new();

    [ObservableProperty]
    private ResearchSessionEntry? _selectedHistorySession;

    // Settings
    [ObservableProperty]
    private LlmSettings _settings = new();

    public string UserConfigFilePath => SettingsService.UserFolderSettingsPath;

    public string AppVersionText =>
        "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    // Workflow state
    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private double _processingProgress;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private ObservableCollection<string> _executionLogs = new();

    // Results
    [ObservableProperty]
    private KnowledgeGraphData? _graphData;

    [ObservableProperty]
    private string _literatureReviewMarkdown = "";

    [ObservableProperty]
    private string _reviewWysiwygHtml = "";

    [ObservableProperty]
    private string _selectedMarkdownTheme = "warm-paper";

    [ObservableProperty]
    private string _selectedReviewViewMode = "Split"; // Wysiwyg, Split, Raw

    [ObservableProperty]
    private bool _isWysiwygDirectEdit = true;

    [ObservableProperty]
    private string _htmlExportContent = "";

    [ObservableProperty]
    private string _htmlExportFilePath = "";

    [ObservableProperty]
    private string _reviewWysiwygFilePath = "";

    // Corpus Chat (Page-Grounded Q&A)
    [ObservableProperty]
    private string _corpusChatQuestion = "";

    [ObservableProperty]
    private string _corpusChatHtmlFilePath = "";

    private readonly List<CorpusChatMessage> _chatMessages = new();

    // Tab control index: 0=Search, 1=Graph, 2=Review, 3=Chat, 4=Settings, 5=Logs
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsView0))]
    [NotifyPropertyChangedFor(nameof(IsView1))]
    [NotifyPropertyChangedFor(nameof(IsView2))]
    [NotifyPropertyChangedFor(nameof(IsView3))]
    [NotifyPropertyChangedFor(nameof(IsView4))]
    [NotifyPropertyChangedFor(nameof(IsView5))]
    [NotifyPropertyChangedFor(nameof(Nav0Bg))]
    [NotifyPropertyChangedFor(nameof(Nav1Bg))]
    [NotifyPropertyChangedFor(nameof(Nav2Bg))]
    [NotifyPropertyChangedFor(nameof(Nav3Bg))]
    [NotifyPropertyChangedFor(nameof(Nav4Bg))]
    [NotifyPropertyChangedFor(nameof(Nav5Bg))]
    private int _selectedTabIndex = 0;

    public bool IsView0 => SelectedTabIndex == 0;
    public bool IsView1 => SelectedTabIndex == 1;
    public bool IsView2 => SelectedTabIndex == 2;
    public bool IsView3 => SelectedTabIndex == 3;
    public bool IsView4 => SelectedTabIndex == 4;
    public bool IsView5 => SelectedTabIndex == 5;

    public string Nav0Bg => SelectedTabIndex == 0 ? "#E0F2FE" : "Transparent";
    public string Nav1Bg => SelectedTabIndex == 1 ? "#E0F2FE" : "Transparent";
    public string Nav2Bg => SelectedTabIndex == 2 ? "#E0F2FE" : "Transparent";
    public string Nav3Bg => SelectedTabIndex == 3 ? "#E0F2FE" : "Transparent";
    public string Nav4Bg => SelectedTabIndex == 4 ? "#E0F2FE" : "Transparent";
    public string Nav5Bg => SelectedTabIndex == 5 ? "#E0F2FE" : "Transparent";

    public string ZotSeekDotColor => IsZotSeekOnline ? "#059669" : "#DC2626";

    public string Stage1Bg => Stage1State == "Running" ? "#E0F2FE" : (Stage1State == "Done" ? "#D1FAE5" : "#F1F5F9");
    public string Stage2Bg => Stage2State == "Running" ? "#E0F2FE" : (Stage2State == "Done" ? "#D1FAE5" : "#F1F5F9");
    public string Stage3Bg => Stage3State == "Running" ? "#E0F2FE" : (Stage3State == "Done" ? "#D1FAE5" : "#F1F5F9");
    public string Stage4Bg => Stage4State == "Running" ? "#E0F2FE" : (Stage4State == "Done" ? "#D1FAE5" : "#F1F5F9");
    public string Stage5Bg => Stage5State == "Running" ? "#E0F2FE" : (Stage5State == "Done" ? "#D1FAE5" : "#F1F5F9");

    public List<string> SearchModeOptions { get; } = new() { "hybrid", "semantic", "keyword" };
    public List<string> GranularityOptions { get; } = new() { "papers", "passages" };
    public List<string> MarkdownThemeOptions { get; } = new() { "warm-paper", "obsidian", "academic-apa", "github-clean", "eye-care-green" };
    public List<string> ReviewViewModeOptions { get; } = new() { "Split", "Wysiwyg", "Raw" };
    public List<string> PdfEngineOptions { get; } = new() { "PdfPig", "Anydoc", "Auto" };
    public List<string> ReasoningOptions { get; } = new() { "none", "low", "medium", "high" };
    public List<string> LanguageOptions { get; } = new()
    {
        "中文 (学术规范，核心术语附英文)",
        "English (Academic Publication Standard)",
        "中英双语对照 (Bilingual Synthesis)"
    };

    public MainViewModel()
    {
        _zotSeekClient = new ZotSeekClient();
        _anydocService = new AnydocService();
        _llmService = new LlmService();
        _biblioService = new BibliometricAndTextMiningService();

        Settings = SettingsService.LoadSettings();

        if (Settings.DefaultSearchTopK <= 0) Settings.DefaultSearchTopK = 30;
        TopK = Settings.DefaultSearchTopK;
        MinSimilarity = Settings.DefaultMinSimilarity > 0 ? Settings.DefaultMinSimilarity : 0.25;
        SelectedGranularity = string.IsNullOrWhiteSpace(Settings.DefaultGranularity) ? "papers" : Settings.DefaultGranularity;
        SelectedMarkdownTheme = string.IsNullOrWhiteSpace(Settings.SelectedMarkdownTheme) ? "warm-paper" : Settings.SelectedMarkdownTheme;

        if (string.IsNullOrWhiteSpace(Settings.ZotSeekBaseUrl))
        {
            Settings.ZotSeekBaseUrl = "http://localhost:23119/zotseek";
        }
        _zotSeekClient.SetBaseUrl(Settings.ZotSeekBaseUrl);

        if (string.IsNullOrWhiteSpace(Settings.ZoteroSqlitePath))
        {
            Settings.ZoteroSqlitePath = _zotSeekClient.Resolver.ZoteroSqlitePath;
        }
        else
        {
            _zotSeekClient.Resolver.SetCustomSqlitePath(Settings.ZoteroSqlitePath);
        }

        if (string.IsNullOrWhiteSpace(Settings.ZoteroLinkedAttachmentDir))
        {
            Settings.ZoteroLinkedAttachmentDir = _zotSeekClient.Resolver.BaseAttachmentPath;
        }
        else
        {
            _zotSeekClient.Resolver.SetCustomBaseAttachmentPath(Settings.ZoteroLinkedAttachmentDir);
        }

        var activeVaultDir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
        ExportService.EnsureLocalJsLibraries(activeVaultDir);
        RefreshHistorySessions(activeVaultDir);
        TryLoadLatestReportFromVault(activeVaultDir);
        RefreshWysiwygHtml();
        RefreshCorpusChatHtml();
        _ = CheckZotSeekStatusAsync();
    }

    public static void OpenUriOrFileCrossPlatform(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", $"\"{target}\"");
            }
            else
            {
                Process.Start("xdg-open", $"\"{target}\"");
            }
        }
        catch { }
    }

    private Task NotifyAsync(string title, string message)
    {
        StatusMessage = message.Replace("\r", " ").Replace("\n", " ");
        Log($"[{title}] {message}");
        if (ShowMessageDialogAsync != null)
        {
            return ShowMessageDialogAsync(title, message);
        }
        return Task.CompletedTask;
    }

    partial void OnSelectedItemChanged(ZotSeekResultItem? value)
    {
        UpdateSelectedPaperInspector(value);
    }

    partial void OnSelectedHistorySessionChanged(ResearchSessionEntry? value)
    {
        if (value == null) return;
        try
        {
            if (File.Exists(value.SynthesisMdPath))
            {
                LiteratureReviewMarkdown = File.ReadAllText(value.SynthesisMdPath, System.Text.Encoding.UTF8);
            }
            if (File.Exists(value.InteractiveHtmlPath))
            {
                HtmlExportContent = File.ReadAllText(value.InteractiveHtmlPath, System.Text.Encoding.UTF8);
                HtmlExportFilePath = value.InteractiveHtmlPath;
            }
            StatusMessage = $"已切换至历史研究档案：{value.DisplayTitle}";
            Log($"[历史档案切换] 已加载：{value.DisplayTitle}");
        }
        catch { }
    }

    [RelayCommand]
    public void SwitchNav(string? indexStr)
    {
        if (int.TryParse(indexStr, out int idx))
        {
            SelectedTabIndex = idx;
        }
    }

    private void UpdateSelectedPaperInspector(ZotSeekResultItem? item)
    {
        if (item == null)
        {
            SelectedPaperDetailMarkdown = "请在左侧列表中点击任意一篇文献，此处将展示其完整元数据、摘要、DOI、本地 PDF 路径及全文 Markdown 预览。";
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"【文献标题】\n{item.Title}\n");
        sb.AppendLine($"【作者与年份】 {item.Authors ?? "未知"} ({item.Year?.ToString() ?? "未知年份"})");
        sb.AppendLine($"【发表期刊/来源】 {item.PublicationTitle ?? "—"}");
        sb.AppendLine($"【DOI】 {item.Doi ?? "未记录 (将在全文解析时自动从首页提取)"}");
        sb.AppendLine($"【Zotero 条目 Key】 {item.ItemKey}   |   【PDF 附件 Key】 {item.PdfAttachmentKey ?? "—"}");
        sb.AppendLine($"【本地 PDF 路径】 {(item.HasPdf ? item.ResolvedPdfPath : "⚠️ 无本地 PDF 附件")}");
        sb.AppendLine(new string('─', 46));

        if (!string.IsNullOrWhiteSpace(item.AbstractNote))
        {
            sb.AppendLine("【完整摘要 (Abstract)】");
            sb.AppendLine(item.AbstractNote);
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(item.MatchedChunk?.Snippet))
        {
            sb.AppendLine("【ZotSeek 语义命中片段】");
            sb.AppendLine(item.MatchedChunk.Snippet);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(item.AnydocMarkdown))
        {
            sb.AppendLine(new string('─', 46));
            sb.AppendLine($"【已提取全文 Markdown 预览 ({item.AnydocMarkdown.Length:N0} 字符)】");
            sb.AppendLine(item.AnydocMarkdown.Length > 6000
                ? item.AnydocMarkdown.Substring(0, 6000) + "\n\n...(全文较长，仅预览前 6,000 字符)..."
                : item.AnydocMarkdown);
        }

        SelectedPaperDetailMarkdown = sb.ToString();
    }

    [RelayCommand]
    public async Task PreviewSelectedPaperFullTextAsync()
    {
        if (SelectedItem == null)
        {
            await NotifyAsync("提示", "请先在左侧列表选中一篇文献！");
            return;
        }
        try
        {
            StatusMessage = $"正在提取《{SelectedItem.Title}》全文 Markdown...";
            var activeVaultDir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
            var engine = string.IsNullOrWhiteSpace(Settings.PdfExtractionEngine) ? "PdfPig" : Settings.PdfExtractionEngine;
            await _anydocService.ConvertPaperToObsidianMarkdownAsync(SelectedItem, activeVaultDir, engine);
            UpdateSelectedPaperInspector(SelectedItem);
            StatusMessage = $"已提取并预览《{SelectedItem.Authors}》全文 ({SelectedItem.AnydocMarkdown?.Length ?? 0:N0} 字符)";
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"提取预览失败: {ex.Message}");
        }
    }

    public void RefreshHistorySessions(string vaultDir)
    {
        try
        {
            if (!Directory.Exists(vaultDir)) return;
            var mdFiles = Directory.GetFiles(vaultDir, "_Synthesis_*.md")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(25)
                .ToList();

            var list = new List<ResearchSessionEntry>();
            foreach (var mdFile in mdFiles)
            {
                var fname = Path.GetFileNameWithoutExtension(mdFile);
                var stamp = fname.Replace("_Synthesis_", "");
                var htmlCandidate = Path.Combine(vaultDir, $"_InteractiveGraph_{stamp}.html");
                if (!File.Exists(htmlCandidate))
                {
                    htmlCandidate = Directory.GetFiles(vaultDir, "_InteractiveGraph_*.html")
                        .OrderBy(f => Math.Abs((File.GetLastWriteTimeUtc(f) - File.GetLastWriteTimeUtc(mdFile)).TotalSeconds))
                        .FirstOrDefault() ?? "";
                }

                string topic = "未命名主题";
                string pCount = "";
                try
                {
                    using var sr = new StreamReader(mdFile, System.Text.Encoding.UTF8);
                    for (int i = 0; i < 15 && !sr.EndOfStream; i++)
                    {
                        var line = sr.ReadLine() ?? "";
                        if (line.StartsWith("query:"))
                            topic = line.Substring(6).Trim().Trim('"');
                        else if (line.StartsWith("paper_count:"))
                            pCount = line.Substring(12).Trim() + "篇";
                    }
                }
                catch { }

                var dt = File.GetLastWriteTime(mdFile).ToString("MM-dd HH:mm");
                list.Add(new ResearchSessionEntry
                {
                    SessionId = stamp,
                    QueryTopic = topic,
                    CreatedTime = dt,
                    DisplayTitle = $"[{dt}] {topic} ({pCount})",
                    SynthesisMdPath = mdFile,
                    InteractiveHtmlPath = htmlCandidate
                });
            }

            HistorySessions = new ObservableCollection<ResearchSessionEntry>(list);
            if (HistorySessions.Count > 0 && SelectedHistorySession == null)
            {
                SelectedHistorySession = HistorySessions[0];
            }
        }
        catch { }
    }

    private void TryLoadLatestReportFromVault(string vaultDir)
    {
        try
        {
            if (!Directory.Exists(vaultDir)) return;
            var latestMd = Directory.GetFiles(vaultDir, "_Synthesis_*.md")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latestMd != null && File.Exists(latestMd))
            {
                LiteratureReviewMarkdown = File.ReadAllText(latestMd, System.Text.Encoding.UTF8);
                Log($"[自动恢复上次成果] 已加载综述: {Path.GetFileName(latestMd)}");
            }

            var latestHtml = Directory.GetFiles(vaultDir, "_InteractiveGraph_*.html")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (latestHtml != null && File.Exists(latestHtml))
            {
                HtmlExportContent = File.ReadAllText(latestHtml, System.Text.Encoding.UTF8);
                HtmlExportFilePath = latestHtml;
                Log($"[自动恢复上次图谱] 已加载知识图谱画布: {Path.GetFileName(latestHtml)}");
            }
        }
        catch { }
    }

    partial void OnLiteratureReviewMarkdownChanged(string value)
    {
        RefreshWysiwygHtml();
    }

    partial void OnSelectedMarkdownThemeChanged(string value)
    {
        Settings.SelectedMarkdownTheme = value;
        RefreshWysiwygHtml();
    }

    partial void OnIsWysiwygDirectEditChanged(bool value)
    {
        RefreshWysiwygHtml();
    }

    public void RefreshWysiwygHtml()
    {
        ReviewWysiwygHtml = ExportService.GenerateWysiwygMarkdownHtml(
            LiteratureReviewMarkdown,
            SelectedMarkdownTheme,
            IsWysiwygDirectEdit);

        try
        {
            var activeVaultDir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
            ExportService.EnsureLocalJsLibraries(activeVaultDir);
            var previewFile = Path.Combine(activeVaultDir, "_LivePreview_Review.html");
            File.WriteAllText(previewFile, ReviewWysiwygHtml, System.Text.Encoding.UTF8);
            ReviewWysiwygFilePath = "";
            ReviewWysiwygFilePath = previewFile;
        }
        catch { }
    }

    [RelayCommand]
    public async Task CheckZotSeekStatusAsync()
    {
        Log("正在探测后台 ZotSeek 服务与本地 PDF 链接库...");
        await Task.Run(() => _zotSeekClient.Resolver.EnsureInitialized(forceReload: true));
        Log($"[PDF 链接库就绪] 根目录: {_zotSeekClient.Resolver.BaseAttachmentPath} | 已映射键数: {_zotSeekClient.Resolver.IndexedPdfCount}");

        var stats = await _zotSeekClient.GetStatsAsync();
        if (stats != null && stats.Ready)
        {
            IsZotSeekOnline = true;
            ZotSeekStatusText = $"ZotSeek 在线 | 索引: {stats.IndexedPapers} 篇 | 链接库: {_zotSeekClient.Resolver.IndexedPdfCount} 键";
            ZotSeekModelInfo = $"Embedding 模型: {stats.ModelId ?? "默认"}";
            Log($"[成功连接] ZotSeek 正常工作，已索引 {stats.IndexedPapers} 篇文献。");
        }
        else
        {
            IsZotSeekOnline = false;
            ZotSeekStatusText = "ZotSeek 离线 (请确保 Zotero 运行中且开启了 AI Agent Access)";
            ZotSeekModelInfo = "端口 23119 未响应";
            Log("[连接告警] 无法连接到 ZotSeek。请确认 Zotero 7+ 已开启且在设置中启用 ZotSeek 本地服务。");
        }
    }

    [RelayCommand]
    public async Task ExecuteSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            await NotifyAsync("提示", "请输入搜索关键词或自然语言研究问题！");
            return;
        }

        try
        {
            IsSearching = true;
            StatusMessage = "正在通过 ZotSeek 语义检索文献...";
            Log($"[语义检索] 查询: '{SearchQuery}', TopK={TopK}, 模式={SelectedSearchMode}, 粒度={SelectedGranularity}, 最小相似度={MinSimilarity:F2}");

            var resp = await _zotSeekClient.SearchAsync(SearchQuery, TopK, SelectedSearchMode, SelectedGranularity, MinSimilarity);
            SearchResults.Clear();

            if (resp?.Results != null && resp.Results.Count > 0)
            {
                foreach (var item in resp.Results)
                {
                    SearchResults.Add(item);
                }
                SelectedItem = SearchResults.FirstOrDefault();
                int pdfCount = resp.Results.Count(r => r.HasPdf);
                Log($"[检索完成] 获得 {resp.Results.Count} 篇相关文献（其中 {pdfCount} 篇已关联本地链接 PDF）。");
                StatusMessage = $"获得 {resp.Results.Count} 条结果（{pdfCount} 篇含本地PDF）";
            }
            else
            {
                Log("[检索完成] 未找到匹配的文献。");
                StatusMessage = "未匹配到文献";
            }

            UpdateSelectionStats();
        }
        catch (Exception ex)
        {
            Log($"[检索错误] {ex.Message}");
            await NotifyAsync("检索错误", ex.Message);
            StatusMessage = "检索失败";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in SearchResults) item.IsSelected = true;
        SearchResults = new ObservableCollection<ZotSeekResultItem>(SearchResults);
        UpdateSelectionStats();
    }

    [RelayCommand]
    public void SelectOnlyWithPdf()
    {
        foreach (var item in SearchResults) item.IsSelected = item.HasPdf;
        SearchResults = new ObservableCollection<ZotSeekResultItem>(SearchResults);
        UpdateSelectionStats();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var item in SearchResults) item.IsSelected = false;
        SearchResults = new ObservableCollection<ZotSeekResultItem>(SearchResults);
        UpdateSelectionStats();
    }

    [RelayCommand]
    public void SortByYearDesc()
    {
        var sorted = SearchResults.OrderByDescending(x => x.Year ?? 0).ToList();
        SearchResults = new ObservableCollection<ZotSeekResultItem>(sorted);
    }

    [RelayCommand]
    public void SortByScoreDesc()
    {
        var sorted = SearchResults.OrderByDescending(x => x.SemanticScore ?? x.Score ?? 0).ToList();
        SearchResults = new ObservableCollection<ZotSeekResultItem>(sorted);
    }

    [RelayCommand]
    public async Task SnowballFromSelectedSeedsAsync()
    {
        var seeds = SearchResults.Where(x => x.IsSelected).Take(6).ToList();
        if (seeds.Count == 0 && SelectedItem != null)
        {
            seeds.Add(SelectedItem);
        }
        if (seeds.Count == 0)
        {
            await NotifyAsync("滚雪球提示", "请先勾选 1～6 篇核心种子文献，再点击【❄️ 种子文献滚雪球扩充】！");
            return;
        }

        try
        {
            IsSearching = true;
            StatusMessage = $"正在基于 {seeds.Count} 篇种子文献执行 ZotSeek 向量滚雪球扩充...";
            Log($"[滚雪球扩充] 正在对 {seeds.Count} 篇种子文献调用 /zotseek/similar ...");

            var existingKeys = new HashSet<string>(SearchResults.Select(x => x.ItemKey), StringComparer.OrdinalIgnoreCase);
            int addedCount = 0;

            foreach (var seed in seeds)
            {
                var simResp = await _zotSeekClient.FindSimilarAsync(seed.ItemKey, topK: 8);
                if (simResp?.Results == null) continue;

                foreach (var simItem in simResp.Results)
                {
                    if (string.IsNullOrEmpty(simItem.ItemKey) || existingKeys.Contains(simItem.ItemKey))
                        continue;

                    existingKeys.Add(simItem.ItemKey);
                    simItem.Source = $"❄️ 源自:{seed.Authors ?? seed.ItemKey}";
                    simItem.IsSelected = simItem.HasPdf;
                    SearchResults.Add(simItem);
                    addedCount++;
                }
            }

            UpdateSelectionStats();
            StatusMessage = $"滚雪球完成：新增 {addedCount} 篇相似文献";
            Log($"[滚雪球完成] 从 {seeds.Count} 篇种子文献扩充出 {addedCount} 篇新相关文献。");
            await NotifyAsync("滚雪球扩充成功", $"已成功通过 ZotSeek 向量空间滚雪球扩充出 {addedCount} 篇新文献（其中含本地 PDF 的文献已自动勾选）！");
        }
        catch (Exception ex)
        {
            Log($"[滚雪球异常] {ex.Message}");
            await NotifyAsync("错误", $"滚雪球扩充失败: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public async Task SnowballSingleItemAsync(ZotSeekResultItem? seed)
    {
        if (seed == null) return;
        try
        {
            IsSearching = true;
            StatusMessage = $"正在查找与《{seed.Title}》最相似的文献...";
            var existingKeys = new HashSet<string>(SearchResults.Select(x => x.ItemKey), StringComparer.OrdinalIgnoreCase);
            var simResp = await _zotSeekClient.FindSimilarAsync(seed.ItemKey, topK: 8);
            int added = 0;
            if (simResp?.Results != null)
            {
                foreach (var simItem in simResp.Results)
                {
                    if (string.IsNullOrEmpty(simItem.ItemKey) || existingKeys.Contains(simItem.ItemKey)) continue;
                    existingKeys.Add(simItem.ItemKey);
                    simItem.Source = $"❄️ 相似:{seed.Authors ?? seed.ItemKey}";
                    simItem.IsSelected = simItem.HasPdf;
                    SearchResults.Add(simItem);
                    added++;
                }
            }
            UpdateSelectionStats();
            StatusMessage = $"由《{seed.Authors}》扩充出 {added} 篇相似文献";
            Log($"[单篇滚雪球] 由 [{seed.ItemKey}] 扩充出 {added} 篇相似文献。");
        }
        catch (Exception ex)
        {
            await NotifyAsync("错误", $"查找相似文献失败: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void UpdateSelectionStats()
    {
        var selectedCount = SearchResults.Count(x => x.IsSelected);
        var total = SearchResults.Count;
        var pdfTotal = SearchResults.Count(x => x.HasPdf);
        SelectionStatsText = $"已选 {selectedCount} / {total} 篇 (有PDF: {pdfTotal} 篇)";
    }

    [RelayCommand]
    public void OpenInZotero(ZotSeekResultItem? item)
    {
        if (item == null) return;
        var uri = item.Links?.Select ?? $"zotero://select/library/items/{item.ItemKey}";
        OpenUriOrFileCrossPlatform(uri);
        Log($"[Zotero定位] 在 Zotero 中定位文献: {item.Title}");
    }

    [RelayCommand]
    public async Task OpenLocalPdf(ZotSeekResultItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.ResolvedPdfPath) || !File.Exists(item.ResolvedPdfPath))
        {
            await NotifyAsync("提示", "该文献尚未关联有效的本地 PDF 文件。");
            return;
        }
        OpenUriOrFileCrossPlatform(item.ResolvedPdfPath);
        Log($"[打开PDF] 已打开本地链接 PDF: {item.ResolvedPdfPath}");
    }

    [RelayCommand]
    public void CancelPipeline()
    {
        if (_pipelineCts != null && !_pipelineCts.IsCancellationRequested)
        {
            _pipelineCts.Cancel();
            Log("[用户取消] 正在中止当前工作流任务...");
            StatusMessage = "正在取消任务...";
        }
    }

    [RelayCommand]
    public async Task RunFullPipelineAsync()
    {
        if (IsProcessing) return;

        var selectedPapers = SearchResults.Where(x => x.IsSelected).ToList();
        if (selectedPapers.Count == 0)
        {
            await NotifyAsync("提示", "请先勾选挑选至少 1 篇感兴趣的文献！");
            return;
        }

        if (string.IsNullOrWhiteSpace(Settings.ApiKey) && !Settings.ApiUrl.Contains("localhost") && !Settings.ApiUrl.Contains("127.0.0.1"))
        {
            await NotifyAsync("提示", "请先在【5. 模型与提示词设置】页签中配置有效的 LLM API Key！");
            SelectedTabIndex = 4;
            return;
        }

        _pipelineCts = new CancellationTokenSource();
        var ct = _pipelineCts.Token;

        try
        {
            IsProcessing = true;
            ProcessingProgress = 2;
            Stage1State = "Running";
            Stage2State = "Idle";
            Stage3State = "Idle";
            Stage4State = "Idle";
            Stage5State = "Idle";
            var swTotal = Stopwatch.StartNew();

            var engine = string.IsNullOrWhiteSpace(Settings.PdfExtractionEngine) ? "PdfPig" : Settings.PdfExtractionEngine;
            int concurrency = Math.Clamp(Settings.MaxParallelExtractions, 1, 8);

            Log("=========================================");
            Log($"[工作流启动] 纳入 {selectedPapers.Count} 篇文献 | 解析引擎: {engine} (并发={concurrency}) | 模型: {Settings.ModelName}");

            var progressReporter = new Progress<string>(msg =>
            {
                StatusMessage = msg;
                Log(msg);
            });

            // Stage 1: Parallel PDF-to-Markdown Conversion into Vault
            var activeVaultDir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
            int completedPapers = 0;
            using var sem = new SemaphoreSlim(concurrency);

            var tasks = selectedPapers.Select(async p =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    await _anydocService.ConvertPaperToObsidianMarkdownAsync(p, activeVaultDir, engine, progressReporter, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ((IProgress<string>)progressReporter).Report($"[解析异常] {p.Title}: {ex.Message}");
                }
                finally
                {
                    int done = Interlocked.Increment(ref completedPapers);
                    ProcessingProgress = (done * 40.0) / selectedPapers.Count;
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            Stage1State = "Done";

            // Stage 2 & 3: CONCURRENT Execution
            Stage2State = "Running";
            Stage3State = "Running";
            StatusMessage = "并发执行中：[OpenAlex计量 + 共引发现 + 共现词建模] ∥ [LLM 知识图谱与学科演进]...";
            ProcessingProgress = 45;

            var biblioTask = Task.Run(async () =>
            {
                var r = await _biblioService.RunConcurrentAnalysisAsync(selectedPapers, progressReporter, ct, _zotSeekClient.Resolver);
                Stage2State = "Done";
                return r;
            }, ct);

            var graphTask = Task.Run(async () =>
            {
                var g = await _llmService.GenerateKnowledgeGraphAsync(Settings, selectedPapers, progressReporter, ct);
                Stage3State = "Done";
                return g;
            }, ct);

            await Task.WhenAll(biblioTask, graphTask);

            var bibReport = await biblioTask;
            var graph = await graphTask;

            Stage4State = "Running";
            ProcessingProgress = 68;
            StatusMessage = "LLM 正在生成文献计量与语料文本挖掘深度解读报告...";
            try
            {
                await _llmService.GenerateBibliometricInterpretationAsync(Settings, selectedPapers, bibReport, progressReporter, ct);
            }
            catch (Exception bibEx)
            {
                Log($"[计量解读提示] {bibEx.Message}");
            }
            Stage4State = "Done";

            graph.Bibliometrics = bibReport;
            GraphData = graph;

            Stage5State = "Running";
            ProcessingProgress = 80;
            Log($"[阶段完毕] 概念节点: {graph.Nodes.Count} | 演进时期: {graph.Evolution.Count} | 共现术语: {bibReport.CoOccurrenceNodes.Count} | 主题簇: {bibReport.TopicModels.Count} | OpenAlex累计被引: {bibReport.TotalCitations} | 共引奠基文献: {bibReport.CoCitedSeminalWorks.Count}");

            // Stage 5: Literature Review Synthesis
            StatusMessage = "LLM 正在基于知识图谱与全文撰写深度文献综述...";
            var reviewMd = await _llmService.GenerateLiteratureReviewAsync(Settings, selectedPapers, graph, progressReporter, ct);
            Stage5State = "Done";

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fullObsidianReportMd = ExportService.GenerateObsidianMarkdown(SearchQuery, selectedPapers, graph, reviewMd);
            LiteratureReviewMarkdown = fullObsidianReportMd;

            var reportFileName = $"_Synthesis_{timestamp}.md";
            var autoSavedReportPath = Path.Combine(activeVaultDir, reportFileName);
            await File.WriteAllTextAsync(autoSavedReportPath, fullObsidianReportMd, System.Text.Encoding.UTF8, ct);
            Log($"[Markdown 汇报已归档] {autoSavedReportPath}");

            ProcessingProgress = 95;

            ExportService.EnsureLocalJsLibraries(activeVaultDir);
            HtmlExportContent = ExportService.GenerateInteractiveHtml(SearchQuery, selectedPapers, graph, fullObsidianReportMd, SelectedMarkdownTheme);
            var autoSavedHtmlPath = Path.Combine(activeVaultDir, $"_InteractiveGraph_{timestamp}.html");
            await File.WriteAllTextAsync(autoSavedHtmlPath, HtmlExportContent, System.Text.Encoding.UTF8, ct);
            HtmlExportFilePath = autoSavedHtmlPath;
            Log($"[HTML 多媒体报告已归档] {autoSavedHtmlPath}");

            RefreshHistorySessions(activeVaultDir);

            swTotal.Stop();
            ProcessingProgress = 100;
            StatusMessage = $"全流程分析完成 (总耗时 {swTotal.Elapsed.TotalSeconds:F1}s)！已生成知识图谱与所见即所得综述。";
            Log($"[工作流圆满完成] 总耗时 {swTotal.Elapsed.TotalSeconds:F1} 秒。");

            SelectedTabIndex = 1;
        }
        catch (OperationCanceledException)
        {
            Log("[已取消] 工作流已被用户手动中止。");
            StatusMessage = "任务已取消";
        }
        catch (Exception ex)
        {
            Log($"[工作流失败] {ex.Message}");
            await NotifyAsync("执行失败", $"执行过程中出现错误:\n{ex.Message}");
            StatusMessage = "执行出错";
        }
        finally
        {
            IsProcessing = false;
            _pipelineCts?.Dispose();
            _pipelineCts = null;
        }
    }

    [RelayCommand]
    public async Task AskCorpusQuestionCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CorpusChatQuestion))
        {
            await NotifyAsync("提示", "请输入您想对已选文献集追问的问题，或点击上方的快捷追问按钮！");
            return;
        }
        var q = CorpusChatQuestion;
        CorpusChatQuestion = "";
        await ExecuteCorpusChatAsync(q);
    }

    [RelayCommand]
    public async Task AskPresetCorpusQuestionAsync(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return;
        await ExecuteCorpusChatAsync(preset);
    }

    private async Task ExecuteCorpusChatAsync(string question)
    {
        if (IsProcessing) return;

        var selectedPapers = SearchResults.Where(x => x.IsSelected).ToList();
        if (selectedPapers.Count == 0)
        {
            await NotifyAsync("提示", "请先在【1. 语义检索与文献挑选】页签勾选要追问的文献！");
            SelectedTabIndex = 0;
            return;
        }

        try
        {
            IsProcessing = true;
            ProcessingProgress = 25;
            StatusMessage = "正在准备文献全文与页码索引...";

            var activeVaultDir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
            var engine = string.IsNullOrWhiteSpace(Settings.PdfExtractionEngine) ? "PdfPig" : Settings.PdfExtractionEngine;

            foreach (var p in selectedPapers.Where(p => string.IsNullOrEmpty(p.AnydocMarkdown)))
            {
                try
                {
                    await _anydocService.ConvertPaperToObsidianMarkdownAsync(p, activeVaultDir, engine);
                }
                catch { }
            }

            _chatMessages.Add(new CorpusChatMessage
            {
                Role = "user",
                ContentMarkdown = question,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            });
            RefreshCorpusChatHtml();

            ProcessingProgress = 60;
            var progressReporter = new Progress<string>(msg =>
            {
                StatusMessage = msg;
                Log(msg);
            });

            var answerMd = await _llmService.AskCorpusQuestionAsync(Settings, selectedPapers, GraphData, question, progressReporter);

            _chatMessages.Add(new CorpusChatMessage
            {
                Role = "assistant",
                ContentMarkdown = answerMd,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            });
            RefreshCorpusChatHtml();

            ProcessingProgress = 100;
            StatusMessage = "文献库溯源追问回答完毕（点击回答中的 📄 p.N ↗ 可直达 Zotero PDF 对应页码）";
            Log($"[文献库追问完成] 已生成带页码溯源的回答 ({answerMd.Length} 字符)。");
        }
        catch (Exception ex)
        {
            Log($"[文献库追问失败] {ex.Message}");
            await NotifyAsync("错误", $"追问执行失败: {ex.Message}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public void RefreshCorpusChatHtml()
    {
        try
        {
            var activeVaultDir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
            ExportService.EnsureLocalJsLibraries(activeVaultDir);
            var html = ExportService.GenerateCorpusChatHtml(_chatMessages);
            var chatFile = Path.Combine(activeVaultDir, "_LivePreview_CorpusChat.html");
            File.WriteAllText(chatFile, html, System.Text.Encoding.UTF8);
            CorpusChatHtmlFilePath = "";
            CorpusChatHtmlFilePath = chatFile;
        }
        catch { }
    }

    [RelayCommand]
    public async Task AppendChatToReview()
    {
        var assistantMsgs = _chatMessages.Where(m => m.Role == "assistant").ToList();
        if (assistantMsgs.Count == 0)
        {
            await NotifyAsync("提示", "当前尚无追问回答可追加。");
            return;
        }

        var sb = new System.Text.StringBuilder(LiteratureReviewMarkdown);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## 附：文献库专题深度追问与页码级溯源核验 (Corpus Q&A Synthesis)");
        sb.AppendLine();
        for (int i = 0; i < _chatMessages.Count; i++)
        {
            var m = _chatMessages[i];
            if (m.Role == "user")
                sb.AppendLine($"### ❓ 追问：{m.ContentMarkdown}");
            else
                sb.AppendLine(m.ContentMarkdown);
            sb.AppendLine();
        }

        LiteratureReviewMarkdown = sb.ToString();
        Log("[已追加至综述] 文献库追问记录已追加至当前文献综述报告末尾。");
        await NotifyAsync("追加成功", "已将全部追问与页码溯源回答追加至【3. 文献综述】末尾！");
    }

    [RelayCommand]
    public void ClearCorpusChat()
    {
        _chatMessages.Clear();
        RefreshCorpusChatHtml();
        Log("[清空追问] 已清空文献库问答历史。");
    }

    [RelayCommand]
    public async Task TestLlmConnectionCommandAsync()
    {
        try
        {
            StatusMessage = "正在测试 LLM 接口连通性...";
            var sw = Stopwatch.StartNew();
            var reply = await _llmService.TestConnectionAsync(Settings);
            sw.Stop();
            StatusMessage = $"LLM 连通成功 ({sw.ElapsedMilliseconds} ms)";
            Log($"[LLM 测试成功] 耗时 {sw.ElapsedMilliseconds}ms, 响应: {reply}");
            await NotifyAsync("LLM 连接测试通过", $"连接成功！耗时: {sw.ElapsedMilliseconds} ms\n\n模型回复:\n{reply}");
        }
        catch (Exception ex)
        {
            StatusMessage = "LLM 连接测试失败";
            Log($"[LLM 测试失败] {ex.Message}");
            await NotifyAsync("LLM 连接测试错误", $"连接失败:\n{ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ResetPromptsToDefault()
    {
        Settings.PromptKnowledgeGraph = LlmSettings.GetDefaultKnowledgeGraphPrompt();
        Settings.PromptBibliometricAnalysis = LlmSettings.GetDefaultBibliometricPrompt();
        Settings.PromptLiteratureReview = LlmSettings.GetDefaultLiteratureReviewPrompt();
        OnPropertyChanged(nameof(Settings));
        SettingsService.SaveSettings(Settings);
        Log("[提示词已重置] 三套核心提示词已全部恢复为高阶学术默认模板。");
        await NotifyAsync("已恢复默认提示词", "【知识图谱】、【文献计量与文本挖掘】与【文献综述】三套提示词已恢复为内置高阶学术默认模板。");
    }

    [RelayCommand]
    public async Task BrowseZoteroSqliteAsync()
    {
        if (OpenFileDialogAsync == null) return;
        var initDir = !string.IsNullOrWhiteSpace(Settings.ZoteroSqlitePath) ? Path.GetDirectoryName(Settings.ZoteroSqlitePath) : null;
        var picked = await OpenFileDialogAsync("选择 Zotero 数据库文件 (zotero.sqlite)", initDir);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            Settings.ZoteroSqlitePath = picked;
            OnPropertyChanged(nameof(Settings));
            _zotSeekClient.Resolver.SetCustomSqlitePath(Settings.ZoteroSqlitePath);
            Log($"[路径设置] 已指定 Zotero 数据库文件: {Settings.ZoteroSqlitePath}");
        }
    }

    [RelayCommand]
    public async Task BrowseLinkedAttachmentDirAsync()
    {
        if (OpenFolderDialogAsync == null) return;
        var picked = await OpenFolderDialogAsync("选择 Zotero 链接 PDF 附件根目录 (baseAttachmentPath)", Settings.ZoteroLinkedAttachmentDir);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            Settings.ZoteroLinkedAttachmentDir = picked;
            OnPropertyChanged(nameof(Settings));
            _zotSeekClient.Resolver.SetCustomBaseAttachmentPath(Settings.ZoteroLinkedAttachmentDir);
            Log($"[路径设置] 已指定 Zotero 链接 PDF 根目录: {Settings.ZoteroLinkedAttachmentDir}");
        }
    }

    [RelayCommand]
    public async Task BrowseMarkdownVaultDirAsync()
    {
        if (OpenFolderDialogAsync == null) return;
        var picked = await OpenFolderDialogAsync("选择本地 Markdown 知识库 / Obsidian Vault 目录", Settings.MarkdownVaultPath);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            Settings.MarkdownVaultPath = picked;
            OnPropertyChanged(nameof(Settings));
            Log($"[路径设置] 已指定 Obsidian Vault 目录: {Settings.MarkdownVaultPath}");
        }
    }

    [RelayCommand]
    public async Task AutoDetectZoteroPathsAsync()
    {
        Settings.ZotSeekBaseUrl = "http://localhost:23119/zotseek";
        var detectedSqlite = ZoteroStorageResolver.DetectZoteroSqlitePath();
        if (!string.IsNullOrEmpty(detectedSqlite))
        {
            Settings.ZoteroSqlitePath = detectedSqlite;
            _zotSeekClient.Resolver.SetCustomSqlitePath(detectedSqlite);
        }
        var detectedBase = ZoteroStorageResolver.DetectBaseAttachmentPath();
        if (!string.IsNullOrEmpty(detectedBase))
        {
            Settings.ZoteroLinkedAttachmentDir = detectedBase;
            _zotSeekClient.Resolver.SetCustomBaseAttachmentPath(detectedBase);
        }
        OnPropertyChanged(nameof(Settings));
        await CheckZotSeekStatusAsync();
        await NotifyAsync("自动探测完成",
            $"已自动从本机 Zotero 配置文件 (prefs.js) 探测并更新：\n\n" +
            $"• ZotSeek 服务地址: {Settings.ZotSeekBaseUrl}\n" +
            $"• Zotero 数据库: {Settings.ZoteroSqlitePath}\n" +
            $"• 链接 PDF 根目录: {Settings.ZoteroLinkedAttachmentDir}\n" +
            $"• 已映射 PDF 键数: {_zotSeekClient.Resolver.IndexedPdfCount}");
    }

    [RelayCommand]
    public async Task SaveSettingsCommandAsync()
    {
        TopK = Settings.DefaultSearchTopK;
        MinSimilarity = Settings.DefaultMinSimilarity;
        SelectedGranularity = Settings.DefaultGranularity;
        _zotSeekClient.SetBaseUrl(Settings.ZotSeekBaseUrl);
        _zotSeekClient.Resolver.SetCustomSqlitePath(Settings.ZoteroSqlitePath);
        _zotSeekClient.Resolver.SetCustomBaseAttachmentPath(Settings.ZoteroLinkedAttachmentDir);
        SettingsService.SaveSettings(Settings);
        await CheckZotSeekStatusAsync();
        Log($"[设置已保存] 配置已写入用户文件夹: {SettingsService.UserFolderSettingsPath}");
        await NotifyAsync("保存成功", $"所有设置保存成功！\n配置文件路径:\n{SettingsService.UserFolderSettingsPath}");
    }

    [RelayCommand]
    public void OpenConfigJsonFile()
    {
        SettingsService.SaveSettings(Settings);
        OpenUriOrFileCrossPlatform(SettingsService.UserFolderSettingsPath);
        Log($"[打开配置文件] {SettingsService.UserFolderSettingsPath}");
    }

    [RelayCommand]
    public void OpenMarkdownVaultDirectory()
    {
        var dir = _anydocService.GetActiveVaultDirectory(Settings.MarkdownVaultPath);
        OpenUriOrFileCrossPlatform(dir);
        Log($"[打开目录] 打开本地 Markdown Vault 目录: {dir}");
    }

    [RelayCommand]
    public async Task ExportMarkdownFileAsync()
    {
        if (string.IsNullOrWhiteSpace(LiteratureReviewMarkdown))
        {
            await NotifyAsync("提示", "尚未生成文献综述内容，请先运行工作流！");
            return;
        }
        if (SaveFileDialogAsync == null) return;

        var path = await SaveFileDialogAsync("导出 Obsidian Markdown", $"文献综述与知识图谱_{DateTime.Now:yyyyMMdd_HHmm}.md", "md");
        if (!string.IsNullOrWhiteSpace(path))
        {
            await File.WriteAllTextAsync(path, LiteratureReviewMarkdown, System.Text.Encoding.UTF8);
            Log($"[导出成功] Obsidian 规范 Markdown 已保存至: {path}");
            await NotifyAsync("导出成功", $"Markdown 文件已保存至:\n{path}");
        }
    }

    [RelayCommand]
    public async Task ExportStyledReviewHtmlFileAsync()
    {
        if (string.IsNullOrWhiteSpace(LiteratureReviewMarkdown))
        {
            await NotifyAsync("提示", "尚未生成文献综述内容！");
            return;
        }
        if (SaveFileDialogAsync == null) return;

        var path = await SaveFileDialogAsync("导出样式化文献综述 HTML", $"文献综述精排文档_{SelectedMarkdownTheme}_{DateTime.Now:yyyyMMdd_HHmm}.html", "html");
        if (!string.IsNullOrWhiteSpace(path))
        {
            var targetDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(targetDir)) ExportService.EnsureLocalJsLibraries(targetDir);
            var html = ExportService.GenerateWysiwygMarkdownHtml(LiteratureReviewMarkdown, SelectedMarkdownTheme, editable: false);
            await File.WriteAllTextAsync(path, html, System.Text.Encoding.UTF8);
            Log($"[导出成功] 样式化文献综述 HTML ({SelectedMarkdownTheme}) 已保存至: {path}");
            await NotifyAsync("导出成功", $"样式化综述 HTML 已保存至:\n{path}");
        }
    }

    [RelayCommand]
    public async Task ExportInteractiveHtmlFileAsync()
    {
        if (string.IsNullOrWhiteSpace(HtmlExportContent))
        {
            await NotifyAsync("提示", "尚未生成交互式分析报告，请先运行工作流！");
            return;
        }
        if (SaveFileDialogAsync == null) return;

        var path = await SaveFileDialogAsync("导出交互式知识图谱 HTML 报告", $"学术演进知识图谱_{DateTime.Now:yyyyMMdd_HHmm}.html", "html");
        if (!string.IsNullOrWhiteSpace(path))
        {
            var targetDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(targetDir)) ExportService.EnsureLocalJsLibraries(targetDir);
            await File.WriteAllTextAsync(path, HtmlExportContent, System.Text.Encoding.UTF8);
            Log($"[导出成功] 交互式多媒体 HTML 报告已保存至: {path}");
            OpenUriOrFileCrossPlatform(path);
        }
    }

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Dispatcher.UIThread.Post(() =>
        {
            ExecutionLogs.Add(line);
        });
    }
}
