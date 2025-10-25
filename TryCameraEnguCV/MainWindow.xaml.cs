using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TryCameraEnguCV
{
    public partial class MainWindow : Window
    {
        private SettingsMenu _settingsMenu; // Модальное окно меню (единственный экземпляр)
        private CameraSettingsViewModel _cameraSettings; // Настройки камеры

        private VideoCapture _capture;
        private DispatcherTimer _timer;

        private DispatcherTimer _clockTimer = new DispatcherTimer();

        private int blueFactor = 1; // коэффициент усиления синего
        private int redFactor = 1;  // коэффициент усиления красного

        // Данные экрана
        private bool _screenDataVisibility = false;
        // Четкость
        private readonly double[] _sharpnessLevels = { 0, 1, 2, 3 };
        private int _sharpnessIndex = 0;
        // Увеличение
        private readonly double[] _scaleLevels = { 1, 1.25, 1.5 };
        private int _scaleIndex = 0;


        //Обработка стоп-кадра
        private bool isFreezeFrame = false;
        private Mat frozenFrame;
        //Секундомер 
        private readonly double[] _stopwatchState = { 1, 1.25, 1.5 };
        private int _stopwatchIndex = 0;
        private DispatcherTimer _stopwatchTimer;
        private TimeSpan _elapsedTime = TimeSpan.Zero;
        private DateTime _lastStartTime;


        // Изменение ориентации изображения
        private bool _isMirrored = false;
        private ScaleTransform _cameraTransform;

        // Захват видеоизображения
        private VideoWriter? _videoWriter;
        private bool _isRecording = false;
        private const double targetFps = 30; // FPS видео
        private CancellationTokenSource? _recordingCts;


        //свойства для обработки клавиш
        public ICommand ToggleScreenDataCommand { get; }
        public ICommand ToggleSharpnessCommand { get; }
        public ICommand ToggleFreezeFrameCommand { get; }
        public ICommand ToggleStopwatchCommand { get; }
        public ICommand ScreenshotCommand { get; }
        public ICommand ShowSettingsMenuCommand { get; }
        public ICommand FocusOnTextBoxCommand { get; }
        public ICommand ClearTextCommand { get; }
        public ICommand ToggleOrientationCommand { get; }
        public ICommand VideoRecordingCommand { get; }
        public ICommand ToggleScaleCommand { get; }

        // ТЕКУЩИЙ КАДР в UI ФОРМАТЕ
        private WriteableBitmap _cameraBitmap;


        public MainWindow()
        {
            InitializeComponent();
        }


        // ТЕКУЩИЙ ПОЛЬЗОВАТЕЛЬ ДЛЯ СОХРАНЕНИЯ НАСТРОЕК В НУЖНЫЙ ФАЙЛ
        private readonly User _currentUser;

        // COM-PORT для общения с БОСВ
        private ComController _comController;
        public MainWindow(User user)
        {
            InitializeComponent();
            Application.Current.MainWindow = this;

            _comController = new ComController("COM3", 9600);
            _comController.DataReceived += msg =>
            {
                // Можно выводить в Debug или TextBox в UI через Dispatcher
                Dispatcher.Invoke(() =>
                {
                });
            };
            _comController.Start();

            // Загружаемся под профилем нужного врача
            _currentUser = user;
            Title = $"БУСВ — {_currentUser.Name}";

            // Привязка DataContext для команд
            this.DataContext = this;

            // инициализируем таймер
            _stopwatchTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1) // обновляем 1 раз в секунду
            };
            _stopwatchTimer.Tick += (s, e) =>
            {
                var currentElapsed = _elapsedTime + (DateTime.Now - _lastStartTime);
                stopwatchText.Text = currentElapsed.ToString(@"hh\:mm\:ss");
            };


            // создаём transform и сразу привязываем к Image
            _cameraTransform = new ScaleTransform(1, 1);
            cameraImage.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5); // центр для трансформаций
            cameraImage.RenderTransform = _cameraTransform;

            // Инициализация команд
            ToggleScreenDataCommand = new RelayCommand(_ =>
            {
                _screenDataVisibility = !_screenDataVisibility;
                hideScreenData();
            });
            ToggleSharpnessCommand = new RelayCommand(_ => ToggleSharpness());
            ToggleFreezeFrameCommand = new RelayCommand(_ => ToggleFreezeFrame());
            ToggleStopwatchCommand = new RelayCommand(_ => ToggleStopwatch());
            ScreenshotCommand = new RelayCommand(_ => TakeScreenshot());
            ShowSettingsMenuCommand = new RelayCommand(_ => showUpSettingsMenu());
            ClearTextCommand = new RelayCommand(_ => {
                SurnameTextBox.Text = NameTextBox.Text = PatronymicTextBox.Text = "";
            });
            FocusOnTextBoxCommand = new RelayCommand(_ => {
                SurnameTextBox.Focus();
                Keyboard.Focus(SurnameTextBox);
                SurnameTextBox.CaretIndex = SurnameTextBox.Text.Length;
            });
            ToggleOrientationCommand = new RelayCommand(_ => {
                _isMirrored = !_isMirrored;
                UpdateTransform();
            });
            ToggleScaleCommand = new RelayCommand(_ => {
                _scaleIndex = (_scaleIndex + 1) % _scaleLevels.Length;
                UpdateTransform();
            });
            VideoRecordingCommand = new RelayCommand(_ => VideoRecording());

            // Настройки камеры
            _cameraSettings = new CameraSettingsViewModel();
            // Подписка на изменения
            _cameraSettings.PropertyChanged += CameraSettings_PropertyChanged;

            // Пробуем открыть камеру сразу
            _capture = new VideoCapture(0);
            TimeStackPanel.Visibility = Visibility.Visible;


            if (_capture != null && _capture.IsOpened)
            {
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameWidth, 1920);
                _capture.Set(Emgu.CV.CvEnum.CapProp.FrameHeight, 1080);

                // Загружаем пользовательские настройки
                LoadUserSettings();

                // Создаём WriteableBitmap один раз
                _cameraBitmap = BitmapSourceConvertFast.CreateWriteableBitmap(1920, 1080);
                cameraImage.Source = _cameraBitmap;

                // Подписка на CompositionTarget.Rendering для плавного видео
                CompositionTarget.Rendering += (s, e) =>
                {
                    UpdateCameraFrameFast(s, e);
                };
            }
            else
            {
                MessageBox.Show("Камера не обнаружена!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool _hasFrameErrorShown = false;
        private bool _isCameraActive = true;


        // Отрисовка и обработка кадров
        private void UpdateCameraFrameFast(object? sender, EventArgs e)
        {
            if (!_isCameraActive || _capture == null)
                return;

            try
            {
                using var frame = _capture.QueryFrame();
                if (frame == null || frame.IsEmpty)
                {
                    return; // пустой кадр, пропускаем
                }

                using var image = frame.ToImage<Bgr, byte>();
                using var adjusted = ApplyAllAdjustments(image, _cameraSettings.Saturation, blueFactor, redFactor);

                try
                {
                    // --- Обновление UI ---
                    if (!isFreezeFrame)
                    {
                        BitmapSourceConvertFast.UpdateBitmapFromMat(adjusted.Mat, _cameraBitmap);
                    }
                    else
                    {
                        if (frozenFrame != null)
                            BitmapSourceConvertFast.UpdateBitmapFromMat(frozenFrame, _cameraBitmap);

                        BitmapSourceConvertFast.UpdateBitmapFromMat(adjusted.Mat, miniCameraImage.Source as WriteableBitmap);
                    }

                    // Успешный кадр — сбрасываем флаги ошибок
                    _hasFrameErrorShown = false;
                }
                catch (Exception ex)
                {
                    if (!_hasFrameErrorShown)
                    {
                        MessageBox.Show(
                            $"Ошибка при обновлении кадра:\n{ex.Message}",
                            "Ошибка отображения кадра",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                        _hasFrameErrorShown = true;
                    }
                }

                // --- Запись видео (если нужна) ---
                // if (_isRecording && _videoWriter != null)
                // {
                //     CaptureCanvasForVideo(videoContainer);
                // }
            }
            catch (Exception ex)
            {
                if (!_hasFrameErrorShown)
                {
                    MessageBox.Show(
                        $"Ошибка при обработке кадра:\n{ex.Message}",
                        "Ошибка обработки",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    _hasFrameErrorShown = true;
                }
                // Пропускаем кадр
            }
        }


        private CancellationTokenSource _clockCancellation;
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TimeStackPanel.Visibility = Visibility.Visible;
            _clockCancellation = new CancellationTokenSource();
            StartClockUpdater(_clockCancellation.Token);

            // Фокус на окне, чтобы сразу ловить клавиши
            this.Focus();
            Keyboard.Focus(this);
        }

        private void StartClockUpdater(CancellationToken token)
        {
            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    // Обновляем UI через Dispatcher
                    await Dispatcher.InvokeAsync(() =>
                    {
                        dateText.Text = DateTime.Now.ToString("dd.MM.yyyy");
                        timeText.Text = DateTime.Now.ToString("HH:mm:ss");
                    });

                    await Task.Delay(1000, token); // раз в секунду
                }
            }, token);
        }

        private CancellationTokenSource _stopwatchCancellation;
        private bool _isStopwatchRunning = false;

        private void ToggleStopwatch()
        {
            _stopwatchIndex = (_stopwatchIndex + 1) % _stopwatchState.Length;

            switch (_stopwatchIndex)
            {
                case 0: // сброс
                    _isStopwatchRunning = false;
                    _stopwatchCancellation?.Cancel();
                    _elapsedTime = TimeSpan.Zero;
                    stopwatchText.Visibility = Visibility.Collapsed;
                    stopwatchText.Text = "";
                    break;

                case 1: // старт
                    _lastStartTime = DateTime.Now;
                    stopwatchText.Visibility = Visibility.Visible;
                    _isStopwatchRunning = true;
                    _stopwatchCancellation = new CancellationTokenSource();
                    StartStopwatchUpdater(_stopwatchCancellation.Token);
                    break;

                case 2: // пауза
                    _isStopwatchRunning = false;
                    _stopwatchCancellation?.Cancel();
                    _elapsedTime += DateTime.Now - _lastStartTime;
                    break;
            }
        }

        private async void StartStopwatchUpdater(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _isStopwatchRunning)
                {
                    var currentElapsed = _elapsedTime + (DateTime.Now - _lastStartTime);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        stopwatchText.Text = currentElapsed.ToString(@"hh\:mm\:ss");
                    });

                    await Task.Delay(1000, token);
                }
            }
            catch (TaskCanceledException) { }
        }

        // ЗАГРУЖАЕМ НАСТРОЙКИ ПО КАЖДОМУ ИЗ ПОЛЬЗОВАТЕЛЕЙ
        private void LoadUserSettings()
        {
            // Папка внутри AppData
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BUSV", "UserData");

            Directory.CreateDirectory(userDataDir);

            string userFile = Path.Combine(userDataDir, $"{_currentUser.Name}.json");

            // Если нет файла — создаем с дефолтными настройками
            if (!File.Exists(userFile))
            {
                var defaultSettings = new
                {
                    Brightness = 0.0,
                    Saturation = 1.0,
                    Sharpness = _capture?.Get(Emgu.CV.CvEnum.CapProp.Sharpness) ?? 0.0,
                    RedFactor = 0.0,
                    BlueFactor = 0.0
                };

                string json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(userFile, json);
            }

            // Загружаем настройки
            string jsonText = File.ReadAllText(userFile);
            var settings = JsonSerializer.Deserialize<Dictionary<string, double>>(jsonText);

            if (settings != null)
            {
                _cameraSettings.Brightness = settings.GetValueOrDefault("Brightness", 0.0);
                _cameraSettings.Saturation = settings.GetValueOrDefault("Saturation", 1.0);
                _cameraSettings.Sharpness = settings.GetValueOrDefault("Sharpness", 0.0);
                _cameraSettings.RedFactor = settings.GetValueOrDefault("RedFactor", 0.0);
                _cameraSettings.BlueFactor = settings.GetValueOrDefault("BlueFactor", 0.0);
            }
        }

        // СОХРАНЕНИЕ НАСТРОЕК В ФАЙЛ
        private void SaveUserSettings()
        {
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BUSV", "UserData");

            Directory.CreateDirectory(userDataDir);

            string userFile = Path.Combine(userDataDir, $"{_currentUser.Name}.json");

            var settings = new
            {
                Brightness = _cameraSettings.Brightness,
                Saturation = _cameraSettings.Saturation,
                Sharpness = _cameraSettings.Sharpness,
                RedFactor = _cameraSettings.RedFactor,
                BlueFactor = _cameraSettings.BlueFactor
            };

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(userFile, json);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            ExitApplication(); // Всё завершаем централизованно
        }

        /// <summary>
        /// Полное завершение приложения с сохранением и остановкой всех процессов.
        /// </summary>
        private async void ExitApplication()
        {
            try
            {
                // ⏹ Отключаем все визуальные обновления
                CompositionTarget.Rendering -= UpdateCameraFrameFast;

                // ⏱ Останавливаем таймеры
                _timer?.Stop();
                _stopwatchTimer?.Stop();

                // 🎥 Освобождаем ресурсы камеры
                _isCameraActive = false;
                _capture?.Dispose();

                // 🔌 Завершаем COM
                if (_comController != null)
                    await _comController.StopAsync();

                // ⏸ Отменяем фоновые токены
                _clockCancellation?.Cancel();

                // 💾 Сохраняем пользовательские настройки
                SaveUserSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при завершении приложения:\n{ex.Message}",
                    "Завершение", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                // 🧹 Завершаем приложение полностью
                Application.Current.Shutdown();
            }
        }



        private async void VideoRecording()
        {
            if (!_isRecording)
            {
                string folder = (string)AppDomain.CurrentDomain.GetData("AppDataPath");

                Directory.CreateDirectory(folder);
                string fileName = $"Video_{DateTime.Now:yyyyMMdd_HHmmss}.avi";
                string filePath = Path.Combine(folder, fileName);

                int width = (int)videoContainer.ActualWidth;
                int height = (int)videoContainer.ActualHeight;

                _videoWriter = new VideoWriter(filePath,
                    VideoWriter.Fourcc('M', 'J', 'P', 'G'),
                    10, // задаём реальный FPS для Canvas (~10)
                    new System.Drawing.Size(width, height),
                    true);

                _isRecording = true;
                _recordingCts = new CancellationTokenSource();

                MessageBox.Show($"Запись начата: {fileName}", "Видео", MessageBoxButton.OK, MessageBoxImage.Information);

                try
                {
                    await Task.Run(() => RecordCanvasLoop(videoContainer, _recordingCts.Token));
                }
                catch (OperationCanceledException) { }
            }
            else
            {
                _isRecording = false;
                _recordingCts?.Cancel();
                _videoWriter?.Dispose();
                _videoWriter = null;
                MessageBox.Show("Запись остановлена", "Видео", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task RecordCanvasLoop(FrameworkElement element, CancellationToken token)
        {
            int targetFps = 10;
            int intervalMs = 1000 / targetFps;

            while (!token.IsCancellationRequested)
            {
                var sw = Stopwatch.StartNew();

                await element.Dispatcher.InvokeAsync(() => CaptureCanvasForVideo(element));

                sw.Stop();
                int delay = intervalMs - (int)sw.ElapsedMilliseconds;
                if (delay > 0)
                    await Task.Delay(delay, token); // ждем ровно столько, чтобы соблюсти FPS
            }
        }

        private void CaptureCanvasForVideo(FrameworkElement element)
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) return;
            if (_videoWriter == null) return;

            var rtb = new RenderTargetBitmap((int)element.ActualWidth,
                                             (int)element.ActualHeight,
                                             96, 96, PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);

            using var bitmap = new System.Drawing.Bitmap(ms);
            using var mat = bitmap.ToImage<Bgr, byte>().Mat;

            _videoWriter.Write(mat);
        }

        // Захват изображения
        private void TakeScreenshot()
        {
            try
            {
                // Рендерим содержимое окна (без рамок)
                var target = this.Content as FrameworkElement;
                if (target == null) return;

                // Создаем bitmap по размеру окна
                var rtb = new RenderTargetBitmap(
                    (int)target.ActualWidth,
                    (int)target.ActualHeight,
                    96, 96, //DPI
                    PixelFormats.Pbgra32);

                rtb.Render(target);

                // Кодируем в JPEG
                var encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                string folder = (string)AppDomain.CurrentDomain.GetData("AppDataPath");
                string fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string filePath = System.IO.Path.Combine(folder, fileName);

                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    encoder.Save(fs);
                }

                MessageBox.Show($"Скриншот сохранён: {fileName}", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) 
            {
                MessageBox.Show($"Ошибка при создании скриншота: {ex.Message}");
            }
        }

        // Секундомер
        //private void ToggleStopwatch()
        //{
        //    _stopwatchIndex = (_stopwatchIndex + 1) % _stopwatchState.Length;

        //    switch (_stopwatchIndex)
        //    {
        //        case 0: // выкл
        //            _stopwatchTimer.Stop();
        //            _elapsedTime = TimeSpan.Zero;
        //            stopwatchText.Text = "";
        //            stopwatchText.Visibility = Visibility.Collapsed;
        //            break;
        //        case 1: // старт
        //            _lastStartTime = DateTime.Now;
        //            stopwatchText.Visibility = Visibility.Visible;
        //            _stopwatchTimer.Start();
        //            break;

        //        case 2: // пауза
        //            _stopwatchTimer.Stop();
        //            _elapsedTime += DateTime.Now - _lastStartTime;
        //            break;
        //    }
        //}

        private void UpdateTransform()
        {
            double scale = _scaleLevels[_scaleIndex];

            _cameraTransform.ScaleX = _isMirrored ? -scale : scale;
            _cameraTransform.ScaleY = scale;
        }

        // Данные экрана вкл./выкл.
        private void hideScreenData()
        {
            if (_screenDataVisibility)
            {
                PatientDataStackPanel.Visibility = Visibility.Collapsed;
                TimeStackPanel.Visibility = Visibility.Collapsed;
                SettingsButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                PatientDataStackPanel.Visibility = Visibility.Visible;
                TimeStackPanel.Visibility = Visibility.Visible;
                SettingsButton.Visibility = Visibility.Visible;
            }
        }

        // Обработка стоп-кадра
        private void ToggleFreezeFrame()
        {
            if (!isFreezeFrame)
            {
                // Сохраняем текущий кадр
                using Mat frame = _capture.QueryFrame(); // <-- точка с запятой
                frozenFrame = frame.Clone();


                miniCameraImage.Visibility = Visibility.Visible;

                isFreezeFrame = true;
            }
            else
            {
                // Убираем мини-плеер
                if (miniCameraImage != null)
                {
                    miniCameraImage.Visibility = Visibility.Hidden;
                }

                // Освобождаем стоп-кадр
                frozenFrame?.Dispose();
                frozenFrame = null;

                isFreezeFrame = false;
            }
        }

        // Изменение четкости
        private void ToggleSharpness()
        {
            _sharpnessIndex = (_sharpnessIndex + 1) % _sharpnessLevels.Length;
            _cameraSettings.Sharpness = _sharpnessLevels[_sharpnessIndex];
        }

        private Image<Bgr, byte> ApplyAllAdjustments(Image<Bgr, byte> img, double saturationFactor, double blueFactorRaw, double redFactorRaw)
        {
            Image<Bgr, byte> result = img.Clone();

            // --- 1. софт-насыщенность ---
            if (Math.Abs(saturationFactor - 1.0) > 0.001)
            {
                using var hsv = result.Convert<Hsv, byte>();
                var data = hsv.Data;

                int height = hsv.Height;
                int width = hsv.Width;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int s = (int)(data[y, x, 1] * saturationFactor);
                        if (s < 0) s = 0;
                        if (s > 255) s = 255;
                        data[y, x, 1] = (byte)s;
                    }
                }

                using var saturated = hsv.Convert<Bgr, byte>();
                result.Dispose();
                result = saturated.Clone();
            }

            // --- 2. красный/синий ---
            {
                var data = result.Data;
                int height = result.Height;
                int width = result.Width;

                double blueFactor = 1.0 + (blueFactorRaw / 20.0);
                double redFactor = 1.0 + (redFactorRaw / 20.0);

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int blue = (int)(data[y, x, 0] * blueFactor);
                        int red = (int)(data[y, x, 2] * redFactor);

                        if (blue < 0) blue = 0;
                        if (blue > 255) blue = 255;
                        if (red < 0) red = 0;
                        if (red > 255) red = 255;

                        data[y, x, 0] = (byte)blue;
                        data[y, x, 2] = (byte)red;
                    }
                }
            }

            return result;
        }


        // --- Обработка насыщенности (software) ---
        private Image<Bgr, byte> AdjustSaturation(Image<Bgr, byte> img, double factor)
        {
            var hsv = img.Convert<Hsv, byte>();
            var data = hsv.Data; // [y, x, c], c: 0=H, 1=S, 2=V

            int height = hsv.Height;
            int width = hsv.Width;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int s = (int)(data[y, x, 1] * factor);
                    if (s < 0) s = 0;
                    if (s > 255) s = 255;
                    data[y, x, 1] = (byte)s;
                }
            }

            return hsv.Convert<Bgr, byte>();
        }

        // --- Обработка красного и синего оттенка ---
        private Image<Bgr, byte> AdjustBlueAndRed(Image<Bgr, byte> img, double blueFactorRaw, double redFactorRaw)
        {
            var result = img.Clone();
            var data = result.Data; // [y, x, c] — c: 0=B, 1=G, 2=R

            int height = result.Height;
            int width = result.Width;

            // Переводим [-20..20] в коэффициент [0..2]
            double blueFactor = 1.0 + (blueFactorRaw / 20.0);
            double redFactor = 1.0 + (redFactorRaw / 20.0);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int blue = (int)(data[y, x, 0] * blueFactor);
                    int red = (int)(data[y, x, 2] * redFactor);

                    if (blue < 0) blue = 0;
                    if (blue > 255) blue = 255;
                    if (red < 0) red = 0;
                    if (red > 255) red = 255;

                    data[y, x, 0] = (byte)blue;
                    data[y, x, 2] = (byte)red;
                }
            }

            return result;
        }


        private void UpdateClock(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                dateText.Text = DateTime.Now.ToString("dd.MM.yyyy");
                timeText.Text = DateTime.Now.ToString("HH:mm:ss");
            });
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                // Проверяем, совпадает ли текст с "подсказкой" — если да, то очищаем поле
                if (tb.Name == "SurnameTextBox" && tb.Text == "Введите фамилию")
                    tb.Text = "";
                else if (tb.Name == "NameTextBox" && tb.Text == "Введите имя")
                    tb.Text = "";
                else if (tb.Name == "PatronymicTextBox" && tb.Text == "Введите отчество")
                    tb.Text = "";
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                // Если поле пустое, возвращаем подсказку
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    if (tb.Name == "SurnameTextBox")
                        tb.Text = "Введите фамилию";
                    else if (tb.Name == "NameTextBox")
                        tb.Text = "Введите имя";
                    else if (tb.Name == "PatronymicTextBox")
                        tb.Text = "Введите отчество";
                }
            }
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.Focus();
        }

        // Фокус и общение между textBox
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                // Если поле пустое, восстанавливаем подсказку
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    if (tb.Name == "SurnameTextBox") tb.Text = "Введите фамилию";
                    else if (tb.Name == "NameTextBox") tb.Text = "Введите имя";
                    else if (tb.Name == "PatronymicTextBox") tb.Text = "Введите отчество";

                    tb.CaretIndex = tb.Text.Length;
                }

                // Переключение фокуса
                if (tb.Name == "SurnameTextBox")
                {
                    NameTextBox.Focus();
                }
                else if (tb.Name == "NameTextBox")
                {
                    PatronymicTextBox.Focus();
                }
                else if (tb.Name == "PatronymicTextBox")
                {
                    // Последнее поле → снимаем фокус полностью
                    Keyboard.ClearFocus();
                    FocusManager.SetFocusedElement(this, this);
                    this.Focus();
                }
            }
        }

        // Нажатие на кнопку с настройками
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            showUpSettingsMenu();
        }

        // Отображение/Скрытие меню настроек
        private void showUpSettingsMenu()
        {
            if (_settingsMenu == null)
            {
                try
                {
                    _settingsMenu = new SettingsMenu(_comController);
                    _settingsMenu.Owner = this;

                    if (_cameraSettings != null)
                        _settingsMenu.DataContext = _cameraSettings;

                    _settingsMenu.ResetSettingsRequested += () =>
                    {
                        _cameraSettings.Brightness = 0.0;
                        _cameraSettings.RedFactor = 0.0;
                        _cameraSettings.BlueFactor = 0.0;
                        _cameraSettings.Sharpness = 0.0;
                        _cameraSettings.Saturation = 1.0;
                        SaveUserSettings();
                    };

                    _settingsMenu.OnPatientDataToggled = visible => PatientDataStackPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    _settingsMenu.OnClockToggled = visible => TimeStackPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

                    _settingsMenu.SetPatientDataState(PatientDataStackPanel.Visibility == Visibility.Visible);
                    _settingsMenu.SetClockState(TimeStackPanel.Visibility == Visibility.Visible);

                    _settingsMenu.Closed += (s, e) => _settingsMenu = null;

                    _settingsMenu.Show();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при открытии настроек: {ex.Message}");
                }
            }
            else
            {
                _settingsMenu.Activate();
            }
        }

        // Изменение настроек на камеру прилетают
        private void CameraSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_capture == null) return;

            switch (e.PropertyName)
            {
                case nameof(CameraSettingsViewModel.Brightness):
                    _capture.Set(Emgu.CV.CvEnum.CapProp.Brightness, _cameraSettings.Brightness);
                    break;
                case nameof(CameraSettingsViewModel.Sharpness):
                    _capture.Set(Emgu.CV.CvEnum.CapProp.Sharpness, _cameraSettings.Sharpness);
                    break;
                case nameof(CameraSettingsViewModel.RedFactor):
                    redFactor = (int) _cameraSettings.RedFactor;
                    break;
                case nameof(CameraSettingsViewModel.BlueFactor):
                    blueFactor = (int) _cameraSettings.BlueFactor;
                    break;
            }
        }
    }

    public static class BitmapSourceConvertFast
    {
        /// <summary>
        /// Создаёт WriteableBitmap нужного размера и формата. Используется один раз при инициализации.
        /// </summary>
        public static WriteableBitmap CreateWriteableBitmap(int width, int height)
        {
            return new WriteableBitmap(
                width,
                height,
                96, 96,               // DPI
                PixelFormats.Bgr24,    // соответствует формату EmguCV Bgr
                null);
        }

        /// <summary>
        /// Обновляет WriteableBitmap данными из EmguCV Mat.
        /// </summary>
        public static void UpdateBitmapFromMat(Mat mat, WriteableBitmap targetBitmap)
        {
            if (mat == null || mat.IsEmpty || targetBitmap == null)
                return;

            int width = mat.Cols;
            int height = mat.Rows;
            int matStride = (int)mat.Step;  // количество байт в строке Mat
            int bmpStride = targetBitmap.BackBufferStride;

            targetBitmap.Lock();
            unsafe
            {
                byte* pBackBuffer = (byte*)targetBitmap.BackBuffer;
                byte* pMatData = (byte*)mat.DataPointer;

                for (int y = 0; y < height; y++)
                {
                    byte* src = pMatData + y * matStride;
                    byte* dst = pBackBuffer + y * bmpStride;

                    // копируем всю строку
                    Buffer.MemoryCopy(src, dst, bmpStride, width * 3);
                }
            }

            targetBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
            targetBitmap.Unlock();
        }
    }
}
