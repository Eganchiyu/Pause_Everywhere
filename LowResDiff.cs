//使用低分辨率差分法检测屏幕变化
using System.Diagnostics;

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
        /// <remarks>此方法通过将指定区域缩放到低分辨率并计算灰度值差异，将当前屏幕内容与前一帧进行比较。
        /// 针对显著变化优化，可以通过更改差分能量阈值<see cref="DIFF_ENERGY_THRESHOLD"/>来修改</remarks>
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
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                _reusableFullGraphics = Graphics.FromImage(_reusableFullBmp);

                _reusableSmallBmp = new Bitmap(
                    DIFF_W, DIFF_H,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
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

            // 3. 灰度 + 差分能量
            var currGray = new byte[DIFF_W * DIFF_H];
            int energy = 0;
            int idx = 0;

            for (int y = 0; y < DIFF_H; y++)
            {
                for (int x = 0; x < DIFF_W; x++)
                {
                    var p = _reusableSmallBmp!.GetPixel(x, y);
                    byte gval = (byte)((p.R + p.G + p.B) / 3);
                    currGray[idx] = gval;

                    if (_hasPrevGray)
                    {
                        energy += Math.Abs(gval - _prevGray![idx]);
                    }

                    idx++;
                }
            }

            _prevGray = currGray;
            _hasPrevGray = true;

            Debug.WriteLine($"低分辨率差分能量: {energy}");

            return energy > Main.DIFF_ENERGY_THRESHOLD;
        }
        #endregion
    }
}

       
