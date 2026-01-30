using ObjectDetectionApp.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;
using ShapesRectangle = System.Windows.Shapes.Rectangle;

namespace ObjectDetectionApp.Views
{
    public partial class LabelingDialog : Window
    {
        private System.Windows.Point _startPoint;
        private System.Windows.Point _endPoint;
        private DrawingRectangle _selectedArea;
        private bool _isSelecting;
        private ShapesRectangle _selectionRect;

        public TrainingData Result { get; private set; }
        public byte[] ImageData { get; set; }

        public LabelingDialog()
        {
            InitializeComponent();
        }

        public void SetImage(System.Windows.Media.Imaging.BitmapImage image)
        {
            PreviewImage.Source = image;
        }

        private void SelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isSelecting = true;
            _startPoint = e.GetPosition(SelectionCanvas);

            SelectionCanvas.Children.Clear();
            SelectionCanvas.CaptureMouse();
        }

        private void SelectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting)
                return;

            _endPoint = e.GetPosition(SelectionCanvas);

            // 선택 영역 그리기
            SelectionCanvas.Children.Clear();

            double x = Math.Min(_startPoint.X, _endPoint.X);
            double y = Math.Min(_startPoint.Y, _endPoint.Y);
            double width = Math.Abs(_endPoint.X - _startPoint.X);
            double height = Math.Abs(_endPoint.Y - _startPoint.Y);

            var rect = new ShapesRectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 2 }),
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 255, 0, 0)),
                Width = width,
                Height = height
            };

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);

            SelectionCanvas.Children.Add(rect);

            // 좌표 표시 업데이트
            UpdateCoordinateDisplay(x, y, width, height);
        }

        private void SelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting)
                return;

            _isSelecting = false;
            _endPoint = e.GetPosition(SelectionCanvas);
            SelectionCanvas.ReleaseMouseCapture();

            // 이미지 좌표로 변환
            CalculateImageCoordinates();
        }

        private void CalculateImageCoordinates()
        {
            if (PreviewImage.Source == null)
                return;

            // Use BitmapSource.PixelWidth/PixelHeight for accurate image pixel size
            if (!(PreviewImage.Source is BitmapSource bmp))
                return;

            double imageWidth = bmp.PixelWidth;
            double imageHeight = bmp.PixelHeight;

            // Image control actual display size
            double displayWidth = PreviewImage.ActualWidth;
            double displayHeight = PreviewImage.ActualHeight;

            if (displayWidth <= 0 || displayHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
                return;

            // Compute how the image is fitted inside the Image control (Stretch=Uniform)
            double imageAspect = imageWidth / imageHeight;
            double displayAspect = displayWidth / displayHeight;

            double actualImageWidth, actualImageHeight;
            double offsetX = 0, offsetY = 0;

            if (imageAspect > displayAspect)
            {
                // image fills horizontally
                actualImageWidth = displayWidth;
                actualImageHeight = displayWidth / imageAspect;
                offsetY = (displayHeight - actualImageHeight) / 2.0;
            }
            else
            {
                // image fills vertically
                actualImageHeight = displayHeight;
                actualImageWidth = displayHeight * imageAspect;
                offsetX = (displayWidth - actualImageWidth) / 2.0;
            }

            // Determine image top-left in canvas coordinates (handle cases where Image control isn't at 0,0)
            Point controlTopLeftInCanvas;
            try
            {
                var transform = PreviewImage.TransformToVisual(SelectionCanvas);
                controlTopLeftInCanvas = transform.Transform(new Point(0, 0));
            }
            catch
            {
                controlTopLeftInCanvas = new Point(0, 0);
            }

            double imageTopLeftX = controlTopLeftInCanvas.X + offsetX;
            double imageTopLeftY = controlTopLeftInCanvas.Y + offsetY;

            // Convert canvas coordinates to image pixel coordinates
            double startX = Math.Min(_startPoint.X, _endPoint.X);
            double startY = Math.Min(_startPoint.Y, _endPoint.Y);
            double selWidth = Math.Abs(_endPoint.X - _startPoint.X);
            double selHeight = Math.Abs(_endPoint.Y - _startPoint.Y);

            // Adjust selection relative to actual image area inside the control
            double relX = startX - imageTopLeftX;
            double relY = startY - imageTopLeftY;

            // If selection is outside image area, clamp
            relX = Math.Max(0, relX);
            relY = Math.Max(0, relY);
            relX = Math.Min(actualImageWidth, relX);
            relY = Math.Min(actualImageHeight, relY);

            double relRight = Math.Min(actualImageWidth, relX + selWidth);
            double relBottom = Math.Min(actualImageHeight, relY + selHeight);

            double adjWidth = Math.Max(0, relRight - relX);
            double adjHeight = Math.Max(0, relBottom - relY);

            // Scale factors from displayed image to actual image pixels
            double scaleX = imageWidth / actualImageWidth;
            double scaleY = imageHeight / actualImageHeight;

            int x = (int)Math.Round(relX * scaleX);
            int y = (int)Math.Round(relY * scaleY);
            int width = (int)Math.Round(adjWidth * scaleX);
            int height = (int)Math.Round(adjHeight * scaleY);

            // Bounds check
            x = Math.Max(0, Math.Min(x, (int)imageWidth));
            y = Math.Max(0, Math.Min(y, (int)imageHeight));
            width = Math.Max(0, Math.Min(width, (int)imageWidth - x));
            height = Math.Max(0, Math.Min(height, (int)imageHeight - y));

            _selectedArea = new DrawingRectangle(x, y, width, height);

            CoordinateTextBlock.Text = $"이미지 좌표: X={x}, Y={y}, Width={width}, Height={height}";
        }

        private void UpdateCoordinateDisplay(double x, double y, double width, double height)
        {
            CoordinateTextBlock.Text = $"선택 영역: X={x:F0}, Y={y:F0}, Width={width:F0}, Height={height:F0}";
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(LabelNameTextBox.Text))
            {
                MessageBox.Show("라벨 이름을 입력해주세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedArea == null || _selectedArea.Width == 0 || _selectedArea.Height == 0)
            {
                MessageBox.Show("객체 영역을 선택해주세요.", "선택 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new TrainingData
            {
                LabelName = LabelNameTextBox.Text.Trim(),
                BoundingBox = _selectedArea,
                ImageData = ImageData
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}