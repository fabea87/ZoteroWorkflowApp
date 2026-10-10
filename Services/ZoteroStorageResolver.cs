using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public class ZoteroItemMetadata
{
    public string? Doi { get; set; }
    public string? PublicationTitle { get; set; }
    public string? AbstractNote { get; set; }
    public int? Year { get; set; }
    public string? Authors { get; set; }
}

public class ZoteroStorageResolver
{
    private string _zoteroSqlitePath;
    private string _zoteroStorageDir;
    private string _baseAttachmentPath;

    // Cache: ItemKey -> Full PDF file path
    private readonly Dictionary<string, string> _keyToPdfMap = new(StringComparer.OrdinalIgnoreCase);
    // Cache: ParentItemKey -> PDF AttachmentItemKey (for zotero://open-pdf/library/items/{attachKey}?page=N)
    private readonly Dictionary<string, string> _parentToAttachKeyMap = new(StringComparer.OrdinalIgnoreCase);
    // Cache: ItemKey -> Metadata (DOI, PublicationTitle, AbstractNote)
    private readonly Dictionary<string, ZoteroItemMetadata> _keyToMetaMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allLocalDois = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _allLocalPdfs = new();
    private bool _isInitialized;

    public string ZoteroSqlitePath => _zoteroSqlitePath;
    public string BaseAttachmentPath => _baseAttachmentPath;
    public int IndexedPdfCount => _keyToPdfMap.Count;
    public int IndexedDoiCount => _keyToMetaMap.Values.Count(m => !string.IsNullOrEmpty(m.Doi));

    public ZoteroStorageResolver()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _zoteroSqlitePath = DetectZoteroSqlitePath() ?? Path.Combine(userProfile, "Zotero", "zotero.sqlite");
        var dataDir = Path.GetDirectoryName(_zoteroSqlitePath) ?? Path.Combine(userProfile, "Zotero");
        _zoteroStorageDir = Path.Combine(dataDir, "storage");

