using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenCvSharp;
using ObjectDetectionApp.Models;
using ObjectDetectionApp.Services;
using ObjectDetectionApp.Views;
using DrawingRectangle = System.Drawing.Rectangle;
using ShapesRectangle = System.Windows.Shapes.Rectangle;

namespace ObjectDetectionApp
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly CameraService _cameraService;
        private readonly ImageProcessingService _imageProcessing;
        private readonly ObjectDetectionService _detectionService;

        private AppMode _currentMode = AppMode.Idle;
        private List<TrainingData> _trainingDataList;
        private Mat _currentFrame;
        private int _selectedCameraIndex = -1;

        // 마우스 드래그 관련
        private bool _isDragging;
        private System.Windows.Point _dragStartPoint;

        public MainWindow()
        {
            InitializeComponent();

            _cameraService = new CameraService();
            _imageProcessing = new ImageProcessingService();
            _detectionService = new ObjectDetectionService();
            _trainingDataList = new List<TrainingData>();

            _cameraService.FrameCaptured += OnFrameCaptured;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshCameraList();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cameraService?.StopCamera();
            _cameraService?.Dispose();
            _currentFrame?.Dispose();
        }

        #region 카메라 관리

        private void RefreshCameraList()
        {
            var cameras = _cameraService.GetAvailableCameras();

            CameraComboBox.ItemsSource = cameras;

            if (cameras.Count > 0)
            {
                CameraComboBox.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("사용 가능한 카메라가 없습니다.", "카메라 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshCameras_Click(object sender, RoutedEventArgs e)
        {
            RefreshCameraList();
        }

        private void CameraComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraComboBox.SelectedItem is CameraInfo cameraInfo)
            {
                _selectedCameraIndex = cameraInfo.Index;

                // 현재 모드에 따라 카메라 시작
                if (_currentMode == AppMode.Training || _currentMode == AppMode.Detection)
                {
                    StartCamera();
                }
            }
        }

        private void StartCamera()
        {
            if (_selectedCameraIndex < 0)
            {
                MessageBox.Show("카메라를 선택해주세요.", "카메라 선택",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool success = _cameraService.StartCamera(_selectedCameraIndex);

            if (success)
            {
                NoCameraText.Visibility = Visibility.Collapsed;
            }
            else
            {
                MessageBox.Show("카메라를 시작할 수 없습니다.", "카메라 오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                NoCameraText.Visibility = Visibility.Visible;
            }
        }

        private void StopCamera()
        {
            _cameraService.StopCamera();
            NoCameraText.Visibility = Visibility.Visible;
            CameraImage.Source = null;
        }

        private void OnFrameCaptured(object sender, Mat frame)
        {
            if (frame == null || frame.Empty())
                return;

            _currentFrame?.Dispose();
            _currentFrame = frame.Clone();

            Dispatcher.Invoke(() =>
            {
                try
                {
                    Mat displayFrame = frame;

                    // 검출 모드일 때 객체 탐지 수행
                    if (_currentMode == AppMode.Detection && _detectionService.IsModelTrained())
                    {
                        ProcessDetection(ref displayFrame);
                    }

                    var bitmapImage = _imageProcessing.MatToBitmapImage(displayFrame);
                    CameraImage.Source = bitmapImage;

                    if (displayFrame != frame)
                    {
                        displayFrame.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"프레임 표시 오류: {ex.Message}");
                }
            });
        }

        #endregion

        #region 모드 전환

        private void TrainingMode_Click(object sender, RoutedEventArgs e)
        {
            SwitchMode(AppMode.Training);
        }

        private void DetectionMode_Click(object sender, RoutedEventArgs e)
        {
            if (!_detectionService.IsModelTrained())
            {
                MessageBox.Show("먼저 학습 모드에서 객체를 학습시켜주세요.", "모델 미학습",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SwitchMode(AppMode.Detection);
        }

        private void SwitchMode(AppMode newMode)
        {
            _currentMode = newMode;

            // UI 업데이트
            switch (newMode)
            {
                case AppMode.Training:
                    ModeTextBlock.Text = "현재 모드: 학습 모드";
                    ConfirmButton.IsEnabled = true;
                    CancelButton.IsEnabled = true;
                    TrainButton.IsEnabled = _trainingDataList.Count > 0;
                    StartCamera();
                    break;

                case AppMode.Detection:
                    ModeTextBlock.Text = "현재 모드: 검출 모드";
                    ConfirmButton.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    TrainButton.IsEnabled = false;
                    StartCamera();
                    break;

                case AppMode.Idle:
                    ModeTextBlock.Text = "현재 모드: 대기 중";
                    ConfirmButton.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    TrainButton.IsEnabled = false;
                    StopCamera();
                    break;
            }
        }

        #endregion

        #region 학습 모드 처리

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode != AppMode.Training)
                return;

            if (_currentFrame == null || _currentFrame.Empty())
            {
                MessageBox.Show("캡처할 프레임이 없습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 라벨링 다이얼로그 열기
            var dialog = new LabelingDialog();

            var bitmapImage = _imageProcessing.MatToBitmapImage(_currentFrame);
            dialog.SetImage(bitmapImage);
            dialog.ImageData = _imageProcessing.MatToByteArray(_currentFrame);

            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                _trainingDataList.Add(dialog.Result);
                UpdateTrainingDataList();
                TrainButton.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == AppMode.Training)
            {
                SwitchMode(AppMode.Idle);
            }
        }

        private void UpdateTrainingDataList()
        {
            LabelListPanel.Children.Clear();

            foreach (var data in _trainingDataList)
            {
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(5),
                    Padding = new Thickness(10),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 240, 241))
                };

                var stackPanel = new StackPanel();

                var labelText = new TextBlock
                {
                    Text = $"라벨: {data.LabelName}",
                    FontWeight = FontWeights.Bold,
                    FontSize = 14
                };

                var coordText = new TextBlock
                {
                    Text = $"좌표: ({data.BoundingBox.X}, {data.BoundingBox.Y})",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };

                var sizeText = new TextBlock
                {
                    Text = $"크기: {data.BoundingBox.Width} x {data.BoundingBox.Height}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray)
                };

                var timeText = new TextBlock
                {
                    Text = $"등록: {data.CreatedAt:HH:mm:ss}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.DarkGray)
                };

                stackPanel.Children.Add(labelText);
                stackPanel.Children.Add(coordText);
                stackPanel.Children.Add(sizeText);
                stackPanel.Children.Add(timeText);

                border.Child = stackPanel;
                LabelListPanel.Children.Add(border);
            }

            // 총 개수 표시
            var summaryBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),
                Padding = new Thickness(10),
                Margin = new Thickness(5)
            };

            var summaryText = new TextBlock
            {
                Text = $"총 {_trainingDataList.Count}개 학습 데이터",
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            summaryBorder.Child = summaryText;
            LabelListPanel.Children.Insert(0, summaryBorder);
        }

        private async void Train_Click(object sender, RoutedEventArgs e)
        {
            if (_trainingDataList.Count == 0)
            {
                MessageBox.Show("학습할 데이터가 없습니다.", "학습 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TrainButton.IsEnabled = false;
            TrainButton.Content = "학습 중...";

            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    bool success = _detectionService.TrainModel(_trainingDataList);

                    Dispatcher.Invoke(() =>
                    {
                        if (success)
                        {
                            MessageBox.Show($"{_trainingDataList.Count}개의 데이터로 모델 학습이 완료되었습니다.",
                                "학습 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("모델 학습에 실패했습니다.", "학습 실패",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            TrainButton.IsEnabled = true;
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"학습 중 오류 발생: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                TrainButton.IsEnabled = true;
            }
            finally
            {
                TrainButton.Content = "학습하기";
            }
        }

        #endregion

        #region 검출 모드 처리

        private void ProcessDetection(ref Mat frame)
        {
            try
            {
                // 이미지 보정
                var enhancedFrame = _imageProcessing.EnhanceImage(frame);

                // 객체 탐지
                var imageData = _imageProcessing.MatToByteArray(enhancedFrame);
                var detections = _detectionService.DetectObjects(imageData);

                // 검출 결과 표시
                if (detections != null && detections.Count > 0)
                {
                    foreach (var detection in detections)
                    {
                        // 정확도 업데이트
                        UpdateAccuracy(detection.Confidence * 100);

                        // 검출된 객체 박스 그리기 (전체 화면)
                        Cv2.PutText(enhancedFrame,
                            $"{detection.Label}: {detection.Confidence:P0}",
                            new OpenCvSharp.Point(10, 30),
                            HersheyFonts.HersheySimplex,
                            1.0,
                            Scalar.Green,
                            2);

                        // 저장 (필요시)
                        SaveDetectedObject(enhancedFrame, detection);
                    }
                }
                else
                {
                    UpdateAccuracy(0);
                }

                frame = enhancedFrame;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"검출 처리 오류: {ex.Message}");
            }
        }

        private void UpdateAccuracy(double accuracy)
        {
            AccuracyProgressBar.Value = accuracy;
            AccuracyTextBlock.Text = $"{accuracy:F1}%";
        }

        private void SaveDetectedObject(Mat frame, DetectionResult detection)
        {
            try
            {
                string fileName = $"detected_{detection.Label}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string savePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "ObjectDetection",
                    fileName);

                var directory = System.IO.Path.GetDirectoryName(savePath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                Cv2.ImWrite(savePath, frame);
                Console.WriteLine($"검출된 객체 저장: {savePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"객체 저장 오류: {ex.Message}");
            }
        }

        #endregion

        #region 마우스 이벤트

        private void CameraImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 학습 모드가 아닐 때는 무시
            if (_currentMode != AppMode.Training)
                return;

            _isDragging = false;
            _dragStartPoint = e.GetPosition(CameraImage);
        }

        private void CameraImage_MouseMove(object sender, MouseEventArgs e)
        {
            // 향후 실시간 영역 선택 기능 추가 가능
        }

        private void CameraImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
        }

        #endregion
    }
}