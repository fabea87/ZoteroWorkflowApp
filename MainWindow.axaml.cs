using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using ZoteroWorkflowApp.ViewModels;

namespace ZoteroWorkflowApp;

public partial class MainWindow : Window
{
    private string _lastNavigatedGraphPath = "";

    public MainWindow()
    {
        InitializeComponent();

        ConfigureNativeWebView(GraphWebView);
        ConfigureNativeWebView(ReviewWebView);
        ConfigureNativeWebView(CorpusChatWebView);

        Loaded += MainWindow_Loaded;
    }

    private void ConfigureNativeWebView(NativeWebView webView)
    {
        webView.EnvironmentRequested += (_, args) =>
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZoteroWorkflowApp",
                "WebViewCache");
            Directory.CreateDirectory(cacheDir);

            if (args is WindowsWebView2EnvironmentRequestedEventArgs wv2Args)
            {
                wv2Args.UserDataFolder = cacheDir;
            }
            else if (args is GtkWebViewEnvironmentRequestedEventArgs gtkArgs)
            {
                gtkArgs.ExperimentalOffscreen = true;
                gtkArgs.BaseCacheDirectory = cacheDir;
                gtkArgs.BaseDataDirectory = cacheDir;
            }
        };

        webView.NavigationStarted += (_, args) =>
        {
            var uriStr = args.Request?.AbsoluteUri;
            if (!string.IsNullOrEmpty(uriStr) && uriStr.StartsWith("zotero://", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                MainViewModel.OpenUriOrFileCrossPlatform(uriStr);
            }
        };
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += Vm_PropertyChanged;

            vm.ShowMessageDialogAsync = ShowModalMessageAsync;
            vm.SaveFileDialogAsync = SaveFilePickerDialogAsync;
            vm.OpenFileDialogAsync = OpenFilePickerDialogAsync;
            vm.OpenFolderDialogAsync = OpenFolderPickerDialogAsync;

            UpdateGraphWebView(vm, forceReload: true);
            UpdateReviewWebView(vm);
            UpdateCorpusChatWebView(vm);
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        if (e.PropertyName == nameof(MainViewModel.HtmlExportFilePath) ||
            e.PropertyName == nameof(MainViewModel.HtmlExportContent))
        {
            UpdateGraphWebView(vm, forceReload: true);
        }
        else if (e.PropertyName == nameof(MainViewModel.ReviewWysiwygFilePath) ||
                 e.PropertyName == nameof(MainViewModel.ReviewWysiwygHtml))
        {
            UpdateReviewWebView(vm);
        }
        else if (e.PropertyName == nameof(MainViewModel.CorpusChatHtmlFilePath))
        {
            UpdateCorpusChatWebView(vm);
        }
        else if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex))
        {
            if (vm.SelectedTabIndex == 1)
                UpdateGraphWebView(vm, forceReload: false);
            else if (vm.SelectedTabIndex == 2)
                UpdateReviewWebView(vm);
            else if (vm.SelectedTabIndex == 3)
                UpdateCorpusChatWebView(vm);
        }
    }

    private void UpdateGraphWebView(MainViewModel vm, bool forceReload)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(vm.HtmlExportFilePath) && File.Exists(vm.HtmlExportFilePath))
            {
                if (forceReload || !string.Equals(_lastNavigatedGraphPath, vm.HtmlExportFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastNavigatedGraphPath = vm.HtmlExportFilePath;
                    GraphWebView.Source = new Uri(vm.HtmlExportFilePath);
                }
            }
            else if (string.IsNullOrEmpty(_lastNavigatedGraphPath))
            {
                GraphWebView.NavigateToString(GetInitialPlaceholderHtml());
            }
        }
        catch { }
    }

    private void UpdateReviewWebView(MainViewModel vm)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(vm.ReviewWysiwygFilePath) && File.Exists(vm.ReviewWysiwygFilePath))
            {
                ReviewWebView.Source = new Uri(vm.ReviewWysiwygFilePath);
            }
            else if (!string.IsNullOrWhiteSpace(vm.ReviewWysiwygHtml))
            {
                ReviewWebView.NavigateToString(vm.ReviewWysiwygHtml);
            }
        }
        catch { }
    }

    private void UpdateCorpusChatWebView(MainViewModel vm)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(vm.CorpusChatHtmlFilePath) && File.Exists(vm.CorpusChatHtmlFilePath))
            {
                CorpusChatWebView.Source = new Uri(vm.CorpusChatHtmlFilePath);
            }
        }
        catch { }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.ExecuteSearchCommand.CanExecute(null))
        {
            vm.ExecuteSearchCommand.Execute(null);
        }
    }

    private void CorpusChatBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.AskCorpusQuestionCommandCommand.CanExecute(null))
        {
            vm.AskCorpusQuestionCommandCommand.Execute(null);
        }
    }

    private void CmbReviewViewMode_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReviewSplitGrid == null || BorderRawEditor == null || BorderWysiwygPreview == null) return;
        if (DataContext is not MainViewModel vm) return;

        var mode = vm.SelectedReviewViewMode;
        if (mode == "Wysiwyg")
        {
            ReviewSplitGrid.ColumnDefinitions[0].Width = new GridLength(0);
            BorderRawEditor.IsVisible = false;
            ReviewSplitGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            BorderWysiwygPreview.IsVisible = true;
        }
        else if (mode == "Raw")
        {
            ReviewSplitGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            BorderRawEditor.IsVisible = true;
            ReviewSplitGrid.ColumnDefinitions[1].Width = new GridLength(0);
            BorderWysiwygPreview.IsVisible = false;
        }
        else
        {
            ReviewSplitGrid.ColumnDefinitions[0].Width = new GridLength(4, GridUnitType.Star);
            BorderRawEditor.IsVisible = true;
            ReviewSplitGrid.ColumnDefinitions[1].Width = new GridLength(6, GridUnitType.Star);
            BorderWysiwygPreview.IsVisible = true;
        }
    }

    private async void CopyReviewText_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.LiteratureReviewMarkdown))
        {
            if (Clipboard != null)
            {
                await Clipboard.SetTextAsync(vm.LiteratureReviewMarkdown);
                await ShowModalMessageAsync("已复制", "文献综述 Markdown 已复制到剪贴板！");
            }
        }
    }

    private void CheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.UpdateSelectionStats();
        }
    }

    private async Task ShowModalMessageAsync(string title, string message)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = SolidColorBrush.Parse("#FFFFFF")
        };

        var closeBtn = new Button
        {
            Content = "确定 (OK)",
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = SolidColorBrush.Parse("#0284C7"),
            Foreground = SolidColorBrush.Parse("#FFFFFF"),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(20, 7),
            CornerRadius = new CornerRadius(6)
        };
        closeBtn.Click += (_, _) => dlg.Close();

        var panel = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 14
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            Foreground = SolidColorBrush.Parse("#0369A1")
        });
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 340,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13.5,
                Foreground = SolidColorBrush.Parse("#1E293B")
            }
        });
        panel.Children.Add(closeBtn);

        dlg.Content = panel;
        await dlg.ShowDialog(this);
    }

    private async Task<string?> SaveFilePickerDialogAsync(string title, string suggestedFileName, string defaultExtension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension
        });
        return file?.TryGetLocalPath();
    }

    private async Task<string?> OpenFilePickerDialogAsync(string title, string? initialDir)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> OpenFolderPickerDialogAsync(string title, string? initialDir)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
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
            <p style="margin-top: 12px; font-size: 12.5px; color: #64748B;">生成后将在此呈现：概念力导向/层级图谱、共现词与主题建模、Connected Papers 文献互引网络、历时范式演进矩阵、研究流派聚类及 Obsidian 双链导出。</p>
          </div>
        </body>
        </html>
        """;
    }
}
