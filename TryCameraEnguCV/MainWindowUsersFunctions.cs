using Emgu.CV.XPhoto;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TryCameraEnguCV
{
    public partial class MainWindow : Window
    {
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
                    BlueFactor = 0.0,
                    WhiteBalance = 3421.0
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
                _cameraSettings.WhiteBalance = settings.GetValueOrDefault("WhiteBalance", 3421.0);
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
                BlueFactor = _cameraSettings.BlueFactor,
                WhiteBalance = _cameraSettings.WhiteBalance,
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
                //CompositionTarget.Rendering -= UpdateCameraFrameFast;

                // ⏱ Останавливаем таймеры
                _timer?.Stop();
                _stopwatchTimer?.Stop();

                // 🎥 Освобождаем ресурсы камеры
                _isCameraActive = false;
                StopCameraLoop();
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

    }
}