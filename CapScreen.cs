using OpenCvSharp;
using System;


namespace Pause_Everywhere
{
    public class CapScreen
    {
        #region 捕获屏幕区域的方法 传入bounds，返回Mat
        /// <summary>
        /// 捕获并处理指定屏幕区域的图像
        /// </summary>
        /// <remarks>
        /// 图像处理包括调整大小、高斯模糊和恢复原尺寸，用于降噪或减少细节。
        /// 调用方负责在使用完毕后释放返回的<see cref="Mat"/>对象。
        /// </remarks>
        /// <param name="bounds">要捕获的屏幕区域（屏幕坐标系）。</param>
        /// <returns>处理后的图像<see cref="Mat"/>对象。</returns>
        /// <summary>
        /// 捕获指定屏幕区域的图像
        /// </summary>
        /// <param name="bounds">要捕获的屏幕区域（屏幕坐标系）。</param>
        /// <returns>捕获的屏幕图像<see cref="Mat"/>对象，调用方负责释放。</returns>
        public static Mat Capture(Rectangle bounds)
        {
            int w = bounds.Width;
            int h = bounds.Height;

            // 捕获屏幕
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new System.Drawing.Size(w, h));

            // 转换为Mat并返回
            return OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp);
        }
        #endregion
    }
}
