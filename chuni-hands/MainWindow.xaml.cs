using Emgu.CV;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace chuni_hands {
    public partial class MainWindow : Window {
        private const string ConfigFile = "chuni-hands.json";

        private VideoCapture _capture;
        private readonly List<Sensor> _sensors = new List<Sensor>(6);
        private Mat _mat = new Mat();
        private byte[] _matData = new byte[0];
        private bool _hasPendingReset = false;

        private Task _captureTask;
        private volatile bool _closing = false;

        private readonly Config _config = new Config();
        private readonly HttpClient _http = new HttpClient();

        public Config Config => _config;

        public int logcount = 0;



        public MainWindow() {
            if (File.Exists(ConfigFile)) {
                _config = Helpers.Deserialize<Config>(ConfigFile);
            }

            for (var i = 0; i < 6; ++i) {
                _sensors.Add(new Sensor(i, _config));
            }

            InitializeComponent();
            Title += " version " + Helpers.GetVersion() + " (Sam07205 Modded For New+/Sun)";

            TheCanvas.Sensors = _sensors;
            RefreshCameras();

            Logger.LogAdded += log => {
                if (logcount >= 100) {
                    LogBox.Document.Blocks.Remove(LogBox.Document.Blocks.FirstBlock); //First line
                }
                else {
                    logcount += 1;
                }

                LogBox.AppendText(log);
                LogBox.AppendText(Environment.NewLine);
                LogBox.ScrollToEnd();
            };
            ThresholdIndexComboBox.SelectedIndex = 0;
            ThresholdBox.SetBinding(TextBox.TextProperty, new Binding("Config.Threshold[0]") { ElementName = "TheWindow" });

            TheCanvas.select_th = 0;
            Logger.Info("\n綠色:並非正在設定的區塊偵測到手\n紅色:並非正在設定的區塊未偵測到手\n藍色:正在設定的區塊偵測到手\n灰色:正在設定的區塊未偵測到手");

        }

        private void FrameUpdate() {

            // compute

            foreach (var sensor in _sensors) {
                sensor.Update(_mat, _hasPendingReset);
            }
            _hasPendingReset = false;

            // send key

            SendKey();

            // update display

            var length = _mat.Rows * _mat.Cols * _mat.NumberOfChannels;
            if (_matData.Length < length) {
                _matData = new byte[length];
            }

            _mat.CopyTo(_matData);

            var bm = BitmapSource.Create(_mat.Cols, _mat.Rows, 96, 96, PixelFormats.Bgr24, null, _matData, _mat.Cols * _mat.NumberOfChannels);
            TheCanvas.Image = bm;
            TheCanvas.InvalidateVisual();
        }

        private void SendKey() {
            if (IsActive) {
                return;
            }

            if (_sensors.All(s => !s.StateChanged)) {
                return;
            }

            switch (_config.SendKeyMode) {
                case "be": {
                        var airKeys = String.Concat(from sensor in _sensors select sensor.Active ? "1" : "0");
                        _http.GetAsync(_config.EndPoint + "?k=" + airKeys);
                        break;
                    }
                case "chuni_io": {
                        ChuniIO.Send(_sensors);
                        break;
                    }
                default:
                    throw new Exception("unknown SendKeyMode");
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            StartCapture();
        }

        private void RefreshCameras() {
            var cameras = CameraHelper.CameraHelper.GetCameras();
            CameraCombo.Items.Clear();
            foreach (var cam in cameras) {
                CameraCombo.Items.Add($"[{cam.Id}] {cam.Name}");
            }

            if (_config.CameraId >= 0 && _config.CameraId < CameraCombo.Items.Count) {
                CameraCombo.SelectedIndex = _config.CameraId;
            }
            else {
                _config.CameraId = 0;
            }
        }

        private void StartCapture() {
            var cap = new VideoCapture(_config.CameraId, VideoCapture.API.DShow);
            if (!cap.IsOpened) {
                Logger.Error("Failed to start video capture");
                return;
            }

            Logger.Info("Video capture started");
            cap.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.FrameWidth, _config.CaptureWidth);
            cap.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.FrameHeight, _config.CaptureHeight);
            cap.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.Autofocus, 0);
            cap.SetCaptureProperty(Emgu.CV.CvEnum.CapProp.Exposure, _config.Exposure);

            _capture = cap;
            _capture.Read(_mat);
            _config.CaptureWidth = _mat.Cols;
            _config.CaptureHeight = _mat.Rows;

            _captureTask = Task.Run(CaptureLoop);
        }

        private void StopCapture() {
            Logger.Info("Stopping capture");

            _closing = true;
            _captureTask?.Wait();
            _captureTask = null;

            _capture?.Stop();
            _capture?.Dispose();
            _capture = null;

            _closing = false;
        }

        private void CaptureLoop() {
            // give camera some time to auto adjust, so user don't need to press reset right after start
            var bootstrapFrames = _config.BootstrapSeconds * _config.Fps;

            while (!_closing) {
                if (bootstrapFrames > 0) {
                    _capture.Read(_mat);
                    --bootstrapFrames;
                }
                else {
                    if (!_config.FreezeVideo) {
                        _capture.Read(_mat);
                    }

                    Dispatcher?.BeginInvoke(new Action(FrameUpdate));
                }

                // what the...?? well, it works
                Thread.Sleep(1000 / _config.Fps);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            StopCapture();
            Helpers.Serialize(_config, ConfigFile);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e) {
            _hasPendingReset = true;
        }

        private void SetThresholdButton_Click(object sender, RoutedEventArgs e) {
            if (int.TryParse(ThresholdBox.Text, out int v)) {
                int index = ThresholdIndexComboBox.SelectedIndex;
                _config.Threshold[index] = v;
                Logger.Info($"Threshold[{index}] = {v}");
            }
            else {
                Logger.Error("Invalid input");
            }
        }
        private void SetThresholdButton_ALL_Click(object sender, RoutedEventArgs e) {
            if (int.TryParse(ThresholdBox.Text, out int v)) {
                for (int i = 0; i <= 5; i++) {
                    _config.Threshold[i] = v;
                }
                Logger.Info($"ALL Threshold = {v}");
            }
            else {
                Logger.Error("Invalid input");
            }
        }



        private void ThresholdIndexComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            int index = ThresholdIndexComboBox.SelectedIndex;
            TheCanvas.select_th = index;
            ThresholdBox.SetBinding(TextBox.TextProperty, new Binding($"Config.Threshold[{index}]") { ElementName = "TheWindow" });
        }

        private void CenterButton_Click(object sender, RoutedEventArgs e) {
            _config.OffsetX = 0;
            _config.OffsetY = 0;
        }

        private void SetCameraBtn_Click(object sender, RoutedEventArgs e) {
            StopCapture();

            _config.CameraId = CameraCombo.SelectedIndex;
            StartCapture();
        }

        private void RefreshCameraBtn_Click(object sender, RoutedEventArgs e) {
            RefreshCameras();
        }
    }
}
