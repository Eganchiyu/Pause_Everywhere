using OpenCvSharp;
using System;
using System.Diagnostics;
using Pause_Everywhere;

public class Gaussian_processor
{
    // ===== 屏幕捕获与处理相关 =====
    private const double SCALE_FACTOR = 0.1;// 缩放因子（缩小10倍处理）
    public const double BRIGHTNESS_THRESHOLD = 180; // 0~255，越大越“亮”
    // ===== 屏幕捕获与处理相关 =====

    public static Mat Process(Mat input)
    {
        // 转灰度计算平均亮度
        using var gray = new Mat();
        Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);

        // 计算平均亮度
        var meanScalar = Cv2.Mean(gray);
        double avgBrightness = meanScalar.Val0;

        // 更新是否需要变暗
        Main._needDim = avgBrightness > BRIGHTNESS_THRESHOLD;

        Debug.WriteLine($"平均亮度: {avgBrightness}, 需要变暗: {Main._needDim}");

        // 获取原始尺寸
        int w = input.Width;
        int h = input.Height;

        // 计算缩放尺寸
        int smallW = (int)(w * SCALE_FACTOR);
        int smallH = (int)(h * SCALE_FACTOR);

        // 处理图像
        var result = new Mat();
        Cv2.Resize(input, result, new OpenCvSharp.Size(smallW, smallH));
        Cv2.GaussianBlur(result, result, new OpenCvSharp.Size(0, 0), 15);
        Cv2.Resize(result, result, new OpenCvSharp.Size(w, h));

        return result;
    }
}
