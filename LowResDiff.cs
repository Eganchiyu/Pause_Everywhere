using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace Pause_Everywhere
{
    public static class LowResDiff
    {
        #region 屏幕变化检测相关参数和状态变量
        // ===== 屏幕变化检测（低分辨率差分） =====
        private const int DIFF_W = 64;
        private const int DIFF_H = 36;

        // 上一帧灰度
        private static byte[]? _prevGray = null;
        private static bool _hasPrevGray = false;

        // 重用对象以减少内存分配
        private static Bitmap? _reusableFullBmp = null;
        private static Graphics? _reusableFullGraphics = null;
        private static Bitmap? _reusableSmallBmp = null;
        private static Graphics? _reusableSmallGraphics = null;
        private static Rectangle _lastBounds = Rectangle.Empty;

        // ===== 屏幕变化检测（低分辨率差分） =====
        #endregion

        #region 低分辨率差分法实现方法 传入bounds，返回真值
        /// <summary>
        /// 低分辨率差分法
        /// </summary>
        /// <remarks>此方法通过将指定区域缩放到低分辨率并计算灰度值差异，将当前屏幕内容与前一帧进行比较。</remarks>
        /// <param name="bounds">目标屏幕区域，以屏幕坐标系中的矩形指定。</param>
        /// <returns>指定边界内是否发生显著变化</returns>
        public static bool IsScreenChangedLowRes(Rectangle bounds)
        {
            // 如果屏幕区域发生变化，重新分配复用的资源
            if (_lastBounds != bounds)
            {
                _reusableFullGraphics?.Dispose();
                _reusableFullBmp?.Dispose();
                _reusableSmallGraphics?.Dispose();
                _reusableSmallBmp?.Dispose();

                _reusableFullBmp = new Bitmap(
                    bounds.Width,
                    bounds.Height,
                    PixelFormat.Format24bppRgb);
                _reusableFullGraphics = Graphics.FromImage(_reusableFullBmp);

                _reusableSmallBmp = new Bitmap(
                    DIFF_W, DIFF_H,
                    PixelFormat.Format24bppRgb);
                _reusableSmallGraphics = Graphics.FromImage(_reusableSmallBmp);
                _reusableSmallGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;

                _lastBounds = bounds;
            }

            // 1. 捕获整屏
            _reusableFullGraphics!.CopyFromScreen(
                bounds.X, bounds.Y,
                0, 0,
                bounds.Size,
                CopyPixelOperation.SourceCopy);

            // 2. 缩放到低分辨率
            _reusableSmallGraphics!.DrawImage(
                _reusableFullBmp!,
                new Rectangle(0, 0, DIFF_W, DIFF_H));

            // 3. 灰度 + 差分能量 (使用传统指针高效遍历)
            var currGray = new byte[DIFF_W * DIFF_H];
            int energy = 0;
            int idx = 0;

            var rect = new Rectangle(0, 0, DIFF_W, DIFF_H);
            var data = _reusableSmallBmp!.LockBits(rect, ImageLockMode.ReadOnly, _reusableSmallBmp.PixelFormat);
            
            try
            {
                unsafe
                {
                    // 获取图像首地址指针
                    byte* ptr = (byte*)data.Scan0;
                    
                    // 经典的双层 for 循环遍历像素
                    for (int y = 0; y < DIFF_H; y++)
                    {
                        // 计算当前行的起始内存地址，必须乘以 Stride（字节对齐后的行宽）
                        byte* row = ptr + (y * data.Stride);
                        
                        for (int x = 0; x < DIFF_W; x++)
                        {
                            // Format24bppRgb 格式在内存中是 B G R 倒序排列的
                            byte b = row[x * 3];
                            byte g = row[x * 3 + 1];
                            byte r = row[x * 3 + 2];

                            // 简单求平均得到灰度值
                            byte gval = (byte)((r + g + b) / 3);
                            currGray[idx] = gval;

                            // 如果有上一帧的数据，计算能量差
                            if (_hasPrevGray)
                            {
                                energy += Math.Abs(gval - _prevGray![idx]);
                            }

                            idx++;
                        }
                    }
                }
            }
            finally
            {
                // 确保无论如何都会解锁内存
                _reusableSmallBmp.UnlockBits(data);
            }

            _prevGray = currGray;
            _hasPrevGray = true;

            Debug.WriteLine($"低分辨率差分能量: {energy}");

            // 请确保你的 Main 类中定义了 DIFF_ENERGY_THRESHOLD 这个公开的常量或静态变量
            return energy > Main.DIFF_ENERGY_THRESHOLD;
        }
        #endregion
    }
}