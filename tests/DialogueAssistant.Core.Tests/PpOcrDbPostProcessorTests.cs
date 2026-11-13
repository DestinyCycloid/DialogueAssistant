using DialogueAssistant.Recognition;
using OpenCvSharp;

namespace DialogueAssistant.Core.Tests;

/// <summary>验证独立 PP-OCRv6 DB 后处理器的置信度过滤、框扩张与点序。</summary>
public sealed class PpOcrDbPostProcessorTests
{
    /// <summary>从高置信矩形概率区生成一个扩张且顺序稳定的文本框。</summary>
    [Fact]
    public void Process_HighConfidenceRectangle_ReturnsExpandedOrderedBox()
    {
        using var prediction = CreateProbabilityMap(1.0);

        var results = PpOcrDbPostProcessor.Process(prediction, prediction.Size(), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(4, result.Length);
        Assert.True(SideLength(result[0], result[1]) > 50 || SideLength(result[0], result[3]) > 50);
        Assert.True(SideLength(result[0], result[1]) > 20 && SideLength(result[0], result[3]) > 20);
        Assert.True(result[0].X <= result[1].X);
        Assert.True(result[0].Y <= result[3].Y);
    }

    /// <summary>低于 DB 文本框阈值的概率区域不返回候选框。</summary>
    [Fact]
    public void Process_ScoreBelowBoxThreshold_ReturnsEmpty()
    {
        using var prediction = CreateProbabilityMap(0.5);

        var results = PpOcrDbPostProcessor.Process(prediction, prediction.Size(), CancellationToken.None);

        Assert.Empty(results);
    }

    /// <summary>构造包含指定像素概率矩形的单通道概率图。</summary>
    private static Mat CreateProbabilityMap(double probability)
    {
        var prediction = new Mat(100, 100, MatType.CV_32FC1, Scalar.Black);
        using var textRegion = prediction[new Rect(10, 20, 50, 20)];
        textRegion.SetTo(new Scalar(probability));
        return prediction;
    }

    /// <summary>计算两个点间的欧氏距离。</summary>
    private static double SideLength(Point2f first, Point2f second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
