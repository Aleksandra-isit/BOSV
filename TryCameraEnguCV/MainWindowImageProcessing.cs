using Emgu.CV;
using Emgu.CV.Structure;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TryCameraEnguCV
{
    public partial class MainWindow : Window
    {
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

        private void UpdateTransform()
        {
            double scale = _scaleLevels[_scaleIndex];

            _cameraTransform.ScaleX = _isMirrored ? -scale : scale;
            _cameraTransform.ScaleY = scale;
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
                    redFactor = (int)_cameraSettings.RedFactor;
                    break;
                case nameof(CameraSettingsViewModel.BlueFactor):
                    blueFactor = (int)_cameraSettings.BlueFactor;
                    break;
            }
        }

    }
}
