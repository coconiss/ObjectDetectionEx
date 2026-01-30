using System;
using System.Drawing;

namespace ObjectDetectionApp.Models
{
    /// <summary>
    /// 학습 데이터를 나타내는 클래스
    /// </summary>
    public class TrainingData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string LabelName { get; set; }
        public byte[] ImageData { get; set; }
        public Rectangle BoundingBox { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public override string ToString()
        {
            return $"{LabelName} - [{BoundingBox.X}, {BoundingBox.Y}, {BoundingBox.Width}, {BoundingBox.Height}]";
        }
    }

    /// <summary>
    /// 카메라 정보를 나타내는 클래스
    /// </summary>
    public class CameraInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return $"카메라 {Index}: {Name}";
        }
    }

    /// <summary>
    /// 객체 탐지 결과를 나타내는 클래스
    /// </summary>
    public class DetectionResult
    {
        public string Label { get; set; }
        public float Confidence { get; set; }
        public Rectangle BoundingBox { get; set; }
    }

    /// <summary>
    /// 애플리케이션 모드
    /// </summary>
    public enum AppMode
    {
        Idle,       // 대기
        Training,   // 학습 모드
        Detection   // 검출 모드
    }
}