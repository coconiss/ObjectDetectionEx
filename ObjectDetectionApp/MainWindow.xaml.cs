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

        // 검출 최적화
        private DateTime _lastDetectionTime = DateTime.MinValue;
        private const int DetectionIntervalMs = 200; // 200ms마다 검출 (초당 5회)

        // 모델 관리
        private List<SavedModel> _savedModels;
        private SavedModel _currentModel;

        public MainWindow()
        {
            InitializeComponent();

            _cameraService = new CameraService();
            _imageProcessing = new ImageProcessingService();
            _detectionService = new ObjectDetectionService();
            _trainingDataList = new List<TrainingData>();
            _savedModels = new List<SavedModel>();

            _cameraService.FrameCaptured += OnFrameCaptured;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshCameraList();
            UpdateModelList();
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
            SelectionCanvas.Children.Clear();
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

                    // 검출 모드일 때 객체 탐지 수행 (스로틀링 적용)
                    if (_currentMode == AppMode.Detection && _detectionService.IsModelTrained())
                    {
                        var timeSinceLastDetection = (DateTime.Now - _lastDetectionTime).TotalMilliseconds;

                        if (timeSinceLastDetection >= DetectionIntervalMs)
                        {
                            ProcessDetection(displayFrame);
                            _lastDetectionTime = DateTime.Now;
                        }
                        // else: 검출 스킵, 기존 박스 유지
                    }

                    var bitmapImage = _imageProcessing.MatToBitmapImage(displayFrame);
                    CameraImage.Source = bitmapImage;
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

            // 캔버스 초기화
            SelectionCanvas.Children.Clear();

            // UI 업데이트
            switch (newMode)
            {
                case AppMode.Training:
                    ModeTextBlock.Text = "현재 모드: 학습 모드 (캡처 후 확인 버튼 클릭)";
                    ModeTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    ConfirmButton.IsEnabled = true;
                    CancelButton.IsEnabled = true;
                    TrainButton.IsEnabled = _trainingDataList.Count > 0;
                    StartCamera();
                    // 학습 모드에서는 현재 학습 데이터 표시
                    UpdateTrainingDataList();
                    break;

                case AppMode.Detection:
                    ModeTextBlock.Text = "현재 모드: 검출 모드 (실시간 객체 검출 중)";
                    ModeTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                    ConfirmButton.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    TrainButton.IsEnabled = false;
                    StartCamera();
                    // 검출 모드에서는 저장된 모델 목록 표시
                    UpdateModelList();
                    break;

                case AppMode.Idle:
                    ModeTextBlock.Text = "현재 모드: 대기 중";
                    ModeTextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(44, 62, 80));
                    ConfirmButton.IsEnabled = false;
                    CancelButton.IsEnabled = false;
                    TrainButton.IsEnabled = false;
                    StopCamera();
                    // 대기 중일 때는 저장된 모델 목록 표시
                    UpdateModelList();
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

                MessageBox.Show($"학습 데이터 추가됨: {dialog.Result.LabelName}", "추가 완료",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

            // 라벨별로 그룹화
            var groupedData = _trainingDataList.GroupBy(x => x.LabelName).ToList();

            foreach (var group in groupedData)
            {
                // 라벨 헤더
                var headerBorder = new Border
                {
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(5)
                };

                var headerText = new TextBlock
                {
                    Text = $"{group.Key} ({group.Count()}개)",
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = FontWeights.Bold,
                    FontSize = 14
                };

                headerBorder.Child = headerText;
                LabelListPanel.Children.Add(headerBorder);

                // 각 데이터 항목
                foreach (var data in group)
                {
                    var border = new Border
                    {
                        BorderBrush = new SolidColorBrush(Colors.LightGray),
                        BorderThickness = new Thickness(1),
                        Margin = new Thickness(10, 2, 5, 2),
                        Padding = new Thickness(8),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 240, 241))
                    };

                    var stackPanel = new StackPanel();

                    var coordText = new TextBlock
                    {
                        Text = $"위치: ({data.BoundingBox.X}, {data.BoundingBox.Y})",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray)
                    };

                    var sizeText = new TextBlock
                    {
                        Text = $"크기: {data.BoundingBox.Width} x {data.BoundingBox.Height}",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Gray)
                    };

                    var timeText = new TextBlock
                    {
                        Text = $"등록: {data.CreatedAt:HH:mm:ss}",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.DarkGray)
                    };

                    stackPanel.Children.Add(coordText);
                    stackPanel.Children.Add(sizeText);
                    stackPanel.Children.Add(timeText);

                    border.Child = stackPanel;
                    LabelListPanel.Children.Add(border);
                }
            }

            // 총 개수 표시
            var summaryBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),
                Padding = new Thickness(10),
                Margin = new Thickness(5, 10, 5, 5)
            };

            var summaryText = new TextBlock
            {
                Text = $"총 {_trainingDataList.Count}개 학습 데이터 ({groupedData.Count}개 라벨)",
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            summaryBorder.Child = summaryText;
            LabelListPanel.Children.Insert(0, summaryBorder);
        }

        private void UpdateModelList()
        {
            LabelListPanel.Children.Clear();

            if (_savedModels.Count == 0)
            {
                var emptyText = new TextBlock
                {
                    Text = "저장된 모델이 없습니다.\n학습 모드에서 모델을 생성하세요.",
                    TextAlignment = TextAlignment.Center,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    Margin = new Thickness(20),
                    FontSize = 14
                };
                LabelListPanel.Children.Add(emptyText);
                return;
            }

            // 모델 목록 표시
            foreach (var model in _savedModels.OrderByDescending(m => m.CreatedAt))
            {
                var modelBorder = new Border
                {
                    BorderBrush = model.IsActive ?
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)) :
                        new SolidColorBrush(Colors.LightGray),
                    BorderThickness = new Thickness(model.IsActive ? 3 : 1),
                    Margin = new Thickness(5),
                    Padding = new Thickness(10),
                    Background = model.IsActive ?
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 255, 230)) :
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 245)),
                    Cursor = Cursors.Hand
                };

                var stackPanel = new StackPanel();

                // 모델 이름
                var nameText = new TextBlock
                {
                    Text = model.Name + (model.IsActive ? " ✓" : ""),
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Foreground = model.IsActive ?
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)) :
                        new SolidColorBrush(Colors.Black)
                };

                // 샘플 수
                var sampleText = new TextBlock
                {
                    Text = $"샘플: {model.SampleCount}개",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    Margin = new Thickness(0, 3, 0, 0)
                };

                // 라벨 목록
                var labelText = new TextBlock
                {
                    Text = $"라벨: {string.Join(", ", model.Labels)}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Gray),
                    TextWrapping = TextWrapping.Wrap
                };

                // 생성 시간
                var timeText = new TextBlock
                {
                    Text = $"생성: {model.CreatedAt:yyyy-MM-dd HH:mm}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.DarkGray),
                    Margin = new Thickness(0, 3, 0, 0)
                };

                // 버튼 패널
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                // 활성화 버튼
                var activateButton = new Button
                {
                    Content = model.IsActive ? "활성화됨" : "활성화",
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 5, 0),
                    Background = model.IsActive ?
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)) :
                        new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 152, 219)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    IsEnabled = !model.IsActive
                };
                activateButton.Click += (s, args) => ActivateModel(model);

                // 삭제 버튼
                var deleteButton = new Button
                {
                    Content = "삭제",
                    Padding = new Thickness(10, 3, 10, 3),
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                deleteButton.Click += (s, args) => DeleteModel(model);

                buttonPanel.Children.Add(activateButton);
                buttonPanel.Children.Add(deleteButton);

                stackPanel.Children.Add(nameText);
                stackPanel.Children.Add(sampleText);
                stackPanel.Children.Add(labelText);
                stackPanel.Children.Add(timeText);
                stackPanel.Children.Add(buttonPanel);

                modelBorder.Child = stackPanel;
                LabelListPanel.Children.Add(modelBorder);
            }

            // 요약 정보
            var summaryBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 73, 94)),
                Padding = new Thickness(10),
                Margin = new Thickness(5, 10, 5, 5)
            };

            var activeModel = _savedModels.FirstOrDefault(m => m.IsActive);
            var summaryText = new TextBlock
            {
                Text = $"총 {_savedModels.Count}개 모델 | 활성: {(activeModel != null ? activeModel.Name : "없음")}",
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            summaryBorder.Child = summaryText;
            LabelListPanel.Children.Insert(0, summaryBorder);
        }

        private void ActivateModel(SavedModel model)
        {
            try
            {
                // 기존 활성 모델 비활성화
                foreach (var m in _savedModels)
                {
                    m.IsActive = false;
                }

                // 선택한 모델 활성화
                model.IsActive = true;
                _currentModel = model;

                // 모델 다시 학습
                bool success = _detectionService.TrainModel(model.TrainingData);

                if (success)
                {
                    UpdateModelList();
                    MessageBox.Show($"'{model.Name}' 모델이 활성화되었습니다.", "모델 전환",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("모델 활성화에 실패했습니다.", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"모델 활성화 오류: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteModel(SavedModel model)
        {
            var result = MessageBox.Show(
                $"'{model.Name}' 모델을 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
                "모델 삭제",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _savedModels.Remove(model);

                // 활성 모델이 삭제되었다면 초기화
                if (model.IsActive)
                {
                    _currentModel = null;
                }

                UpdateModelList();
            }
        }

        private async void Train_Click(object sender, RoutedEventArgs e)
        {
            if (_trainingDataList.Count == 0)
            {
                MessageBox.Show("학습할 데이터가 없습니다.", "학습 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 모델 이름 입력 받기
            var dialog = new System.Windows.Window
            {
                Title = "모델 저장",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "모델 이름:",
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(label, 0);

            var textBox = new TextBox
            {
                Text = $"모델_{DateTime.Now:yyyyMMdd_HHmmss}",
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "저장",
                Width = 80,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(10, 5, 10, 5)
            };
            okButton.Click += (s, args) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };

            var cancelButton = new Button
            {
                Content = "취소",
                Width = 80,
                Padding = new Thickness(10, 5, 10, 5)
            };
            cancelButton.Click += (s, args) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }

            string modelName = textBox.Text.Trim();

            TrainButton.IsEnabled = false;
            TrainButton.Content = "학습 중...";

            try
            {
                bool success = false;

                await System.Threading.Tasks.Task.Run(() =>
                {
                    success = _detectionService.TrainModel(_trainingDataList);
                });

                if (success)
                {
                    // 모델 저장
                    var labels = _trainingDataList.Select(x => x.LabelName).Distinct().ToList();
                    var newModel = new SavedModel
                    {
                        Name = modelName,
                        SampleCount = _trainingDataList.Count,
                        Labels = labels,
                        TrainingData = new List<TrainingData>(_trainingDataList),
                        IsActive = true
                    };

                    // 기존 활성 모델 비활성화
                    foreach (var model in _savedModels)
                    {
                        model.IsActive = false;
                    }

                    _savedModels.Add(newModel);
                    _currentModel = newModel;

                    UpdateModelList();

                    MessageBox.Show($"'{modelName}' 모델이 저장되었습니다.\n샘플 수: {_trainingDataList.Count}개\n라벨: {string.Join(", ", labels)}",
                        "학습 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("모델 학습에 실패했습니다.\n콘솔 로그를 확인하세요.", "학습 실패",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    TrainButton.IsEnabled = true;
                }
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

        private void ProcessDetection(Mat frame)
        {
            try
            {
                SelectionCanvas.Children.Clear();

                // 객체 탐지 (이미지 보정 제거 - 속도 향상)
                var imageData = _imageProcessing.MatToByteArray(frame);
                var detections = _detectionService.DetectObjects(imageData);

                // 검출 결과 표시
                if (detections != null && detections.Count > 0)
                {
                    double maxConfidence = 0;

                    foreach (var detection in detections)
                    {
                        maxConfidence = Math.Max(maxConfidence, detection.Confidence);

                        // 이미지 좌표를 캔버스 좌표로 변환
                        var canvasRect = ImageToCanvasCoordinates(
                            detection.BoundingBox,
                            frame.Width,
                            frame.Height);

                        if (canvasRect.Width <= 0 || canvasRect.Height <= 0)
                            continue;

                        // 바운딩 박스 그리기
                        var rect = new ShapesRectangle
                        {
                            Stroke = new SolidColorBrush(Colors.Lime),
                            StrokeThickness = 3,
                            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 0, 255, 0)),
                            Width = canvasRect.Width,
                            Height = canvasRect.Height
                        };

                        Canvas.SetLeft(rect, canvasRect.X);
                        Canvas.SetTop(rect, canvasRect.Y);

                        SelectionCanvas.Children.Add(rect);

                        // 라벨 텍스트
                        var label = new TextBlock
                        {
                            Text = $"{detection.Label}: {detection.Confidence:P0}",
                            Foreground = new SolidColorBrush(Colors.Lime),
                            FontWeight = FontWeights.Bold,
                            FontSize = 14,
                            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
                            Padding = new Thickness(5, 2, 5, 2)
                        };

                        Canvas.SetLeft(label, canvasRect.X);
                        Canvas.SetTop(label, Math.Max(0, canvasRect.Y - 25));

                        SelectionCanvas.Children.Add(label);
                    }

                    UpdateAccuracy(maxConfidence * 100);
                }
                else
                {
                    UpdateAccuracy(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"검출 처리 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 이미지 좌표를 캔버스 좌표로 변환
        /// </summary>
        private System.Windows.Rect ImageToCanvasCoordinates(DrawingRectangle imageRect, int imageWidth, int imageHeight)
        {
            // Image 컨트롤의 실제 표시 크기
            double displayWidth = CameraImage.ActualWidth;
            double displayHeight = CameraImage.ActualHeight;

            if (displayWidth <= 0 || displayHeight <= 0)
                return new System.Windows.Rect(0, 0, 0, 0);

            // Stretch=Uniform: 비율을 유지하면서 맞춤
            double imageAspect = (double)imageWidth / imageHeight;
            double displayAspect = displayWidth / displayHeight;

            double scale;
            double offsetX = 0;
            double offsetY = 0;

            if (imageAspect > displayAspect)
            {
                // 이미지가 더 넓음 - 가로 기준으로 맞춤
                scale = displayWidth / imageWidth;
                double scaledHeight = imageHeight * scale;
                offsetY = (displayHeight - scaledHeight) / 2;
            }
            else
            {
                // 이미지가 더 높음 - 세로 기준으로 맞춤
                scale = displayHeight / imageHeight;
                double scaledWidth = imageWidth * scale;
                offsetX = (displayWidth - scaledWidth) / 2;
            }

            // 좌표 변환
            double canvasX = imageRect.X * scale + offsetX;
            double canvasY = imageRect.Y * scale + offsetY;
            double canvasWidth = imageRect.Width * scale;
            double canvasHeight = imageRect.Height * scale;

            return new System.Windows.Rect(canvasX, canvasY, canvasWidth, canvasHeight);
        }

        private void UpdateAccuracy(double accuracy)
        {
            AccuracyProgressBar.Value = accuracy;
            AccuracyTextBlock.Text = $"{accuracy:F1}%";

            // 정확도에 따라 색상 변경
            if (accuracy >= 80)
            {
                AccuracyProgressBar.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 204, 113)); // Green
            }
            else if (accuracy >= 60)
            {
                AccuracyProgressBar.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 196, 15)); // Yellow
            }
            else
            {
                AccuracyProgressBar.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 76, 60)); // Red
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