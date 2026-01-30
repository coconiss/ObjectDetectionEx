using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ObjectDetectionApp.Services
{
    /// <summary>
    /// 이미지 처리 서비스
    /// </summary>
    public class ImageProcessingService
    {
        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// Mat을 BitmapImage로 변환
        /// </summary>
        public BitmapImage MatToBitmapImage(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                return null;
            }

            // Convert Mat to System.Drawing.Bitmap
            using (var bitmap = BitmapConverter.ToBitmap(mat))
            {
                IntPtr hBitmap = bitmap.GetHbitmap();
                try
                {
                    // Create BitmapSource from HBitmap
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bitmapSource.Freeze();

                    // Encode BitmapSource to BitmapImage (to ensure stream-backed, thread-safe image)
                    using (var memory = new MemoryStream())
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                        encoder.Save(memory);
                        memory.Position = 0;

                        var bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.StreamSource = memory;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();

                        return bitmapImage;
                    }
                }
                finally
                {
                    // Release HBitmap
                    DeleteObject(hBitmap);
                }
            }
        }

        /// <summary>
        /// Mat을 바이트 배열로 변환
        /// </summary>
        public byte[] MatToByteArray(Mat mat)
        {
            if (mat == null || mat.Empty())
            {
                return null;
            }

            using (var bitmap = BitmapConverter.ToBitmap(mat))
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                return memory.ToArray();
            }
        }

        /// <summary>
        /// 바이트 배열을 Mat으로 변환
        /// </summary>
        public Mat ByteArrayToMat(byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0)
            {
                return null;
            }

            using (var ms = new MemoryStream(imageData))
            using (var bitmap = new Bitmap(ms))
            {
                return BitmapConverter.ToMat(bitmap);
            }
        }

        /// <summary>
        /// 이미지 선명도 향상 (Unsharp Masking)
        /// </summary>
        public Mat EnhanceSharpness(Mat input)
        {
            if (input == null || input.Empty())
            {
                return input;
            }

            var blurred = new Mat();
            Cv2.GaussianBlur(input, blurred, new OpenCvSharp.Size(0, 0), 3);

            var sharpened = new Mat();
            Cv2.AddWeighted(input, 1.5, blurred, -0.5, 0, sharpened);

            blurred.Dispose();

            return sharpened;
        }

        /// <summary>
        /// 이미지 밝기 및 대비 조정
        /// </summary>
        public Mat AdjustBrightnessContrast(Mat input, double alpha = 1.2, int beta = 10)
        {
            if (input == null || input.Empty())
            {
                return input;
            }

            var adjusted = new Mat();
            input.ConvertTo(adjusted, -1, alpha, beta);

            return adjusted;
        }

        /// <summary>
        /// 노이즈 제거
        /// </summary>
        public Mat ReduceNoise(Mat input)
        {
            if (input == null || input.Empty())
            {
                return input;
            }

            var denoised = new Mat();
            Cv2.FastNlMeansDenoisingColored(input, denoised, 10, 10, 7, 21);

            return denoised;
        }

        /// <summary>
        /// 종합 이미지 보정
        /// </summary>
        public Mat EnhanceImage(Mat input)
        {
            if (input == null || input.Empty())
            {
                return input;
            }

            // 1. 노이즈 제거
            var denoised = ReduceNoise(input);

            // 2. 선명도 향상
            var sharpened = EnhanceSharpness(denoised);
            denoised.Dispose();

            // 3. 밝기/대비 조정
            var enhanced = AdjustBrightnessContrast(sharpened);
            sharpened.Dispose();

            return enhanced;
        }

        /// <summary>
        /// 이미지 크롭
        /// </summary>
        public Mat CropImage(Mat input, Rectangle rect)
        {
            if (input == null || input.Empty())
            {
                return null;
            }

            // 경계 검사
            int x = Math.Max(0, rect.X);
            int y = Math.Max(0, rect.Y);
            int width = Math.Min(rect.Width, input.Width - x);
            int height = Math.Min(rect.Height, input.Height - y);

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var roi = new OpenCvSharp.Rect(x, y, width, height);
            return new Mat(input, roi);
        }
    }
}