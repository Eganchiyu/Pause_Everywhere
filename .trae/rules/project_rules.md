# Pause Everywhere 项目公约

## 1. Git 规范

### 1.1 分支管理
- `main`: 主分支，保持稳定可发布状态
- `develop`: 开发分支，日常开发在此分支进行
- `feature/*`: 功能分支，从 develop 分支创建
- `fix/*`: 修复分支，从 develop 或 main 分支创建
- `release/*`: 发布分支，准备发布时从 develop 创建

### 1.2 提交规范 (Conventional Commits)
提交信息格式：`<type>(<scope>): <subject>`

**类型 (type)**:
- `feat`: 新功能
- `fix`: 修复 bug
- `docs`: 文档更新
- `style`: 代码格式调整（不影响逻辑）
- `refactor`: 代码重构
- `perf`: 性能优化
- `test`: 测试相关
- `chore`: 构建/工具相关

**示例**:
```
feat(audio): 添加自定义音效播放功能
fix(hotkey): 修复热键重复触发问题
docs(architecture): 更新项目架构文档
```

### 1.3 Gitignore 规则
必须忽略以下内容：
- 构建输出：`bin/`, `obj/`, `Debug/`, `Release/`
- IDE 文件：`.vs/`, `.idea/`, `*.user`
- 临时文件：`*.tmp`, `*.log`
- 敏感信息：`.env`, `*.pfx`
- 依赖包：`packages/`, `node_modules/`

---

## 2. 代码风格规范

### 2.1 命名规范

**文件命名**:
- 使用 PascalCase：`Main.xaml.cs`, `GaussianProcess.cs`
- XAML 文件与代码文件同名：`Main.xaml` + `Main.xaml.cs`

**类命名**:
- 使用 PascalCase：`Main`, `GaussianProcessor`, `BGPreCompute`
- 接口以 `I` 开头：`IAudioEndpointVolume`

**方法命名**:
- 使用 PascalCase：`ProcessBaseBlur()`, `Capture()`
- 事件处理以 `_Click`, `_Changed` 结尾：`SaveButton_Click()`

**变量命名**:
- 私有字段以 `_` 开头：`_preparedMat`, `_frameLock`
- 静态字段以 `_` 开头：`_endpoint`, _prevGray`
- 常量使用 UPPER_SNAKE_CASE：`HOTKEY_ID`, `SCALE_FACTOR`
- 局部变量使用 camelCase：`bounds`, `energy`

**属性命名**:
- 使用 PascalCase：`Opacity`, `IsVisible`

### 2.2 代码格式
- 使用 4 空格缩进
- 大括号换行（Allman 风格）
- 每个文件一个类（XAML 文件除外）
- 使用 `#region` 组织代码段

### 2.3 注释规范
- 公共 API 必须有 XML 文档注释
- 复杂算法添加行内注释
- 使用中文注释说明业务逻辑

---

## 3. 架构规范

### 3.1 模块职责
- **单一职责**: 每个类只负责一个功能
- **依赖方向**: 高层模块不依赖低层模块，都依赖抽象
- **接口隔离**: 使用小而专一的接口

### 3.2 资源管理
- OpenCvSharp `Mat` 对象必须使用 `using` 或手动 `.Dispose()`
- COM 对象需正确释放
- 事件处理器需在关闭时取消注册

### 3.3 线程安全
- 共享资源访问需加锁
- 使用 `volatile` 标志控制任务状态
- UI 更新必须在 UI 线程执行

---

## 4. 文档同步规范

### 4.1 必须同步更新的文档
当代码发生以下变更时，必须同步更新相关文档：

| 变更类型 | 需要更新的文档 |
|---------|---------------|
| 新增/删除模块 | `docs/architecture.md` |
| 修改模块接口 | `docs/architecture.md` |
| 新增/修改功能 | `README.md`, `docs/development_log.md` |
| 重大架构变更 | `docs/architecture.md`, `docs/development_plan.md` |
| 版本发布 | `CHANGELOG.md`, `README.md` |

### 4.2 文档更新检查
- 每次提交前检查文档是否需要更新
- Pull Request 必须包含文档更新说明
- 文档与代码不一致时，优先更新文档

---

## 5. 测试规范

### 5.1 测试覆盖
- 核心算法必须有单元测试
- 边界条件必须有测试用例
- 性能关键路径需有性能测试

### 5.2 测试命名
- 测试方法名：`<方法名>_<场景>_<预期结果>`
- 示例：`ProcessBaseBlur_ValidInput_ReturnsBlurredMat`

---

## 6. 发布规范

### 6.1 版本号
使用语义化版本：`MAJOR.MINOR.PATCH`
- MAJOR: 不兼容的 API 修改
- MINOR: 向下兼容的功能性新增
- PATCH: 向下兼容的问题修正

### 6.2 发布流程
1. 从 develop 创建 release 分支
2. 更新版本号和 CHANGELOG
3. 测试验证
4. 合并到 main 并打标签
5. 合并回 develop

---

## 7. 协作规范

### 7.1 代码审查
- 所有代码变更必须经过审查
- 审查重点：功能正确性、代码风格、安全性
- 至少一人批准后才能合并

### 7.2 沟通规范
- 提交信息清晰描述变更内容
- 复杂变更需在 PR 中说明设计思路
- 问题讨论使用 Issue 追踪
