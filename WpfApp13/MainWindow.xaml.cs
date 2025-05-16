using FluentFTP;
using Microsoft.Win32;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WpfApp13
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AsyncFtpClient _ftpClient;         // Клиент для работы с FTP-сервером
        private bool _isConnected = false;         // Флаг подключения к серверу
        private string _currentPath = "/";         // Текущий путь на сервере
        private Stack<string> _pathHistory = new Stack<string>();  // История посещенных путей для навигации назад
        public MainWindow()
        {
            InitializeComponent();
        }
        // Метод для добавления сообщений в лог
        private void Log(string message)
        {
            // Используем Dispatcher для безопасного доступа к UI из другого потока
            Dispatcher.Invoke(() =>
            {
                txtLog.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\n");
                txtLog.ScrollToEnd();  // Автоматическая прокрутка к новому сообщению
            });
        }
        // Обработчик нажатия кнопки подключения/отключения
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            // Если уже подключены - отключаемся
            if (_isConnected)
            {
                Disconnect();
                return;
            }
            try
            {
                btnConnect.IsEnabled = false;
                statusBar.Content = "Соединение...";
                // Получаем параметры подключения из UI
                var server = "127.0.0.1";
                var port = 21;
                var username = txtUsername.Text;
                var password = txtPassword.Text;
                // Создаем и подключаем FTP-клиента
                _ftpClient = new AsyncFtpClient(server, username, password, port);
                await _ftpClient.Connect();
                _isConnected = true;
                statusBar.Content = "Клиент соединен";
                Log("Клиент соединен с FTP-сервером");
                // Загружаем содержимое корневой директории
                await LoadDirectory("/");
            }
            catch (Exception ex)
            {
                Log($"Ошибка соединения: {ex.Message}");
                Disconnect();
            }
            finally
            {
                btnConnect.IsEnabled = true;
            }
        }
        // Загрузка содержимого директории
        private async Task LoadDirectory(string path)
        {
            if (!_isConnected) return;
            try
            {
                mainList.Items.Clear();
                _currentPath = path;
                statusBar.Content = $"Текущий путь: {_currentPath}";
                // Получаем список элементов в текущей директории
                var items = await _ftpClient.GetListing(_currentPath);
                // Добавляем кнопку для перехода на уровень выше (кроме корня)
                if (_currentPath != "/")
                {
                    mainList.Items.Add(new
                    {
                        DisplayText = "...",
                        OriginalItem = (FtpListItem)null, // Маркер для родительской директории
                        IsDirectory = true
                    });
                }
                // Добавляем каждый элемент в список
                foreach (var item in items)
                {
                    string displayText;
                    bool isDirectory = item.Type == FtpObjectType.Directory;

                    if (isDirectory)
                    {
                        displayText = $"Папка\n\rНазвание: {item.Name}";
                    }
                    else
                    {
                        displayText = $"Файл\n\rРазмер: {FormatSize(item.Size)}\n\rНазвание: {item.Name}";
                    }
                    mainList.Items.Add(new
                    {
                        DisplayText = displayText,
                        OriginalItem = item,
                        IsDirectory = isDirectory
                    });
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка загрузки директорий: {ex.Message}");
            }
        }
        // Форматирование размера файла в читаемый вид
        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes /= 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }
        // Отключение от FTP-сервера
        private void Disconnect()
        {
            if (_ftpClient != null)
            {
                _ftpClient.Disconnect();
                mainList.Items.Clear();
                _ftpClient = null;
            }
            _isConnected = false;
            _currentPath = "/";
            _pathHistory.Clear();
            Log("Клиент отсоединен от FTP-сервера");
            statusBar.Content = "Клиент отсоединен от FTP-сервера";
        }
        // Обработчик кнопки анализа имен директорий
        private async void BtnList_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected)
            {
                Log("Нет соединений с FTP-сервером");
                return;
            }
            try
            {
                BtnList.IsEnabled = false;
                var items = await _ftpClient.GetListing("/");
                AnalyzeDirectoryNames(items);
            }
            catch (Exception ex)
            {
                Log($"Список ошибок: {ex.Message}");
            }
            finally
            {
                BtnList.IsEnabled = true;
            }
        }
        // Анализ имен директорий (поиск самого длинного и короткого имен)
        private void AnalyzeDirectoryNames(FtpListItem[] items)
        {
            var directories = items.Where(i => i.Type == FtpObjectType.Directory).ToList();
            if (directories.Any())
            {
                var maxDir = directories.OrderByDescending(d => d.Name.Length).First();
                var minDir = directories.OrderBy(d => d.Name.Length).First();
                Log($"Самое длинное название директории: ''{maxDir.Name}'' ({maxDir.Name.Length} символов)");
                Log($"Самое короткое название директории: ''{minDir.Name}'' ({minDir.Name.Length} символов)");
            }
        }
        // Обработчик двойного клика по элементу списка
        private async void mainList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (mainList.SelectedItem == null) return;
            dynamic selectedItem = mainList.SelectedItem;
            FtpListItem ftpItem = selectedItem.OriginalItem;
            bool isDirectory = selectedItem.IsDirectory;
            try
            {
                // Если это ссылка на родительскую директорию
                if (ftpItem == null && isDirectory)
                {
                    await NavigateUp();
                }
                else if (isDirectory)
                {
                    await NavigateIntoDirectory(ftpItem.Name);
                }
                else
                {
                    await DownloadFile(ftpItem.Name);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка: {ex.Message}");
            }
        }
        // Навигация вверх по директориям
        private async Task NavigateUp()
        {
            if (_currentPath == "/") return;
            string parentPath = _currentPath.TrimEnd('/');
            int lastSlash = parentPath.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                parentPath = "/";
            }
            else
            {
                parentPath = parentPath.Substring(0, lastSlash);
                if (string.IsNullOrEmpty(parentPath)) parentPath = "/";
            }
            _pathHistory.Push(_currentPath);
            await LoadDirectory(parentPath);
        }
        // Навигация во вложенную директорию
        private async Task NavigateIntoDirectory(string dirName)
        {
            string newPath = _currentPath == "/"
                ? $"/{dirName}"
                : $"{_currentPath}/{dirName}";
            _pathHistory.Push(_currentPath);
            await LoadDirectory(newPath);
        }
        // Скачивание файла с сервера
        private async Task DownloadFile(string fileName)
        {
            string remotePath = _currentPath == "/"
                ? $"/{fileName}"
                : $"{_currentPath}/{fileName}";
            // Диалог выбора места сохранения файла
            var saveFileDialog = new SaveFileDialog
            {
                FileName = fileName,
                Filter = "All files (*.*)|*.*"
            };
            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    Log($"Скачивание {remotePath}...");
                    statusBar.Content = $"Скачивание {fileName}...";

                    await _ftpClient.DownloadFile(saveFileDialog.FileName, remotePath);

                    Log($"Файл {fileName} скачан успешно");
                    statusBar.Content = $"Загрузка завершена: {fileName}";
                }
                catch (Exception ex)
                {
                    Log($"Ошибка скачивания: {ex.Message}");
                    statusBar.Content = "Скачивание прервано";
                }
            }
        }
        // Обработчик кнопки "Назад"
        private async void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_pathHistory.Count > 0)
            {
                string previousPath = _pathHistory.Pop();
                await LoadDirectory(previousPath);
            }
        }
        // Обработчик закрытия окна
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Disconnect();  // Отключаемся от сервера при закрытии приложения
        }
    }
}