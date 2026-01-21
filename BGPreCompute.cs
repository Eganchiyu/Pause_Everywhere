using OpenCvSharp;
using Pause_Everywhere;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;


namespace Pause_Everywhere
{
    public class BGPreCompute
    {
        // ===== 预计算模糊图像相关 =====
        private Mat _preparedMat = new Mat();// 预计算好的模糊图像（OpenCV矩阵）
        private byte[]? _previousScreenHash = null;// 上一帧的屏幕哈希值（用于变化检测）
        private readonly object _frameLock = new();// 线程锁，保护_preparedMat的访问
        private Task? _precomputeTask;// 后台预计算任务
        
        
        private volatile bool _needsRecalculation = true;// 标记需要重新计算模糊

        private const int SKIP_FRAMES = 5;// 跳帧数，降低CPU使用率
        private int _frameCounter = 0;
        // ===== 预计算模糊图像相关 =====


        /// <summary>
        /// 预计算任务主函数
        /// </summary>
        /// <remarks>启动异步循环，监控屏幕变化和程序状态，判定是否预计算。
        /// 该后台任务通过跳帧来降低 CPU 使用率，并在应用程序处理热键或窗口可见时避免重新计算。此方法应在内部调用，
        /// 用于管理预计算任务的生命周期。</remarks>
        public void StartBackgroundPrecompute()
        {
            _precomputeTask = Task.Run(async () =>
            {
                while (Main._precomputeRunning)
                {
                    try
                    {
                        //Debug.WriteLine("开始计算...");
                        // 如果正在处理热键或窗口可见，跳过本次计算
                        if (Main._isProcessingHotkey || Main.IsVisible)
                        {
                            await Task.Delay(200);
                            continue;
                        }

                        //Debug.WriteLine("开始计算...");

                        var bounds = Main._screenBounds;
                        /*
                         * 判断是否需要重新计算模糊图像
                         * 条件1：标记需要重新计算
                         * 条件2：屏幕变化检测（低分辨率差分法）
                         */
                        //Debug.WriteLine("开始计算...");

                        bool needsBlur = _needsRecalculation || LowResDiff.IsScreenChangedLowRes(bounds);


                        // 如果需要，捕获屏幕并计算模糊图像
                        if (needsBlur)
                        {
                            using var mat = CaptureAndProcessScreen(bounds);
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

