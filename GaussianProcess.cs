using OpenCvSharp;
using System;
using System.Diagnostics;
using Pause_Everywhere;

public class Gaussian_processor
{
    private const double SCALE_FACTOR = 0.1;
    public const double BRIGHTNESS_THRESHOLD = 180;

    // 1. 只负责生成干净的模糊底图（无特效）
    public static Mat ProcessBaseBlur(Mat input)
    {
        using var gray = new Mat();
        Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        var meanScalar = Cv2.Mean(gray);
        double avgBrightness = meanScalar.Val0;
        Main._needDim = avgBrightness > BRIGHTNESS_THRESHOLD;

        int w = input.Width;
        int h = input.Height;
        int smallW = (int)(w * SCALE_FACTOR);
        int smallH = (int)(h * SCALE_FACTOR);

        using var smallMat = new Mat();
        Cv2.Resize(input, smallMat, new OpenCvSharp.Size(smallW, smallH));

        using var blurredMat = new Mat();
        Cv2.GaussianBlur(smallMat, blurredMat, new OpenCvSharp.Size(0, 0), 15);

        var result = new Mat();
        Cv2.Resize(blurredMat, result, new OpenCvSharp.Size(w, h));
        
        return result; // 返回干净的模糊底图
    }

    // 2. 专门用于每一帧的高频调用的特效生成器
    public static Mat ApplyDynamicEffects(Mat baseMat, int effectType)
    {
        Mat result = baseMat.Clone();
        int w = result.Width;
        int h = result.Height;

        if (effectType == 0) // 灰度保持静态即可
        {
            using var tempGray = new Mat();
            Cv2.CvtColor(result, tempGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(tempGray, result, ColorConversionCodes.GRAY2BGR);
        }
        else if (effectType == 1) // 激进版：动态 CRT 扫描线 (可以随时间微移)
        {
            unsafe
            {
                byte* ptr = (byte*)result.DataPointer;
                int stride = (int)result.Step();
                int channels = result.Channels();
                
                // 让扫描线随时间滚动，产生 CRT 屏幕闪烁感
                int offset = Environment.TickCount % 6; 

                for (int y = 0; y < h; y++)
                {
                    if ((y + offset) % 6 < 3)
                    {
                        byte* row = ptr + y * stride;
                        for (int x = 0; x < w * channels; x++)
                        {
                            row[x] = (byte)(row[x] * 0.15); // 压暗
                        }
                    }
                }
            }
        }
        else if (effectType == 2) // 激进动态版：赛博故障风 (Glitch)
        {
            using var bgr = new Mat();
            result.CopyTo(bgr);
            Mat[] channels = Cv2.Split(bgr);

            // 核心：引入随机数，让每一帧撕裂的位置和宽度都不同
            Random rnd = new Random();

            using var rChannel = channels[2];
            using var bChannel = channels[0];

            using var rShifted = new Mat();
            using var bShifted = new Mat();
            rChannel.CopyTo(rShifted);
            bChannel.CopyTo(bShifted);

            // 【加码1】增加同时存在的撕裂带数量 (10 到 25条)
            int numBands = rnd.Next(10, 26);

            for (int i = 0; i < numBands; i++)
            {
                int bandY = rnd.Next(0, h);

                // 【加码2】撕裂带变粗！最大高度达到屏幕高度的 1/6 (以前是1/12)
                // 偶尔会出现非常宽的横向断层
                int bandHeight = rnd.Next(10, h / 6);
                if (bandY + bandHeight > h) bandHeight = h - bandY;

                // 【加码3】极端的横向错位！(核心撕裂感来源)
                // 最大偏移量拉到屏幕宽度的 1/6！
                // 如果是 1920 分辨率，一口气能平移 320 个像素，视觉冲击力极强
                int offsetX = rnd.Next(20, w / 6);
                if (offsetX >= w) offsetX = w - 1;

                int channelSelect = rnd.Next(3); // 随机撕裂红或蓝

                if (channelSelect == 0 || channelSelect == 2) // 红通道右移
                {
                    var srcRect = new OpenCvSharp.Rect(0, bandY, w - offsetX, bandHeight);
                    var dstRect = new OpenCvSharp.Rect(offsetX, bandY, w - offsetX, bandHeight);
                    rChannel[srcRect].CopyTo(rShifted[dstRect]);
                }
                if (channelSelect == 1 || channelSelect == 2) // 蓝通道左移
                {
                    var srcRect = new OpenCvSharp.Rect(offsetX, bandY, w - offsetX, bandHeight);
                    var dstRect = new OpenCvSharp.Rect(0, bandY, w - offsetX, bandHeight);
                    bChannel[srcRect].CopyTo(bShifted[dstRect]);
                }
            }

            channels[2].Dispose();
            channels[0].Dispose();
            channels[2] = rShifted;
            channels[0] = bShifted;

            Cv2.Merge(channels, result);
            foreach (var c in channels) c.Dispose();
        }

        return result;
    }
}