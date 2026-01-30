using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenCvSharp;
using ObjectDetectionApp.Models;

namespace ObjectDetectionApp.Services
{
    /// <summary>
    /// OpenCV 템플릿 매칭 기반 객체 탐지 서비스
    /// ML.NET보다 안정적이고 간단함
    /// </summary>
    public class ObjectDetectionService
    {
        private Dictionary<string, List<Mat>> _templates; // 라벨별 템플릿 목록
        private const double MatchThreshold = 0.6; // 매칭 임계값
        private ImageProcessingService _imageProcessing;

        public ObjectDetectionService()
        {
            _templates = new Dictionary<string, List<Mat>>();
            _imageProcessing = new ImageProcessingService();
        }

        /// <summary>
        /// 모델 학습 - 템플릿 저장
        /// </summary>
        public bool TrainModel(List<TrainingData> trainingDataList)
        {
            try
            {
                Console.WriteLine("\n=== Template Matching Training Started ===");

                if (trainingDataList == null || trainingDataList.Count == 0)
                {
                    Console.WriteLine("ERROR: No training data provided");
                    return false;
                }

                // 기존 템플릿 정리
                foreach (var templates in _templates.Values)
                {
                    foreach (var template in templates)
                    {
                        template?.Dispose();
                    }
                }
                _templates.Clear();

                int totalAdded = 0;
                int skipped = 0;

                foreach (var data in trainingDataList)
                {
                    if (data.ImageData == null || data.ImageData.Length == 0)
                    {
                        Console.WriteLine("SKIP: null/empty image data");
                        skipped++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(data.LabelName))
                    {
                        Console.WriteLine("SKIP: empty label name");
                        skipped++;
                        continue;
                    }

                    try
                    {
                        using (var originalMat = _imageProcessing.ByteArrayToMat(data.ImageData))
                        {
                            if (originalMat == null || originalMat.Empty())
                            {
                                Console.WriteLine($"SKIP: failed to convert to Mat for {data.LabelName}");
                                skipped++;
                                continue;
                            }

                            // 바운딩 박스 검증
                            if (data.BoundingBox.Width < 10 || data.BoundingBox.Height < 10)
                            {
                                Console.WriteLine($"SKIP: bounding box too small ({data.BoundingBox.Width}x{data.BoundingBox.Height})");
                                skipped++;
                                continue;
                            }

                            // 범위 검증 및 조정
                            int x = Math.Max(0, data.BoundingBox.X);
                            int y = Math.Max(0, data.BoundingBox.Y);
                            int width = Math.Min(data.BoundingBox.Width, originalMat.Width - x);
                            int height = Math.Min(data.BoundingBox.Height, originalMat.Height - y);

                            if (width < 10 || height < 10)
                            {
                                Console.WriteLine($"SKIP: adjusted box too small ({width}x{height})");
                                skipped++;
                                continue;
                            }

                            var adjustedBox = new Rectangle(x, y, width, height);

                            using (var croppedMat = _imageProcessing.CropImage(originalMat, adjustedBox))
                            {
                                if (croppedMat == null || croppedMat.Empty())
                                {
                                    Console.WriteLine($"SKIP: failed to crop for {data.LabelName}");
                                    skipped++;
                                    continue;
                                }

                                // 템플릿 저장 (그레이스케일로 변환하여 저장)
                                var grayTemplate = new Mat();
                                if (croppedMat.Channels() == 3)
                                {
                                    Cv2.CvtColor(croppedMat, grayTemplate, ColorConversionCodes.BGR2GRAY);
                                }
                                else
                                {
                                    grayTemplate = croppedMat.Clone();
                                }

                                // 라벨별 템플릿 리스트에 추가
                                if (!_templates.ContainsKey(data.LabelName))
                                {
                                    _templates[data.LabelName] = new List<Mat>();
                                }

                                _templates[data.LabelName].Add(grayTemplate);
                                totalAdded++;

                                Console.WriteLine($"✓ Added template: {data.LabelName} ({width}x{height})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR processing {data.LabelName}: {ex.Message}");
                        skipped++;
                    }
                }

                Console.WriteLine($"\n=== Training Summary ===");
                Console.WriteLine($"Total input: {trainingDataList.Count}");
                Console.WriteLine($"Templates added: {totalAdded}");
                Console.WriteLine($"Skipped: {skipped}");
                Console.WriteLine($"\nTemplates by label:");
                foreach (var kvp in _templates)
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value.Count} template(s)");
                }

                if (_templates.Count == 0)
                {
                    Console.WriteLine("\nERROR: No valid templates created");
                    return false;
                }

                Console.WriteLine("\n=== Training Success ===\n");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n!!! Training Failed !!!");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// 객체 탐지 - 템플릿 매칭 사용
        /// </summary>
        public List<DetectionResult> DetectObjects(byte[] imageData)
        {
            var results = new List<DetectionResult>();

            try
            {
                if (_templates.Count == 0 || imageData == null)
                {
                    return results;
                }

                using (var sourceImage = _imageProcessing.ByteArrayToMat(imageData))
                {
                    if (sourceImage == null || sourceImage.Empty())
                    {
                        return results;
                    }

                    // 그레이스케일 변환
                    var graySource = new Mat();
                    if (sourceImage.Channels() == 3)
                    {
                        Cv2.CvtColor(sourceImage, graySource, ColorConversionCodes.BGR2GRAY);
                    }
                    else
                    {
                        graySource = sourceImage.Clone();
                    }

                    var detections = new List<(string label, double confidence, Rectangle box)>();

                    // 각 라벨의 템플릿으로 매칭 시도
                    foreach (var labelTemplates in _templates)
                    {
                        string label = labelTemplates.Key;

                        foreach (var template in labelTemplates.Value)
                        {
                            try
                            {
                                if (template == null || template.Empty())
                                    continue;

                                // 템플릿이 소스 이미지보다 크면 스킵
                                if (template.Width > graySource.Width || template.Height > graySource.Height)
                                {
                                    continue;
                                }

                                // 템플릿 매칭 수행
                                using (var result = new Mat())
                                {
                                    Cv2.MatchTemplate(graySource, template, result, TemplateMatchModes.CCoeffNormed);

                                    // 최대값 찾기
                                    Cv2.MinMaxLoc(result, out double minVal, out double maxVal, out OpenCvSharp.Point minLoc, out OpenCvSharp.Point maxLoc);

                                    if (maxVal >= MatchThreshold)
                                    {
                                        var matchRect = new Rectangle(
                                            maxLoc.X,
                                            maxLoc.Y,
                                            template.Width,
                                            template.Height
                                        );

                                        detections.Add((label, maxVal, matchRect));
                                        Console.WriteLine($"Match found: {label} at ({maxLoc.X}, {maxLoc.Y}) with confidence {maxVal:P0}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error matching template for {label}: {ex.Message}");
                            }
                        }
                    }

                    graySource.Dispose();

                    // NMS 적용하여 중복 제거
                    var filteredDetections = ApplyNMS(detections);

                    foreach (var detection in filteredDetections)
                    {
                        results.Add(new DetectionResult
                        {
                            Label = detection.label,
                            Confidence = (float)detection.confidence,
                            BoundingBox = detection.box
                        });
                    }

                    if (results.Count > 0)
                    {
                        Console.WriteLine($"Total detections: {results.Count}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Detection error: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Non-Maximum Suppression
        /// </summary>
        private List<(string label, double confidence, Rectangle box)> ApplyNMS(
            List<(string label, double confidence, Rectangle box)> detections,
            float iouThreshold = 0.3f)
        {
            if (detections.Count == 0)
                return new List<(string label, double confidence, Rectangle box)>();

            var sorted = detections.OrderByDescending(d => d.confidence).ToList();
            var keep = new List<(string label, double confidence, Rectangle box)>();

            while (sorted.Count > 0)
            {
                var current = sorted[0];
                keep.Add(current);
                sorted.RemoveAt(0);

                sorted = sorted.Where(d =>
                {
                    // 다른 라벨은 유지
                    if (d.label != current.label)
                        return true;

                    float iou = CalculateIoU(current.box, d.box);
                    return iou < iouThreshold;
                }).ToList();
            }

            return keep;
        }

        /// <summary>
        /// IoU 계산
        /// </summary>
        private float CalculateIoU(Rectangle box1, Rectangle box2)
        {
            int x1 = Math.Max(box1.Left, box2.Left);
            int y1 = Math.Max(box1.Top, box2.Top);
            int x2 = Math.Min(box1.Right, box2.Right);
            int y2 = Math.Min(box1.Bottom, box2.Bottom);

            int intersectionWidth = Math.Max(0, x2 - x1);
            int intersectionHeight = Math.Max(0, y2 - y1);
            int intersectionArea = intersectionWidth * intersectionHeight;

            int box1Area = box1.Width * box1.Height;
            int box2Area = box2.Width * box2.Height;
            int unionArea = box1Area + box2Area - intersectionArea;

            return unionArea > 0 ? (float)intersectionArea / unionArea : 0;
        }

        /// <summary>
        /// 모델 학습 여부 확인
        /// </summary>
        public bool IsModelTrained()
        {
            return _templates.Count > 0;
        }

        /// <summary>
        /// 리소스 정리
        /// </summary>
        public void Dispose()
        {
            foreach (var templates in _templates.Values)
            {
                foreach (var template in templates)
                {
                    template?.Dispose();
                }
            }
            _templates.Clear();
        }
    }
}