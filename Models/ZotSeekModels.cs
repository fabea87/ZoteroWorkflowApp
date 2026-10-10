using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ZoteroWorkflowApp.Models;

public class ZotSeekResultItem
{
    [JsonPropertyName("itemKey")]
    public string ItemKey { get; set; } = string.Empty;

    [JsonPropertyName("libraryKey")]
    public string LibraryKey { get; set; } = "user";

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("authors")]
    public string? Authors { get; set; }

    [JsonPropertyName("year")]
    public object? YearRaw { get; set; }

    [JsonIgnore]
    public int? Year
    {
        get
        {
            if (YearRaw is null) return null;
            if (YearRaw is int i) return i;
            if (YearRaw is long l) return (int)l;
            if (int.TryParse(YearRaw.ToString(), out var parsed)) return parsed;
            return null;
        }
    }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("semanticScore")]
    public double? SemanticScore { get; set; }

    [JsonPropertyName("keywordScore")]
    public double? KeywordScore { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("matchedChunk")]
    public ZotSeekMatchedChunk? MatchedChunk { get; set; }

    [JsonPropertyName("links")]
    public ZotSeekLinks? Links { get; set; }

    // Enriched Zotero SQLite Metadata
    public string? Doi { get; set; }
    public string? PublicationTitle { get; set; }
    public string? AbstractNote { get; set; }

    // OpenAlex Bibliometric Metadata
    public int? CitedByCount { get; set; }
    public double? Fwci { get; set; }
    public int? ReferencedWorksCount { get; set; }
    public string? OpenAlexId { get; set; }
    public List<string> OpenAlexTopics { get; set; } = new();
    [JsonIgnore]
    public List<string> ReferencedWorkIds { get; set; } = new();
    public List<string> Institutions { get; set; } = new();

    // App state
    public bool IsSelected { get; set; }
    public string? PdfAttachmentKey { get; set; }
    public string? ResolvedPdfPath { get; set; }
    public string? AnydocMarkdownPath { get; set; }
    [JsonIgnore]
    public string? AnydocMarkdown { get; set; }
    public bool HasPdf => !string.IsNullOrEmpty(ResolvedPdfPath);
    public bool HasDoi => !string.IsNullOrEmpty(Doi);
    public bool HasMarkdown => !string.IsNullOrEmpty(AnydocMarkdownPath);
}

public class ZotSeekMatchedChunk
{
    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }

    [JsonPropertyName("page")]
    public int? Page { get; set; }

    [JsonPropertyName("textSource")]
    public string? TextSource { get; set; }
}

public class ZotSeekLinks
{
    [JsonPropertyName("select")]
    public string? Select { get; set; }

    [JsonPropertyName("selectHttp")]
    public string? SelectHttp { get; set; }

    [JsonPropertyName("openPdf")]
    public string? OpenPdf { get; set; }

    [JsonPropertyName("openPdfHttp")]
    public string? OpenPdfHttp { get; set; }
}

public class ZotSeekSearchResponse
{
    [JsonPropertyName("results")]
    public List<ZotSeekResultItem> Results { get; set; } = new();
}

public class ZotSeekStats
{
    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    [JsonPropertyName("indexedPapers")]
    public int IndexedPapers { get; set; }

    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }
}