        _baseAttachmentPath = DetectBaseAttachmentPath()
                              ?? Path.Combine(userProfile, "OneDrive", "备份", "zotero-storage");
    }

    public void SetCustomSqlitePath(string? sqlitePath)
    {
        if (!string.IsNullOrWhiteSpace(sqlitePath))
        {
            var candidate = sqlitePath.Trim();
            // If user selected the Zotero data folder instead of the .sqlite file directly, append zotero.sqlite
            if (Directory.Exists(candidate))
            {
                candidate = Path.Combine(candidate, "zotero.sqlite");
            }
            if (File.Exists(candidate) && !string.Equals(_zoteroSqlitePath, candidate, StringComparison.OrdinalIgnoreCase))
            {
                _zoteroSqlitePath = candidate;
                var dataDir = Path.GetDirectoryName(_zoteroSqlitePath);
                if (!string.IsNullOrEmpty(dataDir))
                {
                    _zoteroStorageDir = Path.Combine(dataDir, "storage");
                }
                _isInitialized = false;
            }
        }
    }

    public void SetCustomBaseAttachmentPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            if (!string.Equals(_baseAttachmentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _baseAttachmentPath = path;
                _isInitialized = false;
            }
        }
    }

    public static string? DetectZoteroSqlitePath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var zoteroProfilesDir = Path.Combine(appData, "Zotero", "Zotero", "Profiles");
            if (Directory.Exists(zoteroProfilesDir))
            {
                foreach (var dir in Directory.GetDirectories(zoteroProfilesDir))
                {
                    var prefs = Path.Combine(dir, "prefs.js");
                    if (File.Exists(prefs))
                    {
                        var content = File.ReadAllText(prefs, Encoding.UTF8);
                        var match = Regex.Match(content, @"user_pref\(""extensions\.zotero\.dataDir"",\s*""(.*?)""\);");
                        if (match.Success)
                        {
                            var rawDir = match.Groups[1].Value.Replace("\\\\", "\\");
                            var candidate = Path.Combine(rawDir, "zotero.sqlite");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
        }
        catch { }

        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Zotero", "zotero.sqlite");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    public static string? DetectBaseAttachmentPath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var zoteroProfilesDir = Path.Combine(appData, "Zotero", "Zotero", "Profiles");
            if (Directory.Exists(zoteroProfilesDir))
            {
                foreach (var dir in Directory.GetDirectories(zoteroProfilesDir))
                {
                    var prefs = Path.Combine(dir, "prefs.js");
                    if (File.Exists(prefs))
                    {
                        var content = File.ReadAllText(prefs, Encoding.UTF8);
                        var match = Regex.Match(content, @"user_pref\(""extensions\.zotero\.baseAttachmentPath"",\s*""(.*?)""\);");
                        if (match.Success)
                        {
                            var rawPath = match.Groups[1].Value.Replace("\\\\", "\\");
                            if (Directory.Exists(rawPath)) return rawPath;
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public void EnsureInitialized(bool forceReload = false)
    {
        if (_isInitialized && !forceReload) return;

        _keyToPdfMap.Clear();
        _parentToAttachKeyMap.Clear();
        _keyToMetaMap.Clear();
        _allLocalDois.Clear();
        _allLocalPdfs.Clear();

        // 1. Index all physical PDFs in baseAttachmentPath for fast fallback matching
        try
        {
            if (Directory.Exists(_baseAttachmentPath))
            {
                _allLocalPdfs = Directory.GetFiles(_baseAttachmentPath, "*.pdf", SearchOption.AllDirectories).ToList();
            }
        }
        catch { }

        // 2. Load from zotero.sqlite snapshot (including WAL if Zotero is running)
        if (File.Exists(_zoteroSqlitePath))
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "ZoteroWorkflowApp_DbCache");
            Directory.CreateDirectory(tempDir);
            var snapshotDb = Path.Combine(tempDir, $"zotero_snap_{ProcessId()}_{Guid.NewGuid():N}.sqlite");
            var snapshotWal = snapshotDb + "-wal";

            try
            {
                CopyFileShared(_zoteroSqlitePath, snapshotDb);
                var walSource = _zoteroSqlitePath + "-wal";
                if (File.Exists(walSource))
                {
                    CopyFileShared(walSource, snapshotWal);
                }

                var connStr = new SqliteConnectionStringBuilder
                {
                    DataSource = snapshotDb,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                }.ToString();

                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();

                    // Query A: Attachments
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            SELECT parent.key AS parentKey, attach.key AS attachKey, itemAttachments.contentType, itemAttachments.path
                            FROM itemAttachments
                            JOIN items attach ON itemAttachments.itemID = attach.itemID
                            LEFT JOIN items parent ON itemAttachments.parentItemID = parent.itemID
                            WHERE itemAttachments.path IS NOT NULL";

                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            var parentKey = reader.IsDBNull(0) ? null : reader.GetString(0);
                            var attachKey = reader.IsDBNull(1) ? null : reader.GetString(1);
                            var contentType = reader.IsDBNull(2) ? null : reader.GetString(2);
                            var rawPath = reader.IsDBNull(3) ? null : reader.GetString(3);

                            if (string.IsNullOrWhiteSpace(rawPath)) continue;

                            var resolved = ResolveRawAttachmentPath(rawPath, attachKey, contentType);
                            if (!string.IsNullOrEmpty(resolved))
                            {
                                if (!string.IsNullOrEmpty(parentKey) && !_keyToPdfMap.ContainsKey(parentKey))
                                {
                                    _keyToPdfMap[parentKey] = resolved;
                                    if (!string.IsNullOrEmpty(attachKey))
                                        _parentToAttachKeyMap[parentKey] = attachKey;
                                }
                                if (!string.IsNullOrEmpty(attachKey) && !_keyToPdfMap.ContainsKey(attachKey))
                                {
                                    _keyToPdfMap[attachKey] = resolved;
                                    _parentToAttachKeyMap[attachKey] = attachKey;
                                }
                            }
                        }
                    }

                    // Query B: DOI, PublicationTitle, AbstractNote, Date
                    using (var cmdMeta = conn.CreateCommand())
                    {
                        cmdMeta.CommandText = @"
                            SELECT items.key, fields.fieldName, itemDataValues.value
                            FROM items
                            JOIN itemData ON items.itemID = itemData.itemID
                            JOIN fields ON itemData.fieldID = fields.fieldID
                            JOIN itemDataValues ON itemData.valueID = itemDataValues.valueID
                            WHERE fields.fieldName IN ('DOI', 'publicationTitle', 'abstractNote', 'date')";

                        using var readerMeta = cmdMeta.ExecuteReader();
                        while (readerMeta.Read())
                        {
                            var key = readerMeta.IsDBNull(0) ? null : readerMeta.GetString(0);
                            var fieldName = readerMeta.IsDBNull(1) ? null : readerMeta.GetString(1);
                            var val = readerMeta.IsDBNull(2) ? null : readerMeta.GetString(2);
                            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(fieldName) || string.IsNullOrEmpty(val)) continue;

                            if (!_keyToMetaMap.TryGetValue(key, out var meta))
                            {
                                meta = new ZoteroItemMetadata();
                                _keyToMetaMap[key] = meta;
                            }

                            if (fieldName == "DOI")
                            {
                                meta.Doi = CleanDoi(val);
                                if (!string.IsNullOrEmpty(meta.Doi)) _allLocalDois.Add(meta.Doi);
                            }
                            else if (fieldName == "publicationTitle") meta.PublicationTitle = val.Trim();
                            else if (fieldName == "abstractNote") meta.AbstractNote = val.Trim();
                            else if (fieldName == "date")
                            {
                                var ym = Regex.Match(val, @"\b(19\d\d|20\d\d)\b");
                                if (ym.Success && int.TryParse(ym.Groups[1].Value, out int yVal))
                                    meta.Year = yVal;
                            }
                        }
                    }

                    // Query C: Creators (Authors)
                    using (var cmdAuth = conn.CreateCommand())
                    {
                        cmdAuth.CommandText = @"
                            SELECT items.key, creators.lastName, creators.firstName
                            FROM items
                            JOIN itemCreators ON items.itemID = itemCreators.itemID
                            JOIN creators ON itemCreators.creatorID = creators.creatorID
                            ORDER BY items.itemID, itemCreators.orderIndex";

                        var authListMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                        using var readerAuth = cmdAuth.ExecuteReader();
                        while (readerAuth.Read())
                        {
                            var key = readerAuth.IsDBNull(0) ? null : readerAuth.GetString(0);
                            var last = readerAuth.IsDBNull(1) ? "" : readerAuth.GetString(1);
                            var first = readerAuth.IsDBNull(2) ? "" : readerAuth.GetString(2);
                            if (string.IsNullOrEmpty(key)) continue;
                            var name = !string.IsNullOrWhiteSpace(last) ? last : first;
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            if (!authListMap.TryGetValue(key, out var list))
                            {
                                list = new List<string>();
                                authListMap[key] = list;
                            }
                            list.Add(name);
                        }

                        foreach (var kv in authListMap)
                        {
                            if (!_keyToMetaMap.TryGetValue(kv.Key, out var meta))
                            {
                                meta = new ZoteroItemMetadata();
                                _keyToMetaMap[kv.Key] = meta;
                            }
                            var names = kv.Value;
                            meta.Authors = names.Count switch
                            {
                                1 => names[0],
                                2 => $"{names[0]} & {names[1]}",
                                > 2 => $"{names[0]} et al.",
                                _ => null
                            };
                        }
                    }
                }
            }
            catch
            {
                // Ignore snapshot errors
            }
            finally
            {
                try { if (File.Exists(snapshotDb)) File.Delete(snapshotDb); } catch { }
                try { if (File.Exists(snapshotWal)) File.Delete(snapshotWal); } catch { }
            }
        }

        _isInitialized = true;
    }

    public static string? CleanDoi(string? rawDoi)
    {
        if (string.IsNullOrWhiteSpace(rawDoi)) return null;
        var m = Regex.Match(rawDoi.Trim(), @"(10\.\d{4,9}/[-._;()/:A-Za-z0-9]+)");
        if (m.Success)
        {
            return m.Groups[1].Value.TrimEnd('.', ',', ';', ')');
        }
        return rawDoi.Trim();
    }

    private string? ResolveRawAttachmentPath(string rawPath, string? attachKey, string? contentType)
    {
        string? candidate = null;

        if (rawPath.StartsWith("attachments:", StringComparison.OrdinalIgnoreCase))
        {
            var sub = rawPath.Substring("attachments:".Length).TrimStart('/', '\\');
            var relPath = sub.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            candidate = Path.Combine(_baseAttachmentPath, relPath);
        }
        else if (rawPath.StartsWith("storage:", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(attachKey))
        {
            var fname = rawPath.Substring("storage:".Length).TrimStart('/', '\\');
            candidate = Path.Combine(_zoteroStorageDir, attachKey, fname);
            if (!File.Exists(candidate))
            {
                var altCandidate = Path.Combine(_baseAttachmentPath, fname);
                if (File.Exists(altCandidate)) candidate = altCandidate;
            }
        }
        else if (Path.IsPathRooted(rawPath))
        {
            candidate = rawPath;
        }
        else
        {
            candidate = Path.Combine(_baseAttachmentPath, rawPath.Replace('/', Path.DirectorySeparatorChar));
        }

        if (string.IsNullOrEmpty(candidate)) return null;

        bool isPdf = (contentType != null && contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                     || candidate.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

        if (!isPdf) return null;

        if (File.Exists(candidate))
        {
            return candidate;
        }

        var targetFileName = Path.GetFileName(candidate);
        if (!string.IsNullOrEmpty(targetFileName) && _allLocalPdfs.Count > 0)
        {
            var match = _allLocalPdfs.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p), targetFileName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        return null;
    }

    public string? ResolvePdfForSearchResult(ZotSeekResultItem item)
    {
        EnsureInitialized();

        // Populate DOI, PublicationTitle, AbstractNote, Authors, Year from SQLite metadata
        if (!string.IsNullOrEmpty(item.ItemKey) && _keyToMetaMap.TryGetValue(item.ItemKey, out var meta))
        {
            item.Doi = meta.Doi;
            item.PublicationTitle = meta.PublicationTitle;
            item.AbstractNote = meta.AbstractNote;
            if (string.IsNullOrWhiteSpace(item.Authors) && !string.IsNullOrWhiteSpace(meta.Authors))
                item.Authors = meta.Authors;
            if (item.Year == null && meta.Year != null)
                item.YearRaw = meta.Year.Value;
            if (item.MatchedChunk == null && !string.IsNullOrWhiteSpace(meta.AbstractNote))
                item.MatchedChunk = new ZotSeekMatchedChunk { Snippet = meta.AbstractNote, Page = 1, TextSource = "abstract" };
        }

        // Populate PdfAttachmentKey from Links.OpenPdf or SQLite map
        if (!string.IsNullOrEmpty(item.Links?.OpenPdf))
        {
            var m = Regex.Match(item.Links.OpenPdf, @"items/([A-Z0-9]{8})", RegexOptions.IgnoreCase);
            if (m.Success) item.PdfAttachmentKey = m.Groups[1].Value;
        }
        if (string.IsNullOrEmpty(item.PdfAttachmentKey) && !string.IsNullOrEmpty(item.ItemKey) &&
            _parentToAttachKeyMap.TryGetValue(item.ItemKey, out var mappedAttachKey))
        {
            item.PdfAttachmentKey = mappedAttachKey;
        }

        // 1. Check by itemKey (usually parent itemKey)
        if (!string.IsNullOrEmpty(item.ItemKey) && _keyToPdfMap.TryGetValue(item.ItemKey, out var pathByParent))
        {
            return pathByParent;
        }

        // 2. Check by attachment key inside Links.OpenPdf
        if (!string.IsNullOrEmpty(item.Links?.OpenPdf))
        {
            var m = Regex.Match(item.Links.OpenPdf, @"items/([A-Z0-9]{8})", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var attachKey = m.Groups[1].Value;
                if (_keyToPdfMap.TryGetValue(attachKey, out var pathByAttach))
                {
                    return pathByAttach;
                }
            }
        }

        // 3. Fallback: match by paper Title after " - YYYY - " in _allLocalPdfs (only for specific titles >= 28 chars)
        if (!string.IsNullOrWhiteSpace(item.Title) && _allLocalPdfs.Count > 0)
        {
            var normTitle = NormalizeForMatch(item.Title);
            if (normTitle.Length >= 28)
            {
                var prefix = normTitle.Substring(0, Math.Min(40, normTitle.Length));
                foreach (var pdf in _allLocalPdfs)
                {
                    var fname = Path.GetFileNameWithoutExtension(pdf);
                    var dashIdx = fname.LastIndexOf(" - ", StringComparison.Ordinal);
                    var titlePart = dashIdx >= 0 ? fname.Substring(dashIdx + 3) : fname;
                    var fnameNorm = NormalizeForMatch(titlePart);
                    if (fnameNorm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return pdf;
                    }
                }
            }
        }

        return null;
    }

    public bool IsDoiInLocalZotero(string? doi)
    {
        EnsureInitialized();
        var clean = CleanDoi(doi);
        if (string.IsNullOrEmpty(clean)) return false;
        return _allLocalDois.Contains(clean);
    }

    private static string NormalizeForMatch(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }

    private static void CopyFileShared(string sourcePath, string destPath)
    {
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        sourceStream.CopyTo(destStream);
    }

    private static int ProcessId() => Environment.ProcessId;
}
