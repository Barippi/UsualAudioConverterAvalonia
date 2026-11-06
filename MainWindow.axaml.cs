using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace UsualAudioConverter;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> ValidExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".flac", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".wv"
    };

    private static readonly HashSet<string> CompanionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".cue", ".log"
    };

    public ObservableCollection<AudioItem> Items { get; } = new();
    private readonly HashSet<string> _queuedPaths = new(StringComparer.OrdinalIgnoreCase);

    private string _ffmpegPath = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    private string _exhalePath = OperatingSystem.IsWindows() ? "exhale.exe" : "exhale";
    private string _fdkaacPath = OperatingSystem.IsWindows() ? "fdkaac.exe" : "fdkaac";
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();

        GridItems.ItemsSource = Items;
        InitializeControls();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        Loaded += async (s, e) => await RefreshProfilesAsync();
    }

    private void InitializeControls()
    {
        int cores = Environment.ProcessorCount;
        int defaultThreads = cores <= 2 ? 1 : (cores <= 4 ? 2 : Math.Min(8, cores - 2));

        var threadOptions = new HashSet<int> { 1, 2, 4, 6, 8, cores, defaultThreads };
        ComboThreads.ItemsSource = threadOptions.Where(t => t <= cores).OrderBy(t => t).ToList();
        ComboThreads.SelectedItem = defaultThreads;
    }

    private async Task RefreshProfilesAsync()
    {
        bool hasLibFdk = await EncoderService.CheckLibFdkSupportAsync(_ffmpegPath);
        bool hasFdkaac = !string.IsNullOrEmpty(_fdkaacPath) && (File.Exists(_fdkaacPath) || !_fdkaacPath.Contains(Path.DirectorySeparatorChar));
        bool canUseFdk = hasLibFdk || hasFdkaac;

        var allProfiles = EncoderProfile.GetProfiles();
        var availableProfiles = canUseFdk
            ? allProfiles
            : allProfiles.Where(p => !p.RequiresFdk).ToList();

        var currentSelected = ComboProfiles.SelectedItem as EncoderProfile;

        ComboProfiles.ItemsSource = availableProfiles;
        ComboProfiles.DisplayMemberBinding = new Avalonia.Data.Binding("Name");

        if (currentSelected != null && availableProfiles.Any(p => p.Name == currentSelected.Name))
        {
            ComboProfiles.SelectedItem = availableProfiles.First(p => p.Name == currentSelected.Name);
        }
        else
        {
            ComboProfiles.SelectedIndex = 0;
        }
    }

    private void ComboProfiles_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ComboProfiles.SelectedItem is not EncoderProfile selected) return;

        if (selected.QualityOptions != null && selected.QualityOptions.Length > 0)
        {
            ComboBitrate.IsEnabled = true;
            ComboBitrate.ItemsSource = selected.QualityOptions;
            ComboBitrate.SelectedItem = selected.DefaultQuality;
        }
        else
        {
            ComboBitrate.ItemsSource = new[] { "N/A (ロスレス)" };
            ComboBitrate.SelectedIndex = 0;
            ComboBitrate.IsEnabled = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Xfce4/Thunar や X11 からのドラッグ時に事前フィルタで弾かれないよう無条件で許可
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = new List<string>();

        // 1. Avalonia 標準の GetFiles() 取得
        try
        {
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var f in files)
                {
                    string p = f.Path.IsAbsoluteUri ? f.Path.LocalPath : f.Path.ToString();
                    p = Uri.UnescapeDataString(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!string.IsNullOrEmpty(p)) paths.Add(p);
                }
            }
        }
        catch { }

        // 2. Thunar / XDND 固有のフォーマット総当たり走査 (byte[] / string / text/uri-list)
        if (paths.Count == 0)
        {
            var formats = e.Data.GetDataFormats().ToList();
            foreach (var format in formats)
            {
                try
                {
                    var data = e.Data.Get(format);
                    if (data is string textData)
                    {
                        ExtractPathsFromText(textData, paths);
                    }
                    else if (data is byte[] bytes)
                    {
                        // X11 の生の Atom バイト配列から UTF-8 文字列として復元
                        string decoded = Encoding.UTF8.GetString(bytes);
                        ExtractPathsFromText(decoded, paths);
                    }
                    else if (data is IEnumerable<string> stringEnum)
                    {
                        foreach (var s in stringEnum)
                        {
                            ExtractPathsFromText(s, paths);
                        }
                    }
                }
                catch { }

                if (paths.Count > 0) break;
            }
        }

        // 3. 取得したパスを展開して登録
        if (paths.Count > 0)
        {
            AddPaths(paths.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void ExtractPathsFromText(string rawText, List<string> destination)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return;

        // 改行コードおよび Null 文字区切りに対応
        var lines = rawText.Split(new[] { "\r\n", "\n", "\r", "\0" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            string trimmed = line.Trim();

            // Thunar/Nautilus の特殊プレフィックス (copy/cut コマンドヘッダー等) をスキップ
            if (trimmed.StartsWith("copy", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("cut", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("#"))
            {
                continue;
            }

            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                string p = Uri.UnescapeDataString(uri.LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrEmpty(p)) destination.Add(p);
            }
            else if (trimmed.StartsWith("/") && (File.Exists(trimmed) || Directory.Exists(trimmed)))
            {
                destination.Add(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
    }

    private async void BtnAddFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "音声ファイルを選択",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio Files")
                {
                    Patterns = new[] { "*.wav", "*.flac", "*.mp3", "*.m4a", "*.aac", "*.ogg", "*.opus", "*.wma", "*.aiff", "*.wv" }
                },
                FilePickerFileTypes.All
            }
        });

        if (files.Count > 0)
        {
            var paths = files.Select(f => Uri.UnescapeDataString(f.Path.LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            AddPaths(paths);
        }
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var collected = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    if (ValidExtensions.Contains(Path.GetExtension(path)))
                        collected.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    // CIFS/マウントポイント/ACLに耐性のあるスタック走査
                    TraverseDirectorySafe(path, collected);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Path scan error ({path}): {ex.Message}");
            }
        }

        int addedCount = 0;
        foreach (var file in collected)
        {
            if (_queuedPaths.Add(file))
            {
                Items.Add(new AudioItem
                {
                    FileName = Path.GetFileName(file),
                    SourcePath = file
                });
                addedCount++;
            }
        }

        TxtStatus.Text = $"{Items.Count} 件のファイルが登録されています (追加: {addedCount}件)";
    }

    private static void TraverseDirectorySafe(string rootDir, List<string> collected)
    {
        var dirsToVisit = new Stack<string>();
        dirsToVisit.Push(rootDir);

        while (dirsToVisit.Count > 0)
        {
            string currentDir = dirsToVisit.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(currentDir))
                {
                    if (ValidExtensions.Contains(Path.GetExtension(file)))
                    {
                        collected.Add(file);
                    }
                }
            }
            catch { }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    try
                    {
                        var di = new DirectoryInfo(subDir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        dirsToVisit.Push(subDir);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private void BtnClear_Click(object? sender, RoutedEventArgs e)
    {
        if (_cts != null) return;
        Items.Clear();
        _queuedPaths.Clear();
        ProgressTotal.Value = 0;
        TxtStatus.Text = "リストをクリアしました";
    }

    private async void BtnBrowseDir_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "保存先フォルダを選択"
        });

        if (folders.Count > 0)
        {
            string folderPath = Uri.UnescapeDataString(folders[0].Path.LocalPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            TxtCustomDir.Text = folderPath;
        }
    }

    private async void BtnSettings_Click(object? sender, RoutedEventArgs e)
    {
        var exeFilter = OperatingSystem.IsWindows()
            ? new[] { new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } }, FilePickerFileTypes.All }
            : new[] { FilePickerFileTypes.All };

        var ff = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "ffmpeg を指定", FileTypeFilter = exeFilter });
        if (ff.Count > 0)
        {
            _ffmpegPath = Uri.UnescapeDataString(ff[0].Path.LocalPath);
            EncoderService.ResetFdkCache();
        }

        var ex = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "exhale を指定", FileTypeFilter = exeFilter });
        if (ex.Count > 0) _exhalePath = Uri.UnescapeDataString(ex[0].Path.LocalPath);

        var fdk = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "fdkaac を指定 (任意)", FileTypeFilter = exeFilter });
        if (fdk.Count > 0) _fdkaacPath = Uri.UnescapeDataString(fdk[0].Path.LocalPath);

        await RefreshProfilesAsync();
    }

    private static string? CalculateCommonRootFast(IList<AudioItem> items)
    {
        if (items.Count == 0) return null;
        if (items.Count == 1) return Path.GetDirectoryName(items[0].SourcePath);

        string p1 = Path.GetDirectoryName(items[0].SourcePath) ?? string.Empty;
        string p2 = Path.GetDirectoryName(items[^1].SourcePath) ?? string.Empty;

        int len = Math.Min(p1.Length, p2.Length);
        int commonLen = 0;
        for (int i = 0; i < len; i++)
        {
            if (char.ToUpperInvariant(p1[i]) == char.ToUpperInvariant(p2[i]))
                commonLen++;
            else
                break;
        }

        string common = p1[..commonLen];
        int lastSlash = common.LastIndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return lastSlash > 0 ? common[..lastSlash] : common;
    }

    private static void CopyAlbumCompanionFiles(IEnumerable<AudioItem> items)
    {
        var folderPairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            string srcDir = Path.GetDirectoryName(item.SourcePath) ?? string.Empty;
            string destDir = Path.GetDirectoryName(item.DestinationPath) ?? string.Empty;

            if (!string.IsNullOrEmpty(srcDir) && !string.IsNullOrEmpty(destDir))
                folderPairs.TryAdd(srcDir, destDir);
        }

        foreach (var (srcDir, destDir) in folderPairs)
        {
            if (string.Equals(srcDir, destDir, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(srcDir))
                continue;

            try
            {
                Directory.CreateDirectory(destDir);
                foreach (var file in Directory.EnumerateFiles(srcDir))
                {
                    if (CompanionExtensions.Contains(Path.GetExtension(file)))
                    {
                        string targetFile = Path.Combine(destDir, Path.GetFileName(file));
                        if (!File.Exists(targetFile))
                            File.Copy(file, targetFile, overwrite: false);
                    }
                }
            }
            catch { }
        }
    }

    private async void BtnStart_Click(object? sender, RoutedEventArgs e)
    {
        if (Items.Count == 0) return;
        if (ComboProfiles.SelectedItem is not EncoderProfile profile) return;

        object quality = ComboBitrate.SelectedItem ?? profile.DefaultQuality;
        int maxThreads = (int)(ComboThreads.SelectedItem ?? 2);

        bool isCustom = RadioCustomDir.IsChecked == true;
        bool keepStructure = ChkKeepStructure.IsChecked == true;
        string customBase = (TxtCustomDir.Text ?? string.Empty).Trim();

        string? commonRoot = (isCustom && keepStructure) ? CalculateCommonRootFast(Items) : null;

        foreach (var item in Items)
        {
            string outDir;
            if (isCustom)
            {
                string baseDir = string.IsNullOrEmpty(customBase)
                    ? (Path.GetDirectoryName(item.SourcePath) ?? string.Empty)
                    : customBase;

                if (keepStructure && !string.IsNullOrEmpty(commonRoot) &&
                    item.SourcePath.StartsWith(commonRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string rel = Path.GetRelativePath(commonRoot, Path.GetDirectoryName(item.SourcePath) ?? string.Empty);
                    outDir = Path.Combine(baseDir, rel);
                }
                else
                {
                    outDir = baseDir;
                }
            }
            else
            {
                outDir = Path.GetDirectoryName(item.SourcePath) ?? string.Empty;
            }

            string baseName = Path.GetFileNameWithoutExtension(item.SourcePath);
            string targetPath = Path.Combine(outDir, $"{baseName}{profile.Extension}");

            if (string.Equals(item.SourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                targetPath = Path.Combine(outDir, $"{baseName}_converted{profile.Extension}");
            }

            item.DestinationPath = targetPath;
        }

        CopyAlbumCompanionFiles(Items);

        SetUiEncodingState(true);
        _cts = new CancellationTokenSource();
        int completed = 0;

        ProgressTotal.Maximum = Items.Count;
        ProgressTotal.Value = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxThreads,
            CancellationToken = _cts.Token
        };

        try
        {
            await Parallel.ForEachAsync(Items, parallelOptions, async (item, ct) =>
            {
                item.Status = "変換中";
                var progress = new Progress<string>(p => item.Progress = p);

                try
                {
                    await EncoderService.EncodeAsync(
                        item,
                        _ffmpegPath,
                        _exhalePath,
                        _fdkaacPath,
                        profile,
                        quality,
                        progress,
                        ct
                    );
                }
                catch { }
                finally
                {
                    int done = Interlocked.Increment(ref completed);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ProgressTotal.Value = done;
                        TxtStatus.Text = $"進捗: {done}/{Items.Count} 件完了";
                    });
                }
            });

            TxtStatus.Text = $"すべての変換が完了しました ({Items.Count}件)";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "バッチ処理が中断されました。";
        }
        finally
        {
            _cts = null;
            SetUiEncodingState(false);
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        BtnCancel.IsEnabled = false;
        TxtStatus.Text = "中断処理中...";
    }

    private void SetUiEncodingState(bool isEncoding)
    {
        BtnStart.IsEnabled = !isEncoding;
        BtnClear.IsEnabled = !isEncoding;
        BtnAddFiles.IsEnabled = !isEncoding;
        BtnSettings.IsEnabled = !isEncoding;
        ComboProfiles.IsEnabled = !isEncoding;
        ComboBitrate.IsEnabled = !isEncoding;
        ComboThreads.IsEnabled = !isEncoding;
        RadioSameDir.IsEnabled = !isEncoding;
        RadioCustomDir.IsEnabled = !isEncoding;
        TxtCustomDir.IsEnabled = !isEncoding && RadioCustomDir.IsChecked == true;
        BtnBrowseDir.IsEnabled = !isEncoding && RadioCustomDir.IsChecked == true;
        ChkKeepStructure.IsEnabled = !isEncoding && RadioCustomDir.IsChecked == true;
        BtnCancel.IsEnabled = isEncoding;
    }
}