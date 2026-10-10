using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ZoteroWorkflowApp.ViewModels;

namespace ZoteroWorkflowApp;

public partial class MainWindow : Window
{
    private string _lastNavigatedGraphPath = "";
    private bool _graphNavHooked;
    private bool _reviewNavHooked;
    private bool _chatNavHooked;
    private CoreWebView2Environment? _webViewEnv;

    private async Task<CoreWebView2Environment> GetWebViewEnvironmentAsync()
    {
        if (_webViewEnv != null) return _webViewEnv;
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZoteroWorkflowApp",
            "WebView2Cache");
        Directory.CreateDirectory(cacheDir);
        _webViewEnv = await CoreWebView2Environment.CreateAsync(null, cacheDir);
        return _webViewEnv;
    }

    public MainWindow()
    {
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        GraphWebView.Loaded += GraphWebView_Loaded;
        ReviewWebView.Loaded += ReviewWebView_Loaded;
        CorpusChatWebView.Loaded += CorpusChatWebView_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private async void GraphWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await UpdateGraphWebViewAsync(vm, forceReload: false);
        }
    }

    private async void ReviewWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await UpdateReviewWebViewAsync(vm);
        }
    }

    private async void CorpusChatWebView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await UpdateCorpusChatWebViewAsync(vm);
        }
    }

    private async void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        if (e.PropertyName == nameof(MainViewModel.HtmlExportFilePath) ||
            e.PropertyName == nameof(MainViewModel.HtmlExportContent))
        {
            await UpdateGraphWebViewAsync(vm, forceReload: true);
        }
        else if (e.PropertyName == nameof(MainViewModel.ReviewWysiwygFilePath) ||
                 e.PropertyName == nameof(MainViewModel.ReviewWysiwygHtml))
        {
            await UpdateReviewWebViewAsync(vm);
        }
        else if (e.PropertyName == nameof(MainViewModel.CorpusChatHtmlFilePath))
        {
            await UpdateCorpusChatWebViewAsync(vm);
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            await Task.Delay(50);
            if (vm.SelectedTabIndex == 1)
                await UpdateGraphWebViewAsync(vm, forceReload: false);
            else if (vm.SelectedTabIndex == 2)
                await UpdateReviewWebViewAsync(vm);
            else if (vm.SelectedTabIndex == 3)
                await UpdateCorpusChatWebViewAsync(vm);
        }
    }

    private void HookZoteroProtocolInterceptor(WebView2 webView, ref bool hookedFlag)
    {
        if (hookedFlag || webView.CoreWebView2 == null) return;
        hookedFlag = true;
        webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (args.Uri != null && args.Uri.StartsWith("zotero://", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = args.Uri,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        };
    }

    private async Task UpdateGraphWebViewAsync(MainViewModel vm, bool forceReload)
    {
        if (!GraphWebView.IsLoaded) return;

        try
        {
            var env = await GetWebViewEnvironmentAsync();
            await GraphWebView.EnsureCoreWebView2Async(env);
            HookZoteroProtocolInterceptor(GraphWebView, ref _graphNavHooked);

            if (!string.IsNullOrWhiteSpace(vm.HtmlExportFilePath) && File.Exists(vm.HtmlExportFilePath))
            {
                if (forceReload || !string.Equals(_lastNavigatedGraphPath, vm.HtmlExportFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastNavigatedGraphPath = vm.HtmlExportFilePath;
                    GraphWebView.CoreWebView2.Navigate(new Uri(vm.HtmlExportFilePath).AbsoluteUri);
                }
            }
            else if (string.IsNullOrEmpty(_lastNavigatedGraphPath))
            {
                GraphWebView.NavigateToString(GetInitialPlaceholderHtml());
            }
        }
        catch { }
    }

    private async Task UpdateReviewWebViewAsync(MainViewModel vm)
    {
        if (!ReviewWebView.IsLoaded) return;

        try
        {
            var env = await GetWebViewEnvironmentAsync();
            await ReviewWebView.EnsureCoreWebView2Async(env);
            HookZoteroProtocolInterceptor(ReviewWebView, ref _reviewNavHooked);

            if (!string.IsNullOrWhiteSpace(vm.ReviewWysiwygFilePath) && File.Exists(vm.ReviewWysiwygFilePath))
            {
                ReviewWebView.CoreWebView2.Navigate(new Uri(vm.ReviewWysiwygFilePath).AbsoluteUri);
            }
            else if (!string.IsNullOrWhiteSpace(vm.ReviewWysiwygHtml))
            {
                ReviewWebView.NavigateToString(vm.ReviewWysiwygHtml);
            }
        }
        catch { }
    }

    private async Task UpdateCorpusChatWebViewAsync(MainViewModel vm)
    {
        if (!CorpusChatWebView.IsLoaded) return;

        try
        {
            var env = await GetWebViewEnvironmentAsync();
            await CorpusChatWebView.EnsureCoreWebView2Async(env);
            HookZoteroProtocolInterceptor(CorpusChatWebView, ref _chatNavHooked);

            if (!string.IsNullOrWhiteSpace(vm.CorpusChatHtmlFilePath) && File.Exists(vm.CorpusChatHtmlFilePath))
            {
                CorpusChatWebView.CoreWebView2.Navigate(new Uri(vm.CorpusChatHtmlFilePath).AbsoluteUri);
            }
        }
        catch { }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.ExecuteSearchCommand.CanExecute(null))
        {
            vm.ExecuteSearchCommand.Execute(null);
        }
    }

    private void CorpusChatBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.AskCorpusQuestionCommandCommand.CanExecute(null))
        {
            vm.AskCorpusQuestionCommandCommand.Execute(null);
        }
    }

    private void CmbReviewViewMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ColRawMarkdown == null || ColWysiwygPreview == null) return;
        if (DataContext is not MainViewModel vm) return;

        var mode = vm.SelectedReviewViewMode;
        if (mode == "Wysiwyg")
        {
            ColRawMarkdown.Width = new GridLength(0);
            BorderRawEditor.Visibility = Visibility.Collapsed;
            ColWysiwygPreview.Width = new GridLength(1, GridUnitType.Star);
            BorderWysiwygPreview.Visibility = Visibility.Visible;
        }
        else if (mode == "Raw")
        {
            ColRawMarkdown.Width = new GridLength(1, GridUnitType.Star);
            BorderRawEditor.Visibility = Visibility.Visible;
            ColWysiwygPreview.Width = new GridLength(0);
            BorderWysiwygPreview.Visibility = Visibility.Collapsed;
        }
        else
        {
            ColRawMarkdown.Width = new GridLength(4, GridUnitType.Star);
            BorderRawEditor.Visibility = Visibility.Visible;
            ColWysiwygPreview.Width = new GridLength(6, GridUnitType.Star);
            BorderWysiwygPreview.Visibility = Visibility.Visible;
        }
    }

    private void CopyReviewText_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.LiteratureReviewMarkdown))
        {
            Clipboard.SetText(vm.LiteratureReviewMarkdown);
            MessageBox.Show("文献综述 Markdown 已复制到剪贴板！", "已复制", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.UpdateSelectionStats();
        }
    }

    private static string GetInitialPlaceholderHtml()
    {
        return """
        <!DOCTYPE html>
        <html>
        <head>
          <meta charset="utf-8">
          <style>
            body {
              background: #F8FAFC;
              color: #334155;
              font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Microsoft YaHei", sans-serif;
              display: flex;
              align-items: center;
              justify-content: center;
              height: 100vh;
              margin: 0;
              text-align: center;
            }
            .box {
              max-width: 560px;
              padding: 36px;
              border: 2px dashed #CBD5E1;
              border-radius: 14px;
              background: #FFFFFF;
              box-shadow: 0 4px 16px rgba(15, 23, 42, 0.05);
            }
            h3 { color: #0284C7; margin-bottom: 12px; font-size: 18px; }
            p { line-height: 1.7; font-size: 14px; color: #475569; }
          </style>
        </head>
        <body>
          <div class="box">
            <h3>🌐 高阶学术知识图谱与学科演进画布</h3>
            <p>请先在 <strong>【1. 语义检索与文献挑选】</strong> 页签中检索并勾选您感兴趣的论文，然后点击 <strong>“🚀 全文解析 (.md) ➔ 构建高阶知识图谱与文献综述”</strong>。</p>
            <p style="margin-top: 12px; font-size: 12.5px; color: #64748B;">生成后将在此呈现：概念力导向/层级图谱、Connected Papers 文献互引网络、历时范式演进矩阵、研究流派聚类及 Obsidian 双链导出。</p>
          </div>
        </body>
        </html>
        """;
    }
}
