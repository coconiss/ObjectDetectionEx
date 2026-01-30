using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using ObjectDetectionApp.Models;

namespace ObjectDetectionApp.Services
{
    /// <summary>
    /// 카메라 관리 서비스
    /// </summary>
    public class CameraService : IDisposable
    {
        private VideoCapture _capture;
        private CancellationTokenSource _cts;
        private bool _isCapturing;

        public event EventHandler<Mat> FrameCaptured;

        /// <summary>
        /// 사용 가능한 카메라 목록 조회
        /// </summary>
        public List<CameraInfo> GetAvailableCameras()
        {
            var cameras = new List<CameraInfo>();

            for (int i = 0; i < 10; i++) // 최대 10개 카메라 검색
            {
                using (var testCapture = new VideoCapture(i))
                {
                    if (testCapture.IsOpened())
                    {
                        cameras.Add(new CameraInfo
                        {
                            Index = i,
                            Name = $"Camera {i}"
                        });
                    }
                }
            }

            return cameras;
        }

        /// <summary>
        /// 카메라 시작
        /// </summary>
        public bool StartCamera(int cameraIndex)
        {
            try
            {
                StopCamera();

                // 시도적으로 카메라를 열되, 연결이 느린 장치에 대비해 여러 번 확인
                _capture = new VideoCapture(cameraIndex);

                int attempts = 0;
                while (!_capture.IsOpened() && attempts < 3)
                {
                    Thread.Sleep(200);
                    attempts++;
                    if (!_capture.IsOpened())
                    {
                        _capture.Release();
                        _capture.Dispose();
                        _capture = new VideoCapture(cameraIndex);
                    }
                }

                if (!_capture.IsOpened())
                {
                    return false;
                }

                // 해상도/프레임 낮춰서 처리 부하 감소
                _capture.Set(VideoCaptureProperties.FrameWidth, 640);
                _capture.Set(VideoCaptureProperties.FrameHeight, 480);
                _capture.Set(VideoCaptureProperties.Fps, 30);

                _cts = new CancellationTokenSource();
                _isCapturing = true;

                // 토큰을 전달하여 안전하게 실행
                Task.Run(() => CaptureLoop(_cts.Token), _cts.Token);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"카메라 시작 오류: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 카메라 중지
        /// </summary>
        public void StopCamera()
        {
            _isCapturing = false;
            try
            {
                _cts?.Cancel();
            }
            catch { }
            _cts?.Dispose();
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }

        /// <summary>
        /// 현재 프레임 캡처
        /// </summary>
        public Mat CaptureFrame()
        {
            if (_capture == null || !_capture.IsOpened())
            {
                return null;
            }

            var frame = new Mat();
            try
            {
                _capture.Read(frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CaptureFrame read error: {ex.Message}");
                frame.Dispose();
                return null;
            }

            return frame.Empty() ? null : frame;
        }

        /// <summary>
        /// 프레임 캡처 루프
        /// </summary>
        private async Task CaptureLoop(CancellationToken token)
        {
            try
            {
                while (_isCapturing && !token.IsCancellationRequested)
                {
                    Mat frame = null;
                    try
                    {
                        frame = CaptureFrame();

                        if (frame != null && !frame.Empty())
                        {
                            // 핸들러가 Clone을 하므로 여기서는 원본을 넘기지 않고 안전하게 Clone을 전달
                            FrameCaptured?.Invoke(this, frame.Clone());
                        }

                        await Task.Delay(33, token); // ~30 FPS
                    }
                    catch (OperationCanceledException)
                    {
                        // 정상 취소
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"프레임 캡처 오류: {ex.Message}");
                    }
                    finally
                    {
                        frame?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 무시
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CaptureLoop fatal error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopCamera();
        }
    }
}