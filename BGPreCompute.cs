using OpenCvSharp;
using System.Diagnostics;

namespace Pause_Everywhere
{
    public class BGPreCompute
    {
        #region 参数和状态变量
        // ===== 预计算模糊图像相关 =====
        public static Mat _preparedMat = new Mat();// 预计算好的模糊图像（OpenCV矩阵）
        private byte[]? _previousScreenHash = null;// 上一帧的屏幕哈希值（用于变化检测）
        public static readonly object _frameLock = new();// 线程锁，保护_preparedMat的访问
        public static Task? _precomputeTask;// 后台预计算任务
        
        
        private static volatile bool _needsRecalculation = true;// 标记需要重新计算模糊

        private const int SKIP_FRAMES = 5;// 跳帧数，降低CPU使用率
        private static int _frameCounter = 0;
        // ===== 预计算模糊图像相关 =====
        #endregion、

        /// <summary>
        /// 预计算进程主函数
        /// </summary>
        /// <remarks>启动异步循环，监控屏幕变化和程序状态，判定是否预计算。</remarks>
        public static void StartBackgroundPrecompute()
        {
            _precomputeTask = Task.Run(async () =>
            {
                while (Main._precomputeRunning)
                {
                    try
                    {
                        // 如果正在处理热键或窗口可见，跳过本次计算
                        if (Main._isProcessingHotkey || Main.WindowIsVisible)
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        var bounds = Main._screenBounds;

                        #region 重新计算模糊图像的条件判断 needsBlur(bool)
                        /*
                         * 判断是否需要重新计算模糊图像
                         * 条件1：标记需要重新计算
                         * 条件2：屏幕变化检测（低分辨率差分法）
                         */
                        //Debug.WriteLine("开始计算...");

                        bool needsBlur = _needsRecalculation || LowResDiff.IsScreenChangedLowRes(bounds);
                        #endregion

                        // 如果需要，捕获屏幕并计算模糊图像
                        if (needsBlur)
                        {
                            using var mat = Gaussian_processor.Process(CapScreen.Capture(bounds));
                            lock (_frameLock)
                            {
                                _preparedMat?.Dispose();
                                _preparedMat = mat.Clone();
                            }
                            Debug.WriteLine("======最终结果：重新渲染======");
                            _needsRecalculation = false;
                        }
                        else
                        {
                            Debug.WriteLine("======最终结果：复用图像======");
                        }

                        await Task.Delay(100);

                        // 跳帧处理，降低CPU使用率
                        _frameCounter++;
                        if (_frameCounter < SKIP_FRAMES)
                        {
                            await Task.Delay(100);
                            continue;
                        }
                        _frameCounter = 0;
                    }
                    catch
                    {
                        Debug.WriteLine("预计算过程中出现异常，继续下一次循环");
                        await Task.Delay(100);
                    }
                }
            });
        }
    }

}

