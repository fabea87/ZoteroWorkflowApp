using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using ZoteroWorkflowApp.Models;
using ZoteroWorkflowApp.Services;

namespace ZoteroWorkflowApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--repair-latest"))
        {
            try
            {
                var vault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZoteroWorkflowApp", "MarkdownVault");
                ExportService.EnsureLocalJsLibraries(vault);
                var latestHtml = Directory.GetFiles(vault, "_InteractiveGraph_*.html")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                var latestMd = Directory.GetFiles(vault, "_Synthesis_*.md")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestHtml != null && File.Exists(latestHtml))
                {
                    var oldHtml = File.ReadAllText(latestHtml, Encoding.UTF8);
                    var mGraph = Regex.Match(oldHtml, @"const graphData = (\{.*?\});\r?\n");
                    var mPapers = Regex.Match(oldHtml, @"const papersData = (\[.*?\]);\r?\n");
                    if (mGraph.Success && mPapers.Success)
                    {
                        var graph = JsonSerializer.Deserialize<KnowledgeGraphData>(mGraph.Groups[1].Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                        var papers = JsonSerializer.Deserialize<List<ZotSeekResultItem>>(mPapers.Groups[1].Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                        var md = latestMd != null && File.Exists(latestMd) ? File.ReadAllText(latestMd, Encoding.UTF8) : "";

                        var newHtml = ExportService.GenerateInteractiveHtml("peer feedback", papers, graph, md);
                        File.WriteAllText(latestHtml, newHtml, Encoding.UTF8);
                        Console.WriteLine($"Repaired {latestHtml} -> {newHtml.Length} chars");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Repair error: " + ex);
            }
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }
}
