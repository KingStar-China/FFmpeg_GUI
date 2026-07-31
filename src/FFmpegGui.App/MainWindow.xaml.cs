using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFmpegGui.App.ViewModels;
using Microsoft.Win32;

namespace FFmpegGui.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }
        _viewModel = new MainWindowViewModel(Dispatcher);
        DataContext = _viewModel;
    }

    private async void ImportMain_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateMediaDialog(multiselect: false, "选择主媒体文件");
        if (dialog.ShowDialog(this) == true)
        {
            await LoadFilesAsync(dialog.FileNames, replace: true);
        }
    }

    private async void AddMedia_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateMediaDialog(multiselect: true, "添加媒体文件");
        if (dialog.ShowDialog(this) == true)
        {
            await LoadFilesAsync(dialog.FileNames, replace: false);
        }
    }

    private void ClearMedia_Click(object sender, RoutedEventArgs e) => _viewModel.ClearMedia();

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => _viewModel.ClearCurrentSelection();

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBatchOutput)
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "选择输出文件夹",
                InitialDirectory = ResolveInitialDirectory(_viewModel.OutputPath),
            };
            if (folderDialog.ShowDialog(this) == true)
            {
                _viewModel.SetUserOutputPath(folderDialog.FolderName);
            }

            return;
        }

        var outputPath = _viewModel.OutputPath;
        var dialogFileName = string.IsNullOrWhiteSpace(outputPath) ? string.Empty : Path.GetFileName(outputPath);
        var extension = Path.GetExtension(dialogFileName);
        var saveDialog = new SaveFileDialog
        {
            Title = "另存为",
            FileName = dialogFileName,
            InitialDirectory = ResolveInitialDirectory(outputPath),
            DefaultExt = extension,
            Filter = string.IsNullOrWhiteSpace(extension)
                ? "所有文件 (*.*)|*.*"
                : $"{extension.TrimStart('.').ToUpperInvariant()} 文件 (*{extension})|*{extension}|所有文件 (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (saveDialog.ShowDialog(this) == true)
        {
            _viewModel.SetUserOutputPath(saveDialog.FileName);
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var error = await _viewModel.RunCurrentJobAsync();
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "不能开始执行", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.CancelCurrentTask();

    private void MoveUp_Click(object sender, RoutedEventArgs e) => _viewModel.MoveSelectedTrack(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => _viewModel.MoveSelectedTrack(1);

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (TracksGrid.SelectedItem is TrackItemViewModel track)
        {
            _viewModel.SelectAllLike(track);
        }
    }

    private void TracksGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null and not DataGridRow)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        if (source is DataGridRow row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDroppedFiles(e.Data).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        var files = GetDroppedFiles(e.Data);
        if (files.Count > 0)
        {
            await LoadFilesAsync(files, replace: !_viewModel.HasMedia);
        }
    }

    private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.ScrollToEnd();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => _viewModel.Dispose();

    private async Task LoadFilesAsync(IReadOnlyList<string> paths, bool replace)
    {
        var errors = await _viewModel.LoadFilesAsync(paths, replace);
        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine + Environment.NewLine, errors),
                "部分文件导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public Task LoadInitialFilesAsync(IReadOnlyList<string> paths) => LoadFilesAsync(paths, replace: true);

    private static OpenFileDialog CreateMediaDialog(bool multiselect, string title) => new()
    {
        Title = title,
        Multiselect = multiselect,
        CheckFileExists = true,
        Filter = "媒体文件|*.mkv;*.mk3d;*.mka;*.mks;*.mp4;*.m4v;*.mov;*.avi;*.webm;*.mp3;*.m4a;*.aac;*.flac;*.wav;*.ogg;*.opus;*.ass;*.ssa;*.srt;*.vtt;*.sup;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|所有文件 (*.*)|*.*",
    };

    private static IReadOnlyList<string> GetDroppedFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)
            || data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }

        return paths.Where(File.Exists).ToArray();
    }

    private static string ResolveInitialDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return path;
        }

        var directory = string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);
        return directory is not null && Directory.Exists(directory)
            ? directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }
}
