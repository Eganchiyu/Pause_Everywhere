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
        using var smallMat = new Mat();
        Cv2.Resize(input, smallMat, new OpenCvSharp.Size(smallW, smallH));

        using var blurredMat = new Mat();
        Cv2.GaussianBlur(smallMat, blurredMat, new OpenCvSharp.Size(0, 0), 15);

        var result = new Mat();
        Cv2.Resize(blurredMat, result, new OpenCvSharp.Size(w, h));

        // 应用高级特效
        if (Pause_Everywhere.Properties.Settings.Default.EnableAdvancedEffects)
        {
            int effectType = Pause_Everywhere.Properties.Settings.Default.EffectType;
            if (effectType == 0) // 黑白/灰度化 (Grayscale)
            {
                using var tempGray = new Mat();
                Cv2.CvtColor(result, tempGray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(tempGray, result, ColorConversionCodes.GRAY2BGR);
            }
            else if (effectType == 1) // 老电视/CRT扫描线 (CRT Scanlines)
            {
                unsafe
                {
                    byte* ptr = (byte*)result.DataPointer;
                    int stride = (int)result.Step();
                    int channels = result.Channels();

                    for (int y = 0; y < h; y += 2)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < w * channels; x++)
                        {
                            row[x] = (byte)(row[x] * 0.5); // 调暗偶数行
                        }
                    }
                }
            }
            else if (effectType == 2) // 赛博故障风 (Glitch / RGB Split)
            {
                using var bgr = new Mat();
                result.CopyTo(bgr);
                Mat[] channels = Cv2.Split(bgr);

                int offsetX = 10;

                using var rChannel = channels[2];
                using var bChannel = channels[0];

                // Offset Red channel right
                using var rShifted = new Mat(rChannel.Size(), rChannel.Type(), Scalar.All(0));
                rChannel[new OpenCvSharp.Rect(0, 0, w - offsetX, h)].CopyTo(rShifted[new OpenCvSharp.Rect(offsetX, 0, w - offsetX, h)]);

                // Offset Blue channel left
                using var bShifted = new Mat(bChannel.Size(), bChannel.Type(), Scalar.All(0));
                bChannel[new OpenCvSharp.Rect(offsetX, 0, w - offsetX, h)].CopyTo(bShifted[new OpenCvSharp.Rect(0, 0, w - offsetX, h)]);

                channels[2].Dispose();
                channels[0].Dispose();

                channels[2] = rShifted;
                channels[0] = bShifted;

                Cv2.Merge(channels, result);

                foreach(var c in channels) c.Dispose();
            }
        }

        return result;
    }
}
