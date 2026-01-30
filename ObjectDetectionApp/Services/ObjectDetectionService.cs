using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Image;
using ObjectDetectionApp.Models;

namespace ObjectDetectionApp.Services
{
    /// <summary>
    /// ML.NET 기반 객체 탐지 서비스
    /// </summary>
    public class ObjectDetectionService
    {
        private MLContext _mlContext;
        private ITransformer _trainedModel;
        private PredictionEngine<ImageData, ImagePrediction> _predictionEngine;
        private List<string> _labels;
        private const int ImageSize = 224;

        public ObjectDetectionService()
        {
            _mlContext = new MLContext(seed: 1);
            _labels = new List<string>();
        }

        /// <summary>
        /// 모델 학습
        /// </summary>
        public bool TrainModel(List<TrainingData> trainingDataList)
        {
            try
            {
                if (trainingDataList == null || trainingDataList.Count == 0)
                {
                    Console.WriteLine("TrainModel: trainingDataList is empty");
                    return false;
                }

                // 고유 라벨 추출
                _labels = trainingDataList.Select(x => x.LabelName).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();

                if (_labels.Count < 2)
                {
                    Console.WriteLine("TrainModel: at least two distinct non-empty labels are required for multiclass training.");
                    return false;
                }

                // 학습 데이터 준비
                var imageDataList = new List<ImageData>();

                foreach (var data in trainingDataList)
                {
                    if (data.ImageData == null || data.ImageData.Length == 0)
                        continue;

                    // 임시 파일로 저장 (ML.NET은 파일 경로 필요)
                    string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
                    File.WriteAllBytes(tempPath, data.ImageData);

                    imageDataList.Add(new ImageData
                    {
                        ImagePath = tempPath,
                        Label = data.LabelName
                    });
                }

                if (imageDataList.Count == 0)
                {
                    Console.WriteLine("TrainModel: no valid images to train.");
                    return false;
                }

                var trainingData = _mlContext.Data.LoadFromEnumerable(imageDataList);

                // 전처리 파이프라인: 이미지 로드 -> 리사이즈 -> 픽셀 추출 (Features: Vector<Single>)
                var preprocPipeline = _mlContext.Transforms.LoadImages(outputColumnName: "Image", imageFolder: null, inputColumnName: "ImagePath")
                    .Append(_mlContext.Transforms.ResizeImages(outputColumnName: "Image", imageWidth: ImageSize, imageHeight: ImageSize, inputColumnName: "Image"))
                    .Append(_mlContext.Transforms.ExtractPixels(outputColumnName: "Features", inputColumnName: "Image", interleavePixelColors: true, scaleImage: 1f / 255f));

                // Fit preprocessor and validate schema
                var preprocTransformer = preprocPipeline.Fit(trainingData);
                var transformed = preprocTransformer.Transform(trainingData);

                if (!transformed.Schema.Any(c => c.Name == "Features"))
                {
                    Console.WriteLine("TrainModel: Features column not found after preprocessing.");
                    return false;
                }

                var featuresType = transformed.Schema["Features"].Type;
                if (!(featuresType is VectorDataViewType vectorType) || vectorType.ItemType.RawType != typeof(float))
                {
                    Console.WriteLine($"TrainModel: Features column has unexpected type: {featuresType}. Expected Vector<Single>.");
                    return false;
                }

                // Inspect a sample row to ensure feature length > 0
                using (var cursor = transformed.GetRowCursor(transformed.Schema))
                {
                    var featuresGetter = cursor.GetGetter<VBuffer<float>>(transformed.Schema["Features"]);
                    if (cursor.MoveNext())
                    {
                        VBuffer<float> features = default;
                        featuresGetter(ref features);
                        if (features.Length == 0)
                        {
                            Console.WriteLine("TrainModel: extracted Features vector length is 0.");
                            return false;
                        }
                    }
                }

                // Count examples per label
                var labelCounts = trainingDataList.GroupBy(x => x.LabelName).ToDictionary(g => g.Key, g => g.Count());
                foreach (var kv in labelCounts)
                {
                    Console.WriteLine($"Label '{kv.Key}': {kv.Value} samples");
                }

                // 전체 파이프라인에 레이블 매핑 및 트레이너 추가
                var pipeline = preprocPipeline
                    .Append(_mlContext.Transforms.Conversion.MapValueToKey(outputColumnName: "LabelKey", inputColumnName: "Label"))
                    .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(labelColumnName: "LabelKey", featureColumnName: "Features"))
                    .Append(_mlContext.Transforms.Conversion.MapKeyToValue(outputColumnName: "PredictedLabel", inputColumnName: "PredictedLabel"));

                // 모델 학습
                _trainedModel = pipeline.Fit(trainingData);

                // Prediction Engine 생성
                _predictionEngine = _mlContext.Model
                    .CreatePredictionEngine<ImageData, ImagePrediction>(_trainedModel);

                // 임시 파일 삭제
                foreach (var item in imageDataList)
                {
                    try
                    {
                        if (File.Exists(item.ImagePath))
                        {
                            File.Delete(item.ImagePath);
                        }
                    }
                    catch { }
                }

                return true;
            }
            catch (ArgumentOutOfRangeException aex)
            {
                Console.WriteLine($"ArgumentOutOfRangeException during training: {aex}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"모델 학습 오류: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 객체 탐지
        /// </summary>
        public List<DetectionResult> DetectObjects(byte[] imageData)
        {
            var results = new List<DetectionResult>();

            try
            {
                if (_predictionEngine == null || imageData == null)
                {
                    return results;
                }

                // 임시 파일로 저장
                string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
                File.WriteAllBytes(tempPath, imageData);

                var inputData = new ImageData { ImagePath = tempPath };
                var prediction = _predictionEngine.Predict(inputData);

                // 결과 생성
                if (prediction.Score != null && prediction.Score.Length > 0)
                {
                    var maxScore = prediction.Score.Max();
                    var maxIndex = Array.IndexOf(prediction.Score, maxScore);

                    if (maxIndex >= 0 && maxIndex < _labels.Count)
                    {
                        results.Add(new DetectionResult
                        {
                            Label = _labels[maxIndex],
                            Confidence = maxScore,
                            BoundingBox = new Rectangle(0, 0, 0, 0) // 전체 이미지
                        });
                    }
                }

                // 임시 파일 삭제
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"객체 탐지 오류: {ex}");
            }

            return results;
        }

        /// <summary>
        /// 모델 학습 여부 확인
        /// </summary>
        public bool IsModelTrained()
        {
            return _trainedModel != null && _predictionEngine != null;
        }
    }

    // ML.NET 데이터 클래스
    public class ImageData
    {
        [LoadColumn(0)]
        public string ImagePath { get; set; }

        [LoadColumn(1)]
        public string Label { get; set; }

        public byte[] ImageBytes { get; set; }
    }

    public class ImagePrediction
    {
        [ColumnName("Score")]
        public float[] Score { get; set; }

        [ColumnName("PredictedLabel")]
        public string PredictedLabel { get; set; }
    }
}