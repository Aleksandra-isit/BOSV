using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TryCameraEnguCV
{
    public class CameraSettingsViewModel : INotifyPropertyChanged
    {
        private double _brightness;
        private double _saturation;
        private double _sharpness;
        private double _redFactor = 1.0;
        private double _blueFactor = 1.0;

        public double Brightness
        {
            get => _brightness;
            set { _brightness = value; OnPropertyChanged(nameof(Brightness)); }
        }

        public double Saturation
        {
            get => _saturation;
            set { _saturation = value; OnPropertyChanged(nameof(Saturation)); }
        }

        public double Sharpness
        {
            get => _sharpness;
            set { _sharpness = value; OnPropertyChanged(nameof(Sharpness)); }
        }

        public double RedFactor
        {
            get => _redFactor;
            set 
            { 
                _redFactor = Math.Abs(value) < 0.01 ? 0 : value;
                OnPropertyChanged(nameof(RedFactor)); 
            }
        }

        public double BlueFactor
        {
            get => _blueFactor;
            set 
            {
                _blueFactor = Math.Abs(value) < 0.01 ? 0 : value;
                OnPropertyChanged(nameof(BlueFactor)); 
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
