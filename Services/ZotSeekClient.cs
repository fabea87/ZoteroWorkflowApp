using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public class ZotSeekClient
{
    private readonly HttpClient _httpClient;
    private readonly ZoteroStorageResolver _resolver;
    private string _baseUrl = "http://localhost:23119/zotseek";

    public ZoteroStorageResolver Resolver => _resolver;
    public string BaseUrl => _baseUrl;

    public ZotSeekClient()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        _resolver = new ZoteroStorageResolver();
    }

    public void SetBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var clean = url.Trim().TrimEnd('/');
        if (!clean.EndsWith("/zotseek", StringComparison.OrdinalIgnoreCase))
        {
            clean += "/zotseek";
        }
        _baseUrl = clean;
    }

    public async Task<ZotSeekStats?> GetStatsAsync(CancellationToken ct = default)
    {
        try
        {
            var res = await _httpClient.GetStringAsync($"{_baseUrl}/stats", ct);
            return JsonSerializer.Deserialize<ZotSeekStats>(res);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ZotSeekSearchResponse?> SearchAsync(
        string query,
        int topK = 30,
        string mode = "hybrid",
        string granularity = "papers",
        double minSimilarity = 0.25,
        CancellationToken ct = default)
    {
        try
        {
            await Task.Run(() => _resolver.EnsureInitialized(), ct);

            var minSimStr = minSimilarity.ToString("0.00", CultureInfo.InvariantCulture);
            var url = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&topK={topK}&mode={mode}&granularity={granularity}&minSimilarity={minSimStr}";
            var json = await _httpClient.GetStringAsync(url, ct);
            var result = JsonSerializer.Deserialize<ZotSeekSearchResponse>(json);

            if (result?.Results != null)
            {
                foreach (var item in result.Results)
                {
                    item.ResolvedPdfPath = _resolver.ResolvePdfForSearchResult(item);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new Exception($"无法连接到ZotSeek ({_baseUrl}) 或搜索失败: {ex.Message}。请确保Zotero与ZotSeek已启动，且在设置中勾选允许外部通信。", ex);
        }
    }

    public async Task<ZotSeekSearchResponse?> FindSimilarAsync(
        string itemKey,
        int topK = 8,
        CancellationToken ct = default)
    {
        try
        {
            await Task.Run(() => _resolver.EnsureInitialized(), ct);

            var url = $"{_baseUrl}/similar?itemKey={Uri.EscapeDataString(itemKey)}&libraryKey=user&topK={topK}";
            var json = await _httpClient.GetStringAsync(url, ct);
            var result = JsonSerializer.Deserialize<ZotSeekSearchResponse>(json);

            if (result?.Results != null)
            {
                foreach (var item in result.Results)
                {
                    if (item.SemanticScore == null && item.Score != null)
                    {
                        item.SemanticScore = item.Score;
                    }
                    item.ResolvedPdfPath = _resolver.ResolvePdfForSearchResult(item);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new Exception($"ZotSeek 相似文献滚雪球检索失败: {ex.Message}", ex);
        }
    }
}
