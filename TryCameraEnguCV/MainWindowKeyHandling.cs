using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TryCameraEnguCV
{
    public partial class MainWindow : Window
    {
        //свойства для обработки клавиш
        public ICommand ToggleScreenDataCommand { get; set; }
        public ICommand ToggleSharpnessCommand { get; set; }
        public ICommand ToggleFreezeFrameCommand { get; set; }
        public ICommand ToggleStopwatchCommand { get; set; }
        public ICommand ScreenshotCommand { get; set; }
        public ICommand ShowSettingsMenuCommand { get; set; }
        public ICommand FocusOnTextBoxCommand { get; set; }
        public ICommand ClearTextCommand { get; set; }
        public ICommand ToggleOrientationCommand { get; set; }
        public ICommand VideoRecordingCommand { get; set; }
        public ICommand ToggleScaleCommand { get; set; }

        private void initializeKeys()
        {
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

        }
    }
}
