using System.Drawing;
using Yolov5Net.Scorer;

namespace DetectPeople.YOLOv5Net
{
    public class ObjectDetectResult
    {
        private readonly float[] BBox;

        public ObjectDetectResult(YoloPrediction x)
        {
            Id = x.Label.Id;
            BBox = new float[] { x.Rectangle.X, x.Rectangle.Y, x.Rectangle.Right, x.Rectangle.Bottom };
            Label = x.Label.Name;
            Confidence = x.Score;
        }

        /// <summary>
        /// Confidence level.
        /// </summary>
        public float Confidence { get; }

        public int Id { get; }

        /// <summary>
        /// The Bbox category.
        /// </summary>
        public string Label { get; }

        public Rectangle GetRectangle()
        {
            var x1 = (int)BBox[0];
            var y1 = (int)BBox[1];
            var x2 = (int)BBox[2];
            var y2 = (int)BBox[3];
            var H = y2 - y1;
            var W = x2 - x1;

            return new Rectangle(x1, y1, W, H);
        }
    }
}
