using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace TryCameraEnguCV
{
    /// <summary>
    /// Логика взаимодействия для SettingsMenu.xaml
    /// </summary>
    public partial class SettingsMenu : Window
    {
        string folder = (string)AppDomain.CurrentDomain.GetData("AppDataPath");


        // Делегаты для изменения видимости на главном экране
        public Action<bool>? OnPatientDataToggled;
        public Action<bool>? OnClockToggled;


        public void SetPatientDataState(bool isVisible)
        {
            PatientDataToggle.IsChecked = isVisible;
        }

        public void SetClockState(bool isVisible)
        {
            ClockToggle.IsChecked = isVisible;
        }

        private bool _updatingFromUi = false;
        private bool _uiAdjustingBrightness = false;       // пользователь двигает слайдер
        private int _lastBrightnessSentToUi = -1;          // последняя синхронизированная яркость
        private readonly ComController _comController;
        public SettingsMenu(ComController comController)
        {
            InitializeComponent();

            // Определяем положение окна — снизу слева
            var screen = SystemParameters.WorkArea; // рабочая область (без панели задач)
            this.Left = screen.Left + 10;            // немного отступаем от левого края
            this.Top = screen.Bottom - this.Height - 10; // немного отступаем от нижнего края


            _comController = comController;

            // --- Подписка на COM-ответы ---
            _comController.DataReceived += hex =>
            {
                Dispatcher.Invoke(() =>
                {
                    int brightnessFromBlock = _comController.CurrentBrightness;
                    bool lightFromBlock = _comController.LightOn;

                    // Игнорируем, если пользователь трогает слайдер
                    if (!_uiAdjustingBrightness && BOSVSlider.Value != brightnessFromBlock)
                    {
                        BOSVSlider.Value = brightnessFromBlock;
                        _lastBrightnessSentToUi = brightnessFromBlock;
                    }

                    // Обновляем Toggle только если реально отличается от UI
                    if (BOSVToggle.IsChecked != _comController.LightOn)
                    {
                        BOSVToggle.IsChecked = _comController.LightOn;
                    }

                    // Помпа всегда синхронизируется
                    PumpToggle.IsChecked = _comController.IsPumpOn;
                    PumpPowerToggle.IsChecked = _comController.IsPumpMax;
                    UpdatePumpStatusText();
                });
            };


            // --- Синхронизация ToggleButton и Slider с COM ---
            BOSVToggle.Checked += (s, e) => SetBrightnessFromUi(1);
            BOSVToggle.Unchecked += (s, e) => SetBrightnessFromUi(0);

            BOSVSlider.PreviewMouseDown += (s, e) => _uiAdjustingBrightness = true;
            BOSVSlider.PreviewMouseUp += (s, e) =>
            {
                _uiAdjustingBrightness = false;
                SetBrightnessFromUi((int)BOSVSlider.Value);
            }; ;
            BOSVSlider.ValueChanged += (s, e) =>
            {
                if (_uiAdjustingBrightness)
                {
                    // синхронизируем ToggleButton по положению ползунка
                    BOSVToggle.IsChecked = BOSVSlider.Value > 0;
                }
            };
            // --------------------------------------------------


            // --- Управление помпой ---
            PumpToggle.Checked += (s, e) => UpdatePumpFromUi();
            PumpToggle.Unchecked += (s, e) => UpdatePumpFromUi();
            PumpPowerToggle.Checked += (s, e) => UpdatePumpFromUi();
            PumpPowerToggle.Unchecked += (s, e) => UpdatePumpFromUi();
            // ---------------------------

            this.PreviewKeyDown += SettingsMenu_PreviewKeyDown; // привязываем обработку клавиш
            LoadMedia();

            // Подписка на переключение других кнопок
            PatientDataToggle.Checked += (s, e) => OnPatientDataToggled?.Invoke(true);
            PatientDataToggle.Unchecked += (s, e) => OnPatientDataToggled?.Invoke(false);

            ClockToggle.Checked += (s, e) => OnClockToggled?.Invoke(true);
            ClockToggle.Unchecked += (s, e) => OnClockToggled?.Invoke(false);
        }

        // --- Метод отправки яркости с UI ---
        private void SetBrightnessFromUi(int value)
        {
            // Локально обновляем яркость, чтобы блок не перетягивал UI
            _comController.CurrentBrightness = value;

            bool lightOn = value > 0;
            _comController.LightOn = lightOn;

            _comController.SetBrightness(value);

            BOSVSlider.Value = value;
            BOSVToggle.IsChecked = lightOn;
            _lastBrightnessSentToUi = value;
        }

        private void UpdatePumpStatusText()
        {
            string onOff = PumpToggle.IsChecked == true ? "Вкл" : "Выкл";
            string power = PumpPowerToggle.IsChecked == true ? "Макс" : "Норм";
            PumpStatusTextBlock.Text = $"{onOff} ({power})";
        }

        private void UpdatePumpFromUi()
        {
            _updatingFromUi = true;
            _comController.IsPumpOn = PumpToggle.IsChecked == true;
            _comController.IsPumpMax = PumpPowerToggle.IsChecked == true;
            _updatingFromUi = false;
        }


        private void LoadMedia()
        {
            if (!Directory.Exists(folder)) return;

            // Загрузка изображений
            var imageFiles = Directory.GetFiles(folder, "*.jpg");
            var images = imageFiles.Select(f => new
            {
                FilePath = f,
                Thumbnail = new BitmapImage(new Uri(f))
            }).ToList();
            ImagesList.ItemsSource = images;

            // Загрузка видеозображений
            var videoFiles = Directory.GetFiles(folder, "*.avi");
            var videos = videoFiles.Select(f => new
            {
                FilePath = f,
                FileName = System.IO.Path.GetFileName(f),
                Thumbnail = GetVideoThumbnail(f)
            }).ToList();
            VideosList.ItemsSource = videos;

        }

        private BitmapSource GetVideoThumbnail(string videoPath)
        {
            // Путь к ffmpeg.exe
            string ffmpegExe = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg", "ffmpeg.exe");

            if (!File.Exists(ffmpegExe))
                throw new FileNotFoundException("FFmpeg не найден", ffmpegExe);

            // Временный файл для кадра
            string tempImage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".jpg");

            // Формируем команду: взять кадр на 1-й секунде
            var args = $"-y -i \"{videoPath}\" -ss 00:00:01 -frames:v 1 \"{tempImage}\"";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (!File.Exists(tempImage))
                return null; // или возвращаем заглушку

            // Загружаем кадр как BitmapImage
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(tempImage);
            bitmap.EndInit();
            bitmap.Freeze(); // Чтобы использовать в UI из любого потока

            // Удаляем временный файл
            File.Delete(tempImage);

            return bitmap;
        }


        private void Image_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var img = (sender as Image)?.DataContext as dynamic;
                if (img == null) return;

                var window = new Window
                {
                    Title = "Просмотр изображения",
                    Width = 800,
                    Height = 600,
                    Content = new Image
                    {
                        Source = new BitmapImage(new Uri(img.FilePath)),
                        Stretch = Stretch.Uniform
                    }
                };
                window.ShowDialog();
            }
        }

        private void Video_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var video = (sender as FrameworkElement)?.DataContext as dynamic;
                if (video == null) return;

                var window = new Window
                {
                    Title = "Просмотр видео",
                    Width = 800,
                    Height = 600
                };

                var media = new MediaElement
                {
                    Source = new Uri(video.FilePath),
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Stop,
                    Stretch = Stretch.Uniform
                };

                window.Content = media;
                window.Loaded += (s, _) => media.Play();

                window.ShowDialog();
            }
        }

        private void SettingsMenu_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void CloseSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private string _sourceFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BUSV", "VideoProcessor");


        private string? _usbDrivePath;

        /// <summary>
        /// Проверка наличия usb-носителя при открытии вкладки копирования
        /// </summary>
        private void DataCopyTabItem_Loaded(object sender, RoutedEventArgs e)
        {
            CheckUsbDrive();
        }

        /// <summary>
        /// Проверка наличия флешки
        /// </summary>
        private void CheckUsb_Click(object sender, RoutedEventArgs e)
        {
            CheckUsbDrive();
        }

        private bool CheckUsbDrive()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                .ToList();

            if (drives.Count > 0)
            {
                _usbDrivePath = drives[0].RootDirectory.FullName;
                UsbStatusText.Text = $"Флешка обнаружена: {_usbDrivePath}";
                UsbStatusText.Foreground = new SolidColorBrush(Colors.Green);
                DataCopyBtn.Visibility = Visibility.Visible;
            }
            else
            {
                _usbDrivePath = null;
                UsbStatusText.Text = "Флешка не найдена. Вставьте устройство и нажмите 'Проверить флешку'.";
                UsbStatusText.Foreground = new SolidColorBrush(Colors.Red);
                DataCopyBtn.Visibility = Visibility.Hidden;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Копирование данных
        /// </summary>
        private async void CopyData_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckUsbDrive())
            {
                return;
            }

            CopyProgress.Visibility = Visibility.Visible;
            CopyProgressText.Visibility = Visibility.Visible;
            CopyProgress.Value = 0;

            var sourceFiles = Directory.GetFiles(_sourceFolder, "*", SearchOption.AllDirectories);
            int total = sourceFiles.Length;
            int copied = 0;

            await Task.Run(() =>
            {
                foreach (var file in sourceFiles)
                {
                    try 
                    {


                        string relativePath = file.Substring(_sourceFolder.Length).TrimStart('\\');
                        string destPath = Path.Combine(_usbDrivePath, "VideoProcessor", relativePath);

                        string destDir = Path.GetDirectoryName(destPath)!;
                        Directory.CreateDirectory(destDir);

                        // Пропуск, если файл уже существует
                        if (!File.Exists(destPath))
                        {
                            File.Copy(file, destPath, false);
                        }

                        copied++;
                        Dispatcher.Invoke(() => {
                            CopyProgress.Value = (double)copied / total * 100;
                            CopyProgressText.Text = $"Копируется: {Path.GetFileName(file)}";
                        },
                            DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                            MessageBox.Show($"Ошибка при копировании {file}:\n{ex.Message}")
                        );
                    }
                }
            });

            CopyProgress.Visibility = Visibility.Collapsed;
            CopyProgressText.Visibility = Visibility.Collapsed;
            MessageBox.Show("Копирование завершено!", "Готово");
        }

        // Событие для сброса настроек по умолчанию
        public event Action? ResetSettingsRequested;
        private void ResetSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            // Генерируем событие
            ResetSettingsRequested?.Invoke();
        }
    }
}
