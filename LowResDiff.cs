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

        // 差分能量阈值（核心参数）
        private const int DIFF_ENERGY_THRESHOLD = 8000;

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
            // 1. 捕获整屏
            using var fullBmp = new Bitmap(
                bounds.Width,
                bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(fullBmp))
            {
                g.CopyFromScreen(
                    bounds.X, bounds.Y,
                    0, 0,
                    bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }

            // 2. 缩放到低分辨率
            using var smallBmp = new Bitmap(
                DIFF_W, DIFF_H,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            using (var g2 = Graphics.FromImage(smallBmp))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                g2.DrawImage(
                    fullBmp,
                    new Rectangle(0, 0, DIFF_W, DIFF_H));
            }

            // 3. 灰度 + 差分能量
            var currGray = new byte[DIFF_W * DIFF_H];
            int energy = 0;
            int idx = 0;

            for (int y = 0; y < DIFF_H; y++)
            {
                for (int x = 0; x < DIFF_W; x++)
                {
                    var p = smallBmp.GetPixel(x, y);
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

            return energy > DIFF_ENERGY_THRESHOLD;
        }
        #endregion
    }
}

       
