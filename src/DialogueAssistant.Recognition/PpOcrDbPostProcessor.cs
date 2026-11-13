using Clipper2Lib;
using OpenCvSharp;

namespace DialogueAssistant.Recognition;

/// <summary>独立实现 PP-OCR DB 检测框筛选、评分、膨胀及坐标映射。</summary>
internal static class PpOcrDbPostProcessor
{
    /// <summary>检测概率图阈值。</summary>
    private const float Threshold = 0.3f;
    /// <summary>候选框平均概率最低阈值。</summary>
    private const float BoxThreshold = 0.6f;
    /// <summary>轮廓处理上限。</summary>
    private const int MaxCandidates = 1000;
    /// <summary>边界框外扩比例。</summary>
    private const float UnclipRatio = 1.5f;
    /// <summary>最小有效边长。</summary>
    private const int MinSize = 3;

    /// <summary>从概率图筛选文字轮廓并将检测框映射回原图尺寸。</summary>
    /// <param name="prediction">单通道检测概率图。</param>
    /// <param name="sourceSize">送入检测模型前的图像尺寸。</param>
    /// <returns>依照左上、右上、右下、左下顺序排列的框点。</returns>
    internal static List<Point2f[]> Process(Mat prediction, Size sourceSize, CancellationToken cancellationToken)
    {
        using var bitmap = new Mat();
        Cv2.Compare(prediction, Threshold, bitmap, CmpType.GT);
        var contours = bitmap.FindContoursAsArray(RetrievalModes.List, ContourApproximationModes.ApproxSimple);
        var boxes = new List<Point2f[]>(Math.Min(contours.Length, MaxCandidates));
        foreach (var contour in contours.Take(MaxCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (contour.Length < 3) continue;
            var (points, shortSide) = MiniBox(contour.Select(point => new Point2f(point.X, point.Y)).ToArray());
            if (shortSide < MinSize || Score(points, prediction) < BoxThreshold) continue;
            var expanded = Unclip(points);
            if (expanded.Count != 1) continue;
            var (expandedBox, expandedShortSide) = MiniBox(expanded[0].Select(point => new Point2f(point.X, point.Y)).ToArray());
            if (expandedShortSide < MinSize + 2) continue;
            var mapped = expandedBox.Select(point => new Point2f(
                    Math.Clamp((float)Math.Round(point.X * sourceSize.Width / prediction.Width), 0, sourceSize.Width - 1),
                    Math.Clamp((float)Math.Round(point.Y * sourceSize.Height / prediction.Height), 0, sourceSize.Height - 1)))
                .ToArray();
            if (SideLength(mapped[0], mapped[1]) > MinSize && SideLength(mapped[0], mapped[3]) > MinSize)
                boxes.Add(mapped);
        }
        return boxes;
    }

    /// <summary>取最小外接旋转矩形的四点，并整理为阅读方向一致的顺序。</summary>
    private static (Point2f[] Points, float ShortSide) MiniBox(Point2f[] contour)
    {
        var rect = Cv2.MinAreaRect(contour);
        var points = rect.Points().OrderBy(point => point.X).ToArray();
        var index1 = points[1].Y > points[0].Y ? 0 : 1;
        var index4 = points[1].Y > points[0].Y ? 1 : 0;
        var index2 = points[3].Y > points[2].Y ? 2 : 3;
        var index3 = points[3].Y > points[2].Y ? 3 : 2;
        return ([points[index1], points[index2], points[index3], points[index4]], Math.Min(rect.Size.Width, rect.Size.Height));
    }

    /// <summary>计算轮廓框内部概率的掩膜平均值。</summary>
    private static float Score(Point2f[] points, Mat prediction)
    {
        var minX = Math.Clamp((int)Math.Floor(points.Min(point => point.X)), 0, prediction.Width - 1);
        var maxX = Math.Clamp((int)Math.Ceiling(points.Max(point => point.X)), 0, prediction.Width - 1);
        var minY = Math.Clamp((int)Math.Floor(points.Min(point => point.Y)), 0, prediction.Height - 1);
        var maxY = Math.Clamp((int)Math.Ceiling(points.Max(point => point.Y)), 0, prediction.Height - 1);
        var polygon = points.Select(point => new Point((int)(point.X - minX), (int)(point.Y - minY))).ToArray();
        using var mask = new Mat(maxY - minY + 1, maxX - minX + 1, MatType.CV_8UC1, Scalar.Black);
        mask.FillPoly([polygon], Scalar.White);
        using var crop = new Mat(prediction, new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
        return (float)crop.Mean(mask).Val0;
    }

    /// <summary>以 Clipper2 按 DB unclip 公式扩张四边形。</summary>
    private static Paths64 Unclip(Point2f[] points)
    {
        var area = Math.Abs(SignedArea(points));
        var perimeter = 0d;
        for (var i = 0; i < points.Length; i++) perimeter += SideLength(points[i], points[(i + 1) % points.Length]);
        if (area <= 0 || perimeter <= 0) return [];
        var path = new Path64(points.Select(point => new Point64((long)point.X, (long)point.Y)));
        return Clipper.InflatePaths([path], area * UnclipRatio / perimeter, JoinType.Round, EndType.Polygon, 2.0, 0.25);
    }

    /// <summary>计算多边形有向面积。</summary>
    private static double SignedArea(Point2f[] points)
    {
        double area = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var next = points[(i + 1) % points.Length];
            area += points[i].X * next.Y - next.X * points[i].Y;
        }
        return area / 2d;
    }

    /// <summary>计算点间欧氏距离。</summary>
    private static double SideLength(Point2f first, Point2f second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
