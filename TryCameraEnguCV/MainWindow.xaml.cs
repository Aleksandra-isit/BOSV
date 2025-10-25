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

        // Захват видеоизображения
        private VideoWriter? _videoWriter;
        private bool _isRecording = false;
        private const double targetFps = 30; // FPS видео
        private CancellationTokenSource? _recordingCts;

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

            // Привязка клавиш
            initializeKeys();

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
