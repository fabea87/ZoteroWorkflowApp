using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ZoteroWorkflowApp.Models;

namespace ZoteroWorkflowApp.Services;

public static class SettingsService
{
    // Primary JSON config in the User Profile folder: C:\Users\<Username>\zotero-workflow-settings.json
    public static readonly string UserFolderSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "zotero-workflow-settings.json"
    );

    // Secondary backup / legacy path in AppData
    private static readonly string LegacyAppDataSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZoteroWorkflowApp",
        "settings.json"
    );

    public static LlmSettings LoadSettings()
    {
        LlmSettings? settings = null;

        try
        {
            if (File.Exists(UserFolderSettingsPath))
            {
                var json = File.ReadAllText(UserFolderSettingsPath, Encoding.UTF8);
                settings = JsonSerializer.Deserialize<LlmSettings>(json);
            }
            else if (File.Exists(LegacyAppDataSettingsPath))
            {
                var json = File.ReadAllText(LegacyAppDataSettingsPath, Encoding.UTF8);
                settings = JsonSerializer.Deserialize<LlmSettings>(json);
            }
        }
        catch { }

        settings ??= new LlmSettings();

        if (string.IsNullOrWhiteSpace(settings.PromptKnowledgeGraph) || !settings.PromptKnowledgeGraph.Contains("researchClusters"))
            settings.PromptKnowledgeGraph = LlmSettings.GetDefaultKnowledgeGraphPrompt();
        if (string.IsNullOrWhiteSpace(settings.PromptBibliometricAnalysis))
            settings.PromptBibliometricAnalysis = LlmSettings.GetDefaultBibliometricPrompt();
        if (string.IsNullOrWhiteSpace(settings.PromptLiteratureReview) || !settings.PromptLiteratureReview.Contains("Systematic Synthesis Matrix"))
            settings.PromptLiteratureReview = LlmSettings.GetDefaultLiteratureReviewPrompt();
        if (settings.DefaultSearchTopK <= 0)
            settings.DefaultSearchTopK = 30;
        if (string.IsNullOrWhiteSpace(settings.ZotSeekBaseUrl))
            settings.ZotSeekBaseUrl = "http://localhost:23119/zotseek";
        if (string.IsNullOrWhiteSpace(settings.ZoteroSqlitePath))
            settings.ZoteroSqlitePath = ZoteroStorageResolver.DetectZoteroSqlitePath() ?? "";
        if (string.IsNullOrWhiteSpace(settings.ZoteroLinkedAttachmentDir))
            settings.ZoteroLinkedAttachmentDir = ZoteroStorageResolver.DetectBaseAttachmentPath() ?? "";

        // Ensure the JSON config file is immediately generated/persisted in the User Folder
        SaveSettings(settings);

        return settings;
    }

    public static void SaveSettings(LlmSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserFolderSettingsPath, json, Encoding.UTF8);

            // Also keep AppData backup in sync
            var dir = Path.GetDirectoryName(LegacyAppDataSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(LegacyAppDataSettingsPath, json, Encoding.UTF8);
        }
        catch { }
    }
}
