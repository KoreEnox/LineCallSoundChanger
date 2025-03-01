using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LineCallSoundChanger
{
    public partial class MainWindow : Window
    {
        // 定数
        private static class Constants
        {
            public const string TargetFilePath = @"AppData\Local\LineCall\Data\sound\VoipRing.wav";
            public const string RingtonesFolder = @"LineCallSoundChanger\Ringtones";
            public const string SettingsFolder = @"LineCallSoundChanger\Settings";
            public const string SelectedCacheFile = "selectedRingtone.txt";
            public const string ModeCacheFile = "mode.txt";
            public const string MuteResourceName = "mute.wav";
            public static class Modes
            {
                public const string Normal = "Normal";
                public const string Random = "Random";
                public const string Mute = "Mute";
            }
        }

        // フィールド
        private readonly string _targetFilePath;
        private readonly string _ringtonesFolder;
        private readonly string _settingsFolder;
        private readonly DispatcherTimer _randomTimer;
        private readonly Random _random = new Random();
        private readonly CacheManager _cacheManager;
        private readonly FileSystemWatcher _fileWatcher;

        public MainWindow()
        {
            InitializeComponent();

            // パスの初期化
            _targetFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.TargetFilePath);
            _ringtonesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Constants.RingtonesFolder);
            _settingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Constants.SettingsFolder);
            _cacheManager = new CacheManager(_settingsFolder);

            EnsureDirectoriesExist();
            LoadRingtones();

            // タイマーの初期化
            _randomTimer = new DispatcherTimer { Interval = GetInterval() };
            _randomTimer.Tick += RandomTimer_Tick;

            // ファイル監視の初期化
            _fileWatcher = new FileSystemWatcher(_ringtonesFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                Filter = "*.wav",
                EnableRaisingEvents = true
            };
            _fileWatcher.Created += FileWatcher_Changed;
            _fileWatcher.Deleted += FileWatcher_Changed;
            _fileWatcher.Renamed += FileWatcher_Changed;

            // ウィンドウがアクティブになった時のイベントを登録
            this.Activated += MainWindow_Activated;

            InitializeModeFromCache();
        }

        // ウィンドウがアクティブになった時の処理
        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            // ウィンドウがアクティブになったら、リストを更新
            CheckAndUpdateRingtonesList();
        }

        // ファイル監視イベントハンドラ
        private void FileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            // UI スレッドでリストを更新
            this.Dispatcher.Invoke(() =>
            {
                LoadRingtones();

                // 選択中のファイルが削除された場合の対応
                string? selectedFileName = _cacheManager.ReadCache(Constants.SelectedCacheFile)?.Trim();
                if (selectedFileName != null && !string.IsNullOrWhiteSpace(selectedFileName))
                {
                    string selectedFilePath = Path.Combine(_ringtonesFolder, selectedFileName);
                    if (!File.Exists(selectedFilePath) && e.ChangeType == WatcherChangeTypes.Deleted)
                    {
                        // 削除されたファイルが現在選択中だった場合、自動で別のファイルを選択
                        RestoreSelectedRingtone();
                    }
                }
            });
        }

        private void CheckAndUpdateRingtonesList()
        {
            if (listViewRingtones.ItemsSource is IEnumerable<RingtoneItem> items)
            {
                bool needsUpdate = false;

                // リスト内の各アイテムが実際に存在するか確認
                foreach (var item in items)
                {
                    if (!File.Exists(item.FullPath))
                    {
                        needsUpdate = true;
                        break;
                    }
                }

                // フォルダ内の実ファイル数とリスト数を比較
                int actualFileCount = Directory.GetFiles(_ringtonesFolder, "*.wav").Length;
                if (actualFileCount != items.Count())
                {
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    LoadRingtones();

                    // 選択中のファイルが存在しない場合、別のファイルを選択
                    string? selectedFileName = _cacheManager.ReadCache(Constants.SelectedCacheFile)?.Trim();
                    if (selectedFileName != null && !string.IsNullOrWhiteSpace(selectedFileName))
                    {
                        string selectedFilePath = Path.Combine(_ringtonesFolder, selectedFileName);
                        if (!File.Exists(selectedFilePath))
                        {
                            RestoreSelectedRingtone();
                        }
                    }
                }
            }
        }

        // ディレクトリ確認
        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(_ringtonesFolder);
            Directory.CreateDirectory(_settingsFolder);
        }

        // モードの初期化
        private void InitializeModeFromCache()
        {
            string mode = _cacheManager.ReadCache(Constants.ModeCacheFile)?.Trim();
            string? selectedFileName = _cacheManager.ReadCache(Constants.SelectedCacheFile)?.Trim();

            // 選択されたファイルが存在するか確認
            if (selectedFileName != null && !string.IsNullOrWhiteSpace(selectedFileName))
            {
                string selectedFilePath = Path.Combine(_ringtonesFolder, selectedFileName);
                if (!File.Exists(selectedFilePath))
                {
                    // 選択されたファイルが見つからない場合、キャッシュをクリア
                    _cacheManager.WriteCache(Constants.SelectedCacheFile, string.Empty);
                    selectedFileName = null;
                }
            }

            switch (mode)
            {
                case Constants.Modes.Random:
                    StartRandomMode();
                    break;
                case Constants.Modes.Mute:
                    ApplyMuteMode();
                    break;
                default:
                    UpdateSelectionState(selectedFileName);
                    break;
            }
        }

        // キャッシュ管理クラス
        private class CacheManager
        {
            private readonly string _settingsFolder;

            public CacheManager(string settingsFolder)
            {
                _settingsFolder = settingsFolder;
            }

            public void WriteCache(string fileName, string content)
            {
                File.WriteAllText(Path.Combine(_settingsFolder, fileName), content);
            }

            public string? ReadCache(string fileName)
            {
                string path = Path.Combine(_settingsFolder, fileName);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
        }

        // 着信音の読み込み
        private void LoadRingtones()
        {
            var files = Directory.GetFiles(_ringtonesFolder, "*.wav");
            string? selectedFileName = _cacheManager.ReadCache(Constants.SelectedCacheFile)?.Trim();

            // 選択されたファイルが存在するか確認
            if (selectedFileName != null)
            {
                string selectedFilePath = Path.Combine(_ringtonesFolder, selectedFileName);
                if (!File.Exists(selectedFilePath))
                {
                    // 選択されたファイルが見つからない場合、キャッシュをクリア
                    _cacheManager.WriteCache(Constants.SelectedCacheFile, string.Empty);
                    selectedFileName = null;
                }
            }

            var items = files.Select(file => new RingtoneItem
            {
                FileName = Path.GetFileName(file) ?? string.Empty,
                FullPath = file,
                IsSelected = selectedFileName != null && file.EndsWith(selectedFileName)
            }).ToList();

            listViewRingtones.ItemsSource = items;
        }




        // 更新ボタン
        private void ButtonUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadRingtones();
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("権限が不足しています。管理者権限で実行してください。");
            }
            catch (IOException ex)
            {
                ShowError($"ディレクトリアクセス中にエラーが発生しました: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowError($"予期しないエラーが発生しました: {ex.Message}");
            }
        }

        // ミュート音選択
        private void OpenMuteSoundSelectFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "ミュート音を選択してください",
                Filter = "WAVファイル (*.wav)|*.wav"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string sourceFile = openFileDialog.FileName;
                string destFilePath = Path.Combine(_settingsFolder, Constants.MuteResourceName);

                try
                {
                    File.Copy(sourceFile, destFilePath, true);
                    MessageBox.Show("ミュート音を設定しました。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError($"ミュート音の設定中にエラーが発生しました: {ex.Message}");
                }
            }
        }

        // 着信音アイテムクラス
        public class RingtoneItem : INotifyPropertyChanged
        {
            private string _fileName = string.Empty;
            public string FileName
            {
                get => _fileName;
                set
                {
                    if (_fileName != value)
                    {
                        _fileName = value ?? string.Empty;
                        OnPropertyChanged(nameof(FileName));
                    }
                }
            }

            private string _fullPath = string.Empty;
            public string FullPath
            {
                get => _fullPath;
                set
                {
                    if (_fullPath != value)
                    {
                        _fullPath = value ?? string.Empty;
                        OnPropertyChanged(nameof(FullPath));
                    }
                }
            }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected != value)
                    {
                        _isSelected = value;
                        OnPropertyChanged(nameof(IsSelected));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 着信音追加
        private void ButtonAddRingtone_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "追加する着信音ファイルを選択してください",
                Filter = "WAVファイル (*.wav)|*.wav"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string sourceFile = openFileDialog.FileName;
                string destFileName = Path.GetFileName(sourceFile) ?? "default.wav";
                string destFilePath = Path.Combine(_ringtonesFolder, destFileName);
                int count = 1;

                while (File.Exists(destFilePath))
                {
                    destFileName = $"{Path.GetFileNameWithoutExtension(sourceFile)}_{count}{Path.GetExtension(sourceFile)}";
                    destFilePath = Path.Combine(_ringtonesFolder, destFileName);
                    count++;
                }

                try
                {
                    File.Copy(sourceFile, destFilePath);
                    LoadRingtones();
                    MessageBox.Show("着信音を追加しました。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    ShowError($"着信音追加中にエラーが発生しました: {ex.Message}");
                }
            }
        }

        // 着信音選択
        private void SelectRingtone_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                if (!File.Exists(filePath))
                {
                    ShowError("ファイルが見つかりません。一覧を更新します。");
                    LoadRingtones(); // リロードを行う
                    return;
                }
                ApplyRingtone(filePath);
            }
        }

        // 着信音の適用
        private void ApplyRingtone(string filePath, bool updateCache = true)
        {
            try
            {
                var targetFile = new FileInfo(_targetFilePath);
                targetFile.Directory?.Create();
                if (targetFile.Exists && targetFile.IsReadOnly) targetFile.IsReadOnly = false;

                File.Copy(filePath, _targetFilePath, true);
                targetFile.IsReadOnly = true;

                if (updateCache)
                {
                    string fileName = Path.GetFileName(filePath) ?? string.Empty;
                    _cacheManager.WriteCache(Constants.SelectedCacheFile, fileName);
                    UpdateSelectionState(fileName);
                }
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("権限が不足しています。管理者権限で実行してください。");
            }
            catch (IOException ex)
            {
                ShowError($"ファイル操作中にエラーが発生しました: {ex.Message}");
            }
            catch (Exception ex)
            {
                ShowError($"予期しないエラーが発生しました: {ex.Message}");
            }
        }

        // ディレクトリを開く
        private void OpenSoundDirectory_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(_ringtonesFolder))
            {
                Process.Start("explorer.exe", _ringtonesFolder);
            }
            else
            {
                ShowError("指定したディレクトリが存在しません。");
            }
        }

        // 選択状態の更新
        private void UpdateSelectionState(string? selectedFileName)
        {
            if (listViewRingtones.ItemsSource is IEnumerable<RingtoneItem> items)
            {
                foreach (var item in items)
                {
                    item.IsSelected = item.FileName == selectedFileName;
                }
            }
        }

        // ランダムモード開始
        private void StartRandomMode()
        {
            if (!ValidateRingtonesExist()) return;

            checkBoxRandomMode.IsChecked = true;
            checkBoxMuteMode.IsChecked = false;
            listViewRingtones.IsEnabled = false;
            _randomTimer.Interval = GetInterval();
            _randomTimer.Start();
            textBlockCurrentRandom.Text = "(なし)";
            _cacheManager.WriteCache(Constants.ModeCacheFile, Constants.Modes.Random);
        }

        // ミュートモード適用
        private void ApplyMuteMode()
        {
            string mutePath = Path.Combine(_settingsFolder, Constants.MuteResourceName);
            if (!File.Exists(mutePath))
            {
                ShowError("ミュート音が登録されていません。ミュートを利用するには、ミュート音を登録してください。");
                checkBoxMuteMode.IsChecked = false;
                return;
            }

            checkBoxRandomMode.IsChecked = false;
            checkBoxMuteMode.IsChecked = true;
            listViewRingtones.IsEnabled = false;
            ApplyRingtone(mutePath, false);
            _cacheManager.WriteCache(Constants.ModeCacheFile, Constants.Modes.Mute);
        }

        // ランダムモードチェック
        private void checkBoxRandomMode_Checked(object sender, RoutedEventArgs e) => StartRandomMode();

        private void checkBoxRandomMode_Unchecked(object sender, RoutedEventArgs e)
        {
            _randomTimer.Stop();
            listViewRingtones.IsEnabled = true;
            textBlockCurrentRandom.Text = "(なし)";
            RestoreSelectedRingtone();
            _cacheManager.WriteCache(Constants.ModeCacheFile, Constants.Modes.Normal);
        }

        // ミュートモードチェック
        private void checkBoxMuteMode_Checked(object sender, RoutedEventArgs e) => ApplyMuteMode();

        private void checkBoxMuteMode_Unchecked(object sender, RoutedEventArgs e)
        {
            listViewRingtones.IsEnabled = true;

            // 通常モードに設定
            _cacheManager.WriteCache(Constants.ModeCacheFile, Constants.Modes.Normal);

            // 選択中のファイルを確認・復元
            string? selectedFileName = _cacheManager.ReadCache(Constants.SelectedCacheFile);
            if (!string.IsNullOrWhiteSpace(selectedFileName))
            {
                // 選択されたファイルが存在するか確認
                string selectedFilePath = Path.Combine(_ringtonesFolder, selectedFileName);
                if (File.Exists(selectedFilePath))
                {
                    // 既存の選択ファイルを適用
                    ApplyRingtone(selectedFilePath, false);
                    return;
                }
            }

            // ここに到達した場合は選択ファイルが存在しない
            // キャッシュをクリア
            _cacheManager.WriteCache(Constants.SelectedCacheFile, string.Empty);

            // フォルダ内のファイルを取得
            var files = Directory.GetFiles(_ringtonesFolder, "*.wav");

            if (files.Length > 0)
            {
                // フォルダ内の最初のファイルを選択
                string firstFile = files[0];
                string fileName = Path.GetFileName(firstFile);

                // 新しいファイルを適用して保存
                ApplyRingtone(firstFile, true);

                MessageBox.Show($"選択されていた効果音が見つからなかったため、「{fileName}」を選択しました。",
                    "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // フォルダ内にファイルが存在しない場合
                ShowError("着信音が登録されていません。効果音を追加してください。");
                UpdateSelectionState(null);
            }
        }

        // インターバルの取得
        private TimeSpan GetInterval()
        {
            if (!int.TryParse(textBoxInterval.Text, out int seconds) || seconds <= 0)
            {
                seconds = 60;
                textBoxInterval.Text = "60";
            }
            return TimeSpan.FromSeconds(seconds);
        }

        // ランダムタイマーイベント
        private void RandomTimer_Tick(object? sender, EventArgs e)
        {
            if (listViewRingtones.ItemsSource is IEnumerable<RingtoneItem> items && items.Any())
            {
                var itemList = items.ToList();
                int index = _random.Next(itemList.Count);
                var selectedItem = itemList[index];
                ApplyRingtone(selectedItem.FullPath, false);
                textBlockCurrentRandom.Text = selectedItem.FileName;
            }
        }

        // テスト再生
        private void TestPlayback_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        new SoundPlayer(filePath).Play();
                    }
                    else
                    {
                        ShowError("ファイルが見つかりません。一覧を更新します。");
                        LoadRingtones(); // リロードを行う
                    }
                }
                catch (Exception ex)
                {
                    ShowError($"テスト再生中にエラーが発生しました: {ex.Message}");
                }
            }
        }

        // 右クリック処理
        private void ListViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item)
            {
                item.IsSelected = true; // 右クリックした項目を選択
                e.Handled = true;
                Console.WriteLine("Right-click detected on ListViewItem");
            }
        }

        // ヘルパーメソッド
        private bool ValidateRingtonesExist()
        {
            if (listViewRingtones.Items.Count == 0)
            {
                ShowError("着信音が登録されていません。最低1つ登録してください。");
                checkBoxRandomMode.IsChecked = false;
                return false;
            }
            return true;
        }

        private void RestoreSelectedRingtone()
        {
            string? selectedFileName = _cacheManager.ReadCache(Constants.SelectedCacheFile);
            if (!string.IsNullOrWhiteSpace(selectedFileName))
            {
                string selectedFilePath = Path.Combine(_ringtonesFolder, selectedFileName);
                if (File.Exists(selectedFilePath))
                {
                    ApplyRingtone(selectedFilePath, false);
                    return;
                }
            }

            // ファイルが存在しない場合、キャッシュをクリア
            _cacheManager.WriteCache(Constants.SelectedCacheFile, string.Empty);

            // フォルダ内のファイルを取得
            var files = Directory.GetFiles(_ringtonesFolder, "*.wav");

            if (files.Length > 0)
            {
                // フォルダ内の最初のファイルを選択
                string firstFile = files[0];
                string fileName = Path.GetFileName(firstFile);

                // 新しいファイルを適用して保存
                ApplyRingtone(firstFile, true);

                MessageBox.Show($"選択されていた効果音が見つからなかったため、「{fileName}」を選択しました。",
                    "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // フォルダ内にファイルが存在しない場合
                ShowError("着信音が登録されていません。効果音を追加してください。");
                UpdateSelectionState(null);
            }
        }

        // ウィンドウ終了時にファイル監視を停止
        protected override void OnClosed(EventArgs e)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            base.OnClosed(e);
        }

        private void ShowError(string message) =>
            MessageBox.Show(message, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}