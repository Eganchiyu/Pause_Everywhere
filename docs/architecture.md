# Pause Everywhere 项目架构文档

## 项目概述

Pause Everywhere 是一款 Windows 全局暂停工具，通过热键（Ctrl+Alt+P）一键暂停所有应用程序，并提供模糊遮罩、音视频暂停、系统静音等功能。

---

## 目录结构

```
Pause_Everywhere/
├── Properties/                    # 配置文件目录
│   ├── Settings.Designer.cs       # 自动生成的设置类
│   └── Settings.settings          # 用户设置持久化文件
├── scripts/                       # 脚本工具目录
│   ├── flatten.cmd                # 项目扁平化批处理
│   └── flatten_project_codes.py   # 项目代码扁平化 Python 脚本
├── docs/                          # 项目文档目录
│   ├── architecture.md            # 本文档：项目架构说明
│   ├── development_plan.md        # 开发规划
│   └── development_log.md         # 开发日志
├── .trae/                         # Trae IDE 配置
│   ├── rules/                     # 项目公约规则
│   │   └── project_rules.md       # 项目开发规范
│   └── skills/                    # Trae Skills 配置
│       └── post_commit_check      # 提交后检查技能
├── App.xaml                       # WPF 应用程序入口 XAML
├── App.xaml.cs                    # WPF 应用程序入口代码
├── App.config                     # 应用程序配置
├── AssemblyInfo.cs                # 程序集信息
├── Main.xaml                      # 主窗口 XAML 布局
├── Main.xaml.cs                   # 主窗口核心逻辑
├── SettingsWindow.xaml            # 设置窗口 XAML 布局
├── SettingsWindow.xaml.cs         # 设置窗口交互逻辑
├── Audio.cs                       # 音频控制模块
├── BGPreCompute.cs                # 后台预计算模块
├── CapScreen.cs                   # 屏幕截图模块
├── GaussianProcess.cs             # 高斯模糊与特效处理模块
├── LowResDiff.cs                  # 低分辨率差分检测模块
├── Pause_Everywhere.csproj        # C# 项目文件
├── Pause_Everywhere.slnx          # 解决方案文件
├── .gitignore                     # Git 忽略规则
├── .gitattributes                 # Git 属性配置
└── README.md                      # 项目说明文档
```

---

## 模块架构详解

### 1. 主窗口模块 (Main)

**文件**: `Main.xaml`, `Main.xaml.cs`

**职责**: 应用程序的核心控制中心，负责热键注册、窗口显示/隐藏、音视频控制、UI 更新。

**关键组件**:
- **热键系统**: 使用 Windows API `RegisterHotKey` 注册全局热键 (Ctrl+Alt+P)
- **消息循环**: 通过 `WndProc` 处理 `WM_HOTKEY` 消息
- **动态特效定时器**: 30FPS 定时器用于渲染动态视觉效果
- **系统托盘**: `NotifyIcon` 提供右键菜单（设置、退出）

**核心流程**:
```
热键触发 → 显示窗口 → 获取预计算图像 → 应用特效 → 暂停音视频 → 静音系统
热键再次触发 → 隐藏窗口 → 恢复音视频 → 恢复音量
```

**关键参数**:
- `DIM_OPACITY`: 暗化层透明度
- `DIFF_ENERGY_THRESHOLD`: 屏幕变化检测阈值
- `isMute`: 是否静音
- `dim_flag`: 是否启用暗化层

---

### 2. 屏幕截图模块 (CapScreen)

**文件**: `CapScreen.cs`

**职责**: 捕获指定屏幕区域的图像。

**接口**:
```csharp
public static Mat Capture(Rectangle bounds)
```

**实现**:
- 使用 `System.Drawing.Graphics.CopyFromScreen` 捕获屏幕
- 返回 OpenCvSharp `Mat` 对象（BGR 格式）
- 调用方负责释放返回的 `Mat`

**注意事项**:
- 捕获的图像为 `Format24bppRgb` 格式
- 需要处理多显示器场景

---

### 3. 高斯模糊与特效模块 (GaussianProcess)

**文件**: `GaussianProcess.cs`

**职责**: 生成模糊底图并应用动态视觉特效。

**核心方法**:

#### `ProcessBaseBlur(Mat input)`
- 将输入图像缩放到 10% 分辨率
- 应用高斯模糊（核大小 15x15）
- 恢复到原始尺寸
- 判断亮度是否需要暗化层（阈值 180）

#### `ApplyDynamicEffects(Mat baseMat, int effectType)`
- **效果 0**: 灰度化（静态）
- **效果 1**: CRT 扫描线（动态，30FPS 滚动）
- **效果 2**: 赛博故障风 Glitch（动态，随机撕裂带）

**性能优化**:
- 使用 `unsafe` 指针直接操作内存
- 动态特效每帧生成，底图缓存复用

---

### 4. 后台预计算模块 (BGPreCompute)

**文件**: `BGPreCompute.cs`

**职责**: 后台持续监控屏幕变化，预计算模糊图像。

**核心机制**:
- 启动异步任务循环监控
- 使用 `LowResDiff` 检测屏幕变化
- 变化时捕获屏幕并计算模糊图像
- 缓存结果供热键触发时使用

**线程安全**:
- 使用 `_frameLock` 保护 `_preparedMat` 访问
- 使用 `volatile` 标志控制任务状态

**跳帧策略**:
- `SKIP_FRAMES = 7`: 每 7 帧处理一次，降低 CPU 使用率

---

### 5. 低分辨率差分检测模块 (LowResDiff)

**文件**: `LowResDiff.cs`

**职责**: 检测屏幕是否发生显著变化。

**算法**:
1. 将屏幕缩放到 64x36 低分辨率
2. 转换为灰度图
3. 与上一帧计算差分能量
4. 能量超过阈值则认为屏幕变化

**性能优化**:
- 复用 `Bitmap` 和 `Graphics` 对象
- 使用 `unsafe` 指针遍历像素
- 缓存上一帧灰度数据

**关键参数**:
- `DIFF_W = 64`, `DIFF_H = 36`: 低分辨率尺寸
- `DIFF_ENERGY_THRESHOLD`: 变化检测阈值（从设置读取）

---

### 6. 音频控制模块 (Audio.cs)

**文件**: `Audio.cs`

**职责**: 控制系统音频静音/取消静音，播放自定义音效。

#### SystemAudio 类
- 使用 COM 接口 `IAudioEndpointVolume` 控制系统音量
- 静态构造函数初始化音频端点
- `SetMute(bool mute)`: 设置静音状态

#### CustomAudioPlayer 类
- 使用 `MediaPlayer` 播放自定义音效
- `PlayStartSound()`: 播放暂停开始音效
- `PlayEndSound()`: 播放暂停结束音效

**COM 接口**:
- `IAudioEndpointVolume`: 音频端点音量控制
- `IMMDeviceEnumerator`: 音频设备枚举
- `IMMDevice`: 音频设备接口

---

### 7. 设置窗口模块 (SettingsWindow)

**文件**: `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`

**职责**: 提供用户配置界面，管理应用设置。

**配置项**:
- **通用**: 暗化透明度、变化阈值、静音、暗化层
- **音频**: 启用音效、音效文件路径
- **图像**: 启用图像覆盖、图像路径、填充模式、透明度
- **文字**: 启用文字覆盖、文字内容、字号、颜色
- **特效**: 启用高级特效、特效类型

**持久化**:
- 使用 `Properties.Settings.Default` 保存/读取
- 点击保存按钮立即生效

---

## 数据流

```
┌─────────────────────────────────────────────────────────────┐
│                        屏幕内容                              │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  LowResDiff (低分辨率差分检测)                                │
│  - 缩放到 64x36                                              │
│  - 灰度转换                                                  │
│  - 差分能量计算                                              │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  BGPreCompute (后台预计算)                                   │
│  - 监控屏幕变化                                              │
│  - 触发模糊计算                                              │
│  - 缓存结果                                                  │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  GaussianProcess (高斯模糊与特效)                             │
│  - ProcessBaseBlur: 生成模糊底图                              │
│  - ApplyDynamicEffects: 应用动态特效                          │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Main (主窗口)                                               │
│  - 热键触发                                                  │
│  - 显示模糊遮罩                                              │
│  - 暂停音视频                                                │
│  - 静音系统                                                  │
└─────────────────────────────────────────────────────────────┘
```

---

## 依赖关系

```
Main
├── BGPreCompute
│   ├── CapScreen
│   ├── GaussianProcess
│   └── LowResDiff
├── CapScreen
├── GaussianProcess
├── Audio
│   ├── SystemAudio (COM)
│   └── CustomAudioPlayer
└── SettingsWindow
    └── Properties.Settings
```

---

## 技术栈

- **框架**: .NET 8.0, WPF
- **图像处理**: OpenCvSharp
- **音频控制**: Windows Core Audio API (COM)
- **UI**: WPF XAML
- **构建**: MSBuild

---

## 注意事项

1. **内存管理**: OpenCvSharp 的 `Mat` 对象需要手动释放，使用 `using` 或 `.Dispose()`
2. **线程安全**: 预计算模块与主窗口共享 `_preparedMat`，需加锁访问
3. **性能**: 动态特效使用 `unsafe` 指针优化，需确保指针操作安全
4. **COM 接口**: 音频控制使用 COM 接口，需正确初始化和释放
5. **热键冲突**: 全局热键可能与其他应用冲突，需考虑热键自定义

---

## 未来扩展方向

1. 多显示器支持优化
2. 自定义热键配置
3. 更多视觉特效
4. 插件系统
5. 国际化支持
