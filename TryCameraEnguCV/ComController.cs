using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Windows.Documents;

public class ComController : IDisposable
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort _serialPort;
    private CancellationTokenSource _cts;
    private Task _sendTask;

    public event Action<string> DataReceived; // для UI/логирования

    private bool _lightOn = false;
    private int _brightness = 0; // 0–10
    private PumpState _pumpState = PumpState.OffNormal;
    private byte[] _command = new byte[] { 0x80, 0x00 };

    private readonly byte[] BrightnessTable = new byte[]
    {
        0x80,0xC2,0xD7,0xEC,0x81,0x96,0xAB,0xC0,0xD5,0xEA,0xFF
    };

    public enum PumpState : byte
    {
        OffNormal = 0x00, OnNormal = 0x01, OffMax = 0x02, OnMax = 0x03
    }

    private enum BOSVAnswersState : byte
    {
        OkNoActions = 0xFF,
        BOSVInError = 0xBF,
        ButtonLightUp = 0xFE,
        ButtonLightDown = 0xFD,
        ButtonLightOnOff = 0xFB,
        ButtonPompaOnOff = 0xEF,
        ButtonPompaMinMax = 0xF7,
    }

    public ComController(string portName = "COM3", int baudRate = 9600)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public int CurrentBrightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0, 10);
    }

    public bool LightOn
    {
        get => _lightOn;
        set => _lightOn = value;
    }

    public bool IsPumpOn
    {
        get => _pumpState == PumpState.OnNormal || _pumpState == PumpState.OnMax;
        set
        {
            bool isMax = IsPumpMax;
            _pumpState = value
                ? (isMax ? PumpState.OnMax : PumpState.OnNormal)
                : (isMax ? PumpState.OffMax : PumpState.OffNormal);
        }
    }

    public bool IsPumpMax
    {
        get => _pumpState == PumpState.OnMax || _pumpState == PumpState.OffMax;
        set
        {
            bool isOn = IsPumpOn;
            _pumpState = value
                ? (isOn ? PumpState.OnMax : PumpState.OffMax)
                : (isOn ? PumpState.OnNormal : PumpState.OffNormal);
        }
    }

    public void Start()
    {
        if (_sendTask != null) return; // уже запущено

        _cts = new CancellationTokenSource();
        _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            ReadTimeout = 1000
        };
        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.Open();

        Debug.WriteLine($"✅ COM-порт открыт: {_portName}");

        // Запускаем цикл отправки команды в отдельном потоке
        _sendTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    CreateCommand();

                    _serialPort.Write(_command, 0, _command.Length);
                    Debug.WriteLine("➡ Отправлено: " + BitConverter.ToString(_command));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("❌ Ошибка при отправке: " + ex.Message);
                }

                await Task.Delay(200, _cts.Token); // 200 мс между командами
            }
        }, _cts.Token);
    }

    /// <summary>
    /// Итоговая команда, посылаемая в контроллер
    /// </summary>
    private void CreateCommand()
    {
        _command[0] = BrightnessTable[_brightness];
        _command[1] = (byte)_pumpState;
        if (_brightness >= 4) _command[1] += 0x08;
    }

    public void SetPumpState(PumpState state)
    {
        _pumpState = state;
    }

    public void SetBrightness(int level)
    {
        _brightness = Math.Clamp(level, 0, 10);
    }

    /// <summary>
    /// Ответ от осветителя
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            int bytesToRead = _serialPort.BytesToRead;
            if (bytesToRead > 0)
            {
                byte[] buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);

                string hex = BitConverter.ToString(buffer); // например: "C2-00-1A-FF"
                Debug.WriteLine("⬅ Принято (HEX): " + hex);

                // Анализируем ответ от блока
                AnalyzeReceivedData(buffer);

                // Событие для внешнего логирования / UI
                DataReceived?.Invoke(hex);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("❌ Ошибка при чтении: " + ex.Message);
        }
    }

    /// <summary>
    /// Анализ ответа от осветителя
    /// </summary>
    public void AnalyzeReceivedData(byte[] buffer)
    {
        if (buffer.Length < 2) return;

        switch ((BOSVAnswersState)buffer[1])
        {
            case BOSVAnswersState.OkNoActions:
                // ничего не делать
                break;

            case BOSVAnswersState.ButtonLightUp:
                _brightness = Math.Min(_brightness + 1, 10);
                _lightOn = _brightness > 0; // автоматически включаем свет
                Debug.WriteLine($"🔆 Яркость увеличена до {_brightness}");
                Debug.WriteLine($"💡 Свет {(_lightOn ? "Вкл" : "Выкл")}");
                break;

            case BOSVAnswersState.ButtonLightDown:
                _brightness = Math.Max(_brightness - 1, 0);
                _lightOn = _brightness > 0; // автоматически выключаем, если яркость 0
                Debug.WriteLine($"🔆 Яркость уменьшена до {_brightness}");
                Debug.WriteLine($"💡 Свет {(_lightOn ? "Вкл" : "Выкл")}");
                break;

            case BOSVAnswersState.ButtonLightOnOff:
                _lightOn = !_lightOn;
                Debug.WriteLine($"💡 Свет {(_lightOn ? "Вкл" : "Выкл")}");
                break;

            case BOSVAnswersState.ButtonPompaOnOff:
                // переключаем помпу между On и Off, сохраняя мощность
                switch (_pumpState)
                {
                    case PumpState.OffNormal:
                        _pumpState = PumpState.OnNormal;
                        break;
                    case PumpState.OnNormal:
                        _pumpState = PumpState.OffNormal;
                        break;
                    case PumpState.OffMax:
                        _pumpState = PumpState.OnMax;
                        break;
                    case PumpState.OnMax:
                        _pumpState = PumpState.OffMax;
                        break;
                }
                Debug.WriteLine($"🚰 Помпа {(_pumpState == PumpState.OffNormal || _pumpState == PumpState.OffMax ? "Выкл" : "Вкл")}");
                break;

            case BOSVAnswersState.ButtonPompaMinMax:
                // переключаем мощность помпы, сохраняя состояние On/Off
                switch (_pumpState)
                {
                    case PumpState.OffNormal:
                        _pumpState = PumpState.OffMax;
                        break;
                    case PumpState.OffMax:
                        _pumpState = PumpState.OffNormal;
                        break;
                    case PumpState.OnNormal:
                        _pumpState = PumpState.OnMax;
                        break;
                    case PumpState.OnMax:
                        _pumpState = PumpState.OnNormal;
                        break;
                }
                Debug.WriteLine($"🚰 Помпа мощность {(_pumpState == PumpState.OffNormal || _pumpState == PumpState.OnNormal ? "Норм" : "Макс")}");
                break;

            default:
                Debug.WriteLine($"❌ Неизвестный ответ: 0x{buffer[1]:X2}");
                break;
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _sendTask?.Wait();
            _serialPort?.Close();
            Debug.WriteLine("⚙️ COM-порт закрыт.");
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _serialPort?.Dispose();
        _cts?.Dispose();
    }
}
