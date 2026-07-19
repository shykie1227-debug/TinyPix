# TinyPix 4.0 Pencil → WinUI 3 控件映射规范

> 状态：设计冻结输入文档
> 适用平台：Windows 10 22H2、Windows 11，x64
> 设计源：`design/TinyPix-4.0.pen`
> 设计导出：`design/exports/1200x800`、`design/exports/900x600`

## 1. 固定技术边界

- 语言与运行时：C#、.NET 10 LTS。
- UI：WinUI 3、Windows App SDK 2.2.0。
- MVVM：CommunityToolkit.Mvvm，只使用 `ObservableObject`、`ObservableProperty`、`RelayCommand`、`AsyncRelayCommand` 和消息机制等基础能力。
- 发布模型：Unpackaged、self-contained、Windows x64 便携目录；运行时不依赖系统安装 .NET、Windows App SDK Runtime、WebView2 或第三方 UI 运行时。
- 架构：单进程模块化单体；UI 通过应用服务调用本地媒体引擎，不建立 HTTP、WebSocket、Web API 或独立后端服务。
- UI 技术禁区：禁止 WebView、HTML/CSS、JavaScript UI、第三方 UI 框架、在线字体、在线图标、在线预览和运行时资源下载。
- 控件原则：先使用 WinUI 3 原生控件；设计差异优先通过 `Style`、`ThemeResource` 和组合型 `UserControl` 实现。只有原生视觉树无法表达的交互才使用自定义 `ControlTemplate`。
- 数据绑定：页面与固定 ViewModel 优先使用编译期检查的 `x:Bind`；数据模板、动态资源和可替换内容使用 `Binding`。命令统一暴露为 `ICommand`，耗时命令使用 `IAsyncRelayCommand`。
- 线程规则：媒体解析、预览生成、OCR、PDF 渲染和文件扫描不得占用 UI 线程；ViewModel 仅在 UI Dispatcher 上发布可观察状态。
- 依赖事实源：PDF、模型、OCR 和媒体引擎的版本、哈希、目录、并发及许可证门禁以 `design/DEPENDENCY-BASELINE.md` 为准；本文件把冻结依赖映射到 UI、运行与验收，不允许实现时自由替换。
- 原文件规则：拖入、浏览、预览和处理均不改名、不移动、不覆盖源文件。应用不会为历史、数据库或内部存档复制原媒体；用户明确执行导出、转换、保存或 `file.rename` 时生成的文件属于用户请求的输出，不属于“内部复制原媒体”。这些输出只能写入用户指定目录，其中 `file.rename` 仍按“创建重命名副本”规则执行。预览中间物只能写入 `Cache`。

### 1.1 输出路径不可变门禁

- 所有输出工具共用一个输出层校验器；在创建临时文件、打开输出流或调用媒体引擎前，先规范化全部输入路径和候选输出路径。
- Windows 路径比较按不区分大小写处理，并解析绝对路径、`.`/`..`、长路径前缀、符号链接/目录联接可解析的最终目标；候选输出只要与**任一输入文件**指向同一路径或同一文件标识，就无条件拒绝任务。
- “拒绝源路径覆盖”是不可绕过的业务规则：不显示“仍然覆盖”按钮，`ContentDialog` 不能解除，命令行/重试/历史任务也不能绕过。
- 候选输出与非输入的既有文件冲突时，才允许按工具规则执行“改名保存”或由 `ContentDialog` 确认覆盖；确认结果仍须再次通过输入同路径门禁。
- `file.rename` 的正式语义是“创建重命名副本”：读取每个源文件，在用户指定输出目录创建具有新名称的输出副本；不调用源文件 rename/move，不删除源文件，不覆盖源文件，也不默认写回源目录。部分失败时保留已成功输出并列出失败项。

### 1.2 Unpackaged self-contained 部署映射

正式 `TinyPix.App.csproj` 和 Release 发布配置必须锁定以下属性：

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
  <OutputType>WinExe</OutputType>
  <WindowsPackageType>None</WindowsPackageType>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <PublishSingleFile>false</PublishSingleFile>
  <WindowsAppSdkBootstrapInitialize>false</WindowsAppSdkBootstrapInitialize>
  <WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>
</PropertyGroup>
```

- `WindowsPackageType=None` 固定无 MSIX 包身份；不得调用要求包身份的安装、更新、文件关联、后台任务或通知 API。确需平台能力时，必须先检测包身份并使用无包身份可用的 Win32/Windows App SDK 等效路径。
- 本产品是 **unpackaged + self-contained**。Windows App SDK 框架内容随应用发布，因此不使用面向 framework-dependent 部署的 `Bootstrap.Initialize`/`MddBootstrapInitialize` 去寻找系统已安装 Runtime；固定启用 `WindowsAppSdkUndockedRegFreeWinRTInitialize` 自动初始化注册无关 WinRT。阶段 C 必须验证自动初始化失败会在主窗口业务服务启动前形成可诊断日志和阻断提示，不允许静默退回系统 Runtime。
- `PublishSingleFile=false` 是正式发布门禁；发布物保留完整目录，不启用自解压、首次运行释放依赖或临时目录运行。
- `dotnet publish -c Release -r win-x64 --self-contained true` 的输出必须包含 Windows App SDK 自包含文件和所有 `win-x64` native 资产。构建清单逐项核对 PDFium、SkiaSharp native、OpenCvSharpExtern、ONNX Runtime、SQLite native、FFmpeg/FFprobe 及其他 RID 资产，缺少任一强制文件即禁止生成 ZIP。
- 所有运行路径以 `AppContext.BaseDirectory` 为唯一根解析：`Engines/`、`Models/`、`Templates/`、`Licenses/`、`Config/`、`Data/`、`Cache/`、`Logs/`；不得依赖当前工作目录、用户 PATH、注册表或系统安装位置。
- 启动时先验证便携标志、目录资产、引擎/模型哈希和 `Config/Data/Cache/Logs` 的创建与写入能力。软件目录不可写时显示阻断页，禁用所有处理命令并提示将完整目录移动到普通用户可写位置；不得静默改写 `%AppData%`、`%LocalAppData%` 或注册表。
- 发布验收必须在未安装 .NET、Windows App SDK Runtime、WebView2、Python、FFmpeg 或数据库的全新普通用户 Windows 10 22H2 x64 与 Windows 11 x64 VM 中进行；断开网络、解压 ZIP、直接运行 `TinyPix.exe`，确认主窗口真实显示、自动初始化成功、引擎/模型自检通过、目录写入策略正确且 Process Monitor 无应用主动注册表写入。

## 2. App Shell 与页面骨架

### 2.1 主窗口

| Pencil 区域 | WinUI 实现 | 绑定与行为 | 无障碍与键盘 | 模板策略 |
|---|---|---|---|---|
| 主窗口 | `Window` + `AppWindow` + HWND 消息钩子 | 初始客户区 1200×800；记忆窗口尺寸但不写注册表；通过 `WM_GETMINMAXINFO` 或经阶段 C 证明等效可靠的 Win32 最小跟踪尺寸机制，阻止客户区小于 900×600 | 窗口标题为“TinyPix 4.0”；Alt+F4 关闭；关闭时有运行任务则用 `ContentDialog` 确认 | 不自定义 `Window` 模板；消息钩子只负责尺寸约束 |
| 顶部分类 | `NavigationView`，`PaneDisplayMode="Top"` | `SelectedItem` ↔ `SelectedCategory`；固定“图片工具 / 视频工具 / 工具箱”；设置入口打开模态设置弹窗，不改变 `SelectedCategory` | 每项设置 `AutomationProperties.Name`；方向键切换；Enter/Space 激活；Ctrl+, 打开设置；Alt+Home 回到当前分类首项 | 仅样式化选中背景和间距，不替换 `ControlTemplate` |
| 品牌与标题区 | 首选 `NavigationView.PaneHeader` + `TextBlock`；固定回退为 NavigationView 外同一顶栏中的独立原生 `Grid`/`TextBlock` Header | 阶段 C 在 1200×800、900×600、100%/125%/150% 显示缩放、200% 文本缩放及中英文下验证 Top 模式是否稳定显示且不挤压分类；任一场景失败即采用固定回退，品牌区置于顶栏首列，NavigationView 置于第二列 | 品牌文本不进入重复 Tab 顺序；窗口名由自动化根节点提供；回退不改变 Tab/读屏顺序 | 不自定义 NavigationView 模板；不存在第三种临时方案 |
| 离线状态 | `InfoBar` 或紧凑 `Border + SymbolIcon + TextBlock` | 固定显示“完全离线”；引擎自检异常时切换为警告状态 | `AutomationProperties.Name="完全离线，本地处理"`；状态变化用 `AutomationProperties.LiveSetting="Polite"` | 紧凑显示采用组合控件，不改原生模板 |
| 页面内容 | `Frame` | `NavigationService` 按工具 ID 导航；同模板工具复用页面和 ViewModel，不为每个工具复制 XAML | 导航后焦点移到页面标题；页面标题为 HeadingLevel 1 | 不自定义 |

主窗口只使用一个 `Window`。预览、设置、历史和任务均在同一窗口内完成；不创建浮动工具窗。标题栏保留 Windows 原生标题栏和系统按钮，避免自绘拖拽区、缩放与高对比兼容问题。最小尺寸实现通过 HWND 子类化/消息挂钩处理 `WM_GETMINMAXINFO`：根据当前 DPI 将 900×600 客户区换算成含非客户区的最小跟踪尺寸，写入 `MINMAXINFO.ptMinTrackSize`，窗口销毁时解除钩子；VisualStateManager 不承担窗口尺寸限制。

### 2.2 四区工作台

工作台根节点使用两行三列 `Grid`：第一行为主要工作区，第二行为任务队列；列依次为文件栏、预览/编辑区、参数栏。任务队列跨三列。每列独立管理滚动，根页面禁止水平滚动。

```text
Row 0: 184–224px 文件栏 | MinMax 中央区 | 280–340px 参数栏
Row 1:                    Task Queue（跨 3 列）
```

- 文件栏：`Grid` 内部使用 `ListView`，由它拥有纵向滚动。
- 中央区：普通工作台使用 `Grid`；编辑画布按模板选择 `Canvas` 或独立 `ScrollViewer`。
- 参数栏：`ScrollViewer` 包裹单个垂直 `StackPanel`，参数卡内部不再嵌套纵向滚动控件。
- 任务队列：`ListView` 横向不滚动，任务行随宽度拉伸；大量任务依靠内建虚拟化。
- 三栏分隔使用 `Grid` 列边界和单像素 `Border`；禁止“外卡片包内卡片”的重复表面。

## 3. 25 个 Pencil 可复用组件映射

### 3.1 操作与输入组件

| # | Pencil 组件 | WinUI 原生控件/组合 | 关键绑定属性 | AutomationProperties 与键盘 | 状态视觉 | 自定义 `ControlTemplate` |
|---:|---|---|---|---|---|---|
| 1 | `Button/Primary` | `Button` | `Command`、`CommandParameter`、`IsEnabled`；默认动作由页面级 `KeyboardAccelerator` 和焦点策略表达 | `Name` 使用动作+对象，如“开始输出视频”；Tab 聚焦；Enter/Space 执行；不得仅用图标命名 | Normal/Hover/Pressed/Disabled/Focused/Loading；背景使用 `action-primary`，文字和功能图标统一使用 `on-action-primary`，禁止写死白色、黑色或酸橙色 | 否；使用共享 `Style` 设置圆角、最小高度和主题资源 |
| 2 | `Button/Secondary` | `Button` | 同主按钮；用于浏览、重置、管理等非主动作 | 明确 `Name`；Tab、Enter、Space | 原生状态；透明/浅色表面、`control-border` 功能边框和焦点环 | 否 |
| 3 | `Button/Danger` | `Button` | `Command`、`IsEnabled`；删除、清空等动作执行前由命令触发确认 | `Name` 明确影响范围，如“清空全部缓存”；键盘与按钮一致 | 红色只用于危险动作；Disabled 不以透明度作为唯一差异 | 否；危险色来自主题字典 |
| 4 | `Button/Icon` | `Button` + `FontIcon`/`SymbolIcon` | `Command`、`ToolTipService.ToolTip` | 必须设置可读 `Name`；Tab 聚焦；Enter/Space；工具提示不能替代无障碍名 | 44×44 最小命中区；Hover/Pressed/Focused 清晰 | 否；图标来自 Segoe Fluent Icons 本地字形 |
| 5 | `Input/Text` | `TextBox` | `Text` 双向绑定、`Header`、`PlaceholderText`、`MaxLength`、`IsReadOnly`、`Description` | Label 作为 `Header`；Ctrl+A/C/V/X；错误通过 `Description` 和邻近 `InfoBar` 朗读 | Normal/PointerOver/Focused/Disabled/Invalid；功能边界使用 `control-border`，错误不只靠红色 | 否 |
| 6 | `Input/Number` | `NumberBox` | `Value` 双向绑定、`Minimum`、`Maximum`、`SmallChange`、`LargeChange`、`SpinButtonPlacementMode`、`NumberFormatter` | `Name` 含单位；上下键微调；PageUp/PageDown 大步进；允许直接输入 | 原生数值状态；越界显示明确文本，不移动布局 | 否 |
| 7 | `Input/Select` | `ComboBox` | `ItemsSource`、`SelectedItem`/`SelectedValue`、`DisplayMemberPath`、`Header`、`Description` | Alt+Down 展开；上下键选择；Esc 关闭；选项名称可被 Narrator 读取 | 原生展开、聚焦、禁用状态；选中项不能仅靠颜色 | 否 |
| 8 | `Control/Switch` | `ToggleSwitch` | `IsOn` 双向绑定、`Header`、`OnContent`、`OffContent`、`IsEnabled` | Space 切换；`Name` 描述设置对象；状态由 Toggle 模式自动暴露 | On/Off/PointerOver/Pressed/Disabled/Focused；必须同时有文字状态 | 否 |
| 9 | `Input/PathPicker` | 组合型 `UserControl`：只读 `TextBox` + `Button` | `Path`、`PickerKind`、`FileTypeFilter`、`Command`、`ValidationMessage` | Tab 顺序先路径后“浏览”；路径框支持 Ctrl+C；按钮支持 Enter/Space；组名说明用途 | 路径不可写时显示错误图标、文本与焦点定位 | 否；仅组合原生控件，不创建新输入模板 |

### 3.2 进度、卡片与反馈组件

| # | Pencil 组件 | WinUI 原生控件/组合 | 关键绑定属性 | AutomationProperties 与键盘 | 状态视觉 | 自定义 `ControlTemplate` |
|---:|---|---|---|---|---|---|
| 10 | `Status/Progress` | `ProgressBar` + `TextBlock` | `Value`、`Minimum=0`、`Maximum=100`、`IsIndeterminate`、`StatusText` | `Name` 为任务名；`ItemStatus` 报告“处理中 68%”；进度更新使用 `LiveSetting="Polite"` 并节流 | Waiting 为不确定进度；Running 为确定进度；Success/Failed 由邻近状态文本承担 | 否；只通过主题资源设置酸橙色进度色 |
| 11 | `Card/File` | `ListViewItem` 数据模板：`Grid` + `Image` + 文本 + `Button` | `FileName`、`Path`、`Thumbnail`、`Metadata`、`Status`、`IsSelected`、`RemoveCommand` | 整项 `Name` 包含文件名、类型、状态；上下键移动；Space 选择；Delete 请求移除；上下文菜单支持 Shift+F10 | Selected/PointerOver/Focused/Disabled；等待、成功、失败同时有图标和文字 | 否；使用 `ListViewItem` 容器样式，不替换选择行为 |
| 12 | `Card/Task` | `ListViewItem` 数据模板 + `ProgressBar` + 取消 `Button` + `MenuFlyout` | `JobName`、`State`、`Progress`、`CanCancel`、`CancelCommand`、`RetryCommand`、`OpenOutputCommand` | 整项报告任务状态；Tab 可进入取消/菜单；Esc 不自动取消；Delete 不取消任务 | Queued/Running/Cancelling/Success/Failed/Interrupted；每种状态含文本和图标 | 否 |
| 13 | `Group/Parameters` | `Expander` + `StackPanel`；固定核心组可用 `Grid` + Heading | `Header`、`IsExpanded`、参数 ViewModel | Header 设置 HeadingLevel 2；Enter/Space 展开；Tab 进入子控件 | Expanded/Collapsed/Focused；错误数量可在 Header 文本显示 | 否；原生 `Expander` 足够 |
| 14 | `Card/Preset` | `GridView` + `GridViewItem` 数据模板；较复杂布局可用 `ItemsRepeater` | `ItemsSource`、`SelectedItem`、`SelectCommand`、`IsCustom` | 方向键在卡片间移动；Space/Enter 选择；`Name` 包含预设名和关键参数 | Selected 使用 `control-border`、图标和文字；PointerOver/Focused/Disabled | 否；卡片视觉通过 ItemContainerStyle |
| 15 | `InfoBar/Success` | `InfoBar`，`Severity="Success"` | `IsOpen`、`Title`、`Message`、`ActionButton` | `Name` 组合标题和正文；完成状态 `LiveSetting="Polite"`；Tab 可达动作按钮 | 成功图标+文字+系统成功色 | 否 |
| 16 | `InfoBar/Warning` | `InfoBar`，`Severity="Warning"` | 同上 | 警告出现时不抢焦点；关键磁盘警告使用 Polite 朗读 | 警告图标+文字+系统警告色 | 否 |
| 17 | `InfoBar/Error` | `InfoBar`，`Severity="Error"` | `ErrorCode` 仅用于可复制详情，不作为主文案 | `LiveSetting="Assertive"` 仅用于阻断性失败；提供“重新选择/重试/查看详情”按钮 | 错误图标+文字+系统错误色，禁止仅红框 | 否 |
| 18 | `InfoBar/Offline` | `InfoBar`，`Severity="Informational"`，不可关闭 | 固定离线说明、引擎状态 | `Name="完全离线，文件仅在本机处理"`；通常不进入 Tab 顺序 | 普通状态使用中性/成功图标；异常由 Warning/Error 替换 | 否 |
| 19 | `State/Empty` | 组合型 `UserControl`：`FontIcon` + Heading + 说明 + `Button` | `Title`、`Description`、`PrimaryCommand`、`AcceptsMultiple` | 组名描述可接受输入；主按钮进入正常 Tab 顺序；拖放区本身不伪装成按钮 | Empty/DragOver/Disabled；DragOver 必须有边框、图标和文字变化 | 否；组合控件通过 `VisualStateManager` 切换外观 |

### 3.3 Shell、导航与命令组件

| # | Pencil 组件 | WinUI 原生控件/组合 | 关键绑定属性 | AutomationProperties 与键盘 | 状态视觉 | 自定义 `ControlTemplate` |
|---:|---|---|---|---|---|---|
| 20 | `Shell/LeftFilePanel` | `UserControl`：标题区 + `Button` + `TabView` 或两个 `ToggleButton` + `ListView` + 引擎状态 | `Files`、`SelectedFile`、`ActiveListMode`、`AddFilesCommand`、`EngineHealth` | 先“添加文件”，再列表模式，再文件项；Ctrl+O 添加；Delete 请求移除；F6 移到中央区 | 空/有文件/多文件/源文件丢失/引擎异常 | 否；模式切换优先 `TabView`，设计需紧凑时用两个原生 `ToggleButton` |
| 21 | `Shell/RightParameterPanel` | `UserControl`：`ScrollViewer` + 参数 `StackPanel` + 底部主按钮 | `ParameterGroups`、`HasChanges`、`ResetCommand`、`ExecuteCommand`、`ValidationSummary` | 页面标题后按视觉顺序遍历参数；Alt+R 重置参数；Ctrl+Enter 执行；首个错误自动聚焦 | Default/Modified/Invalid/Busy/Disabled；Busy 时参数不可编辑但取消仍可用 | 否 |
| 22 | `Shell/TaskQueue` | `UserControl`：标题/计数 + `ListView` + 批量命令 `CommandBar` | `Jobs`、`ActiveCount`、`IsExpanded`、`CancelAllCommand`、`ClearCompletedCommand` | F6 移入/移出队列；上下键选任务；Ctrl+J 展开并聚焦队列；任务状态可朗读 | Collapsed/Expanded/Empty/Running/PartialFailure | 否；队列行复用 `Card/Task` |
| 23 | `Navigation/ToolEntry` | `NavigationViewItem`；页面内次级工具列表使用 `ListViewItem` | `Content`、`Icon`、`Tag=ToolId`、`IsSelected`、`IsEnabled` | `Name` 为工具名；方向键导航；Enter/Space 激活；禁用项说明原因 | Selected/PointerOver/Pressed/Disabled/Focused | 否 |
| 24 | `Header/PageTitle` | `Grid` + `StackPanel` + `TextBlock` + 可选 `Button` | `Title`、`Subtitle`、`HelpCommand`、`FitToWindowCommand` | 标题 `HeadingLevel=Level1`；副标题不重复标题；帮助按钮有明确名称 | 默认、参数已修改、只读等状态由副标题/InfoBar 表达 | 否 |
| 25 | `CommandBar/Editor` | `CommandBar` + `AppBarButton`、`AppBarToggleButton`、`AppBarSeparator` | Undo/Redo/Crop/Rotate/Delete/Zoom/Fit 等命令及 `IsEnabled` | 原生左右键与 Tab 行为；每项有 `Label`、`Icon`、`KeyboardAccelerator`；溢出项仍可键盘访问 | Normal/PointerOver/Pressed/Checked/Disabled/Focused；窄屏使用原生溢出 | 否；仅设样式和 DynamicOverflow，不拼装自定义工具条 |

## 4. 八类页面模板映射

### 4.1 模板 1：转换/压缩工作台

适用：视频输出、视频压缩、格式转换、GIF、音频提取、图片批处理等以“输入 → 参数 → 输出”为主的工具。

- 根布局：四区工作台。
- 文件区：`Shell/LeftFilePanel`，支持多选拖放和 `FileOpenPicker`。
- 预览区：视频优先 `MediaPlayerElement`；图片使用 `Image`；无法原生播放的视频显示本地 FFmpeg 生成的缩略图、时长和“可继续处理”说明。固定深色媒体舞台使用 `media-stage`，时间码、刻度和功能图标使用 `on-media`，禁止继承页面 `ink`。
- 预设区：`GridView`；在 900×600 隐藏预设卡片，改为右栏首个 `ComboBox`。
- 参数区：原生 `ComboBox`、`NumberBox`、`Slider`、`ToggleSwitch`、路径选择器。
- 任务区：`Shell/TaskQueue`。
- 自定义边界：不自定义媒体播放器模板；播放控制若设计要求极简，使用 `MediaPlayerElement.AreTransportControlsEnabled=false` 后由 `CommandBar` 调用 `MediaPlayer` API，但必须补齐播放、暂停、定位、音量和键盘语义。

### 4.2 模板 2：时间轴编辑工作台

适用：唯一“视频剪辑”路由中的时间剪辑、分割、合并拼接、字幕/水印时段、倍速区间等单轨编辑。`video.trim`、`video.split`、`video.merge` 保持独立 Handler，但共享一个 Page/ViewModel 壳层，通过编辑模式装配不同命令与参数组。

- 播放器：`MediaPlayerElement` 与 ViewModel 的播放位置同步；Space 播放/暂停，J/K/L 后退/暂停/前进，左右键按固定步长移动，Shift+左右键精细移动。
- 时间轴：组合型 `UserControl`，内部为水平 `ScrollViewer` + `Canvas`；缩略图轨、音频波形和标记轨分别绘制。使用本地 FFmpeg 生成缩略图和波形数据；轨道文字、时间码和刻度统一引用 `on-media`。
- 坐标合同：缩略图、波形、片段、入点、出点和播放头使用同一源时间坐标系，`x = (timestamp / duration) * trackWidth`；缩略图、波形和片段轨不得重叠。入点/出点用带 I/O 标签的系统高亮标记，播放头使用独立前景；入点前、出点后显示“范围外”。空闲态不得高亮“分割”。
- 入点、出点和播放头：可聚焦的 `Thumb` 不属于 WinUI 3 原生公开控件时，使用 `Slider` 表达单点位置；双端范围使用两个可聚焦 `Slider` 叠放在同一轨道语义中，避免引入第三方 RangeSlider。
- 片段列表/合并排序：`ListView`，支持键盘 Alt+Up/Alt+Down 调整顺序；拖拽排序只是补充方式。
- 命令：使用 `CommandBar`，Undo/Redo、设置入点/出点、分割、排除/恢复、缩放、适应窗口均有 `KeyboardAccelerator`。时间轴聚焦时绑定 Space、J/K/L、I/O、B、Delete、Ctrl+Z/Ctrl+Y、`+`/`-`；文本输入聚焦时暂停字符快捷键。导出模式固定为“关键帧优先”和“精确到帧”；后者在 UI 明示“精确边界自动重编码”。
- 单轨片段状态：保留片段与排除区间使用不同填充、描边和文字状态共同表达；不能仅靠绿色/灰色区分。Delete 执行可撤销的排除/恢复，不调用文件删除。
- 范围定位：左/右为 1 秒，Shift+左/右为逐帧，Ctrl+左/右为关键帧；对应命令必须可由屏幕阅读器读出当前时间、片段编号、起止和保留状态。
- 自定义边界：允许自定义时间轴 `UserControl` 的绘制和命中测试；不替换 `Slider`、`Button`、`CommandBar` 的基础交互模板。
- 明确禁止：多轨、Ripple/Roll 编辑、转场、关键帧动画和工程文件；合并排序继续使用独立 `ListView`。

### 4.3 模板 3：图片编辑工作台

适用：裁剪、缩放、旋转、翻转、调色、水印、EXIF 清理、拼图和联系表。

- 图片显示：`ScrollViewer` + `Grid` + `Image`，缩放时保持图像与叠加层同一变换。
- 裁剪与叠加：`Canvas` 覆盖在 `Image` 上方；裁剪框为自定义组合 `UserControl`，四角/边缘手柄均可键盘聚焦和用方向键移动，Shift+方向键大步移动。
- 调整参数：原生 `Slider`、`NumberBox`、`ToggleSwitch` 和 `ComboBox`；数值与滑块双向同步。
- 编辑命令：`CommandBar`；Ctrl+Z/Ctrl+Y 撤销/重做，R 旋转，F 适应窗口，Delete 删除选中叠加对象。
- 预览策略：参数变更先产生内存或缓存预览，绝不写回源文件；应用前后对比使用 `ToggleButton`，不创建网页式滑动组件。
- 自定义边界：仅裁剪框、选区手柄、文字/水印边界框需要自定义 `UserControl`；图像、滚动、缩放命令继续使用原生控件。

### 4.4 模板 4：智能证件照工作台

适用：一寸、二寸、护照、签证、背景替换、构图校验和五/六寸排版。

- 模板选择：`GridView` 绑定本地模板集合；卡片的自动化名称包含名称、毫米尺寸、像素、DPI。
- 主预览：`Image` + `Canvas`，绘制脸部框、头顶线、眼位线、下巴线、安全边距和裁切框；检测结果不合格时在画布外用 `InfoBar` 给出可操作原因。
- 背景：预设颜色使用 `RadioButtons` 或 `GridView` 单选；自定义色使用 Windows 原生 `ColorPicker`，颜色值同时显示为可编辑十六进制文本。
- 校验结果：`ListView` 显示人脸数量、侧脸、分辨率、头部比例、边距、DPI；每项有图标、文字和数值，不只显示红绿颜色。
- 输出：原图、透明结果、标准照、排版照使用 `CheckBox` 多选；输出路径使用路径选择器。
- 人脸定位固定使用 OpenCV Zoo `face_detection_yunet_2023mar.onnx`：只承担人脸框、置信度、双眼中心/鼻尖/双嘴角共 5 点、单/多/无人脸判定和基础构图提示。不得把 5 点结果宣称为 68/106 点精细关键点、高精度头部姿态、活体检测、生物识别或身份判断；侧脸、遮挡、极小脸必须显示“请人工确认”。
- YuNet 资产固定为 `Models/Face/face_detection_yunet_2023mar.onnx`，SHA-256 `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4`，许可证为 MIT，文本位于 `Licenses/YuNet/LICENSE-MIT.txt`。启动和每次模型服务初始化都校验哈希；缺失或不符时禁用证件照人脸定位并显示阻断性 Error `InfoBar`。
- 背景移除固定使用 `modnet_photographic_portrait_matting.onnx`：只承担人像 alpha matte、透明 PNG 和背景替换，不承担人脸识别或身份推断。输入固定 `1×3×512×512`、batch 1、`CPUExecutionProvider`，全进程只创建并复用 1 个 ONNX Runtime session，禁止并发 MODNet session。
- MODNet 资产固定为 `Models/Portrait/modnet_photographic_portrait_matting.onnx`，SHA-256 `07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9`，许可证为 Apache-2.0，文本位于 `Licenses/MODNet/LICENSE-APACHE-2.0.txt`。哈希或许可证缺失即禁止进入发行包和运行该能力。
- CPU/内存边界：YuNet 复用 1 个 detector，检测图长边最大 1280px，单次预算 128 MiB、硬上限 256 MiB，轻任务最多 2 个并发；MODNet 属于媒体重任务，单次预算 512 MiB、硬上限 1 GiB，原图上限 50 MP，只保留一份原图并用缩放副本推理，输出按行/块合成。内存不足时取消当前推理、释放中间缓冲并显示“模型内存不足”，不得让界面无响应。
- 隐私边界：人脸框、5 点坐标和 alpha matte 仅用于当前本地任务，可存在于内存或可清理临时缓存；不写入历史数据库，不保存人脸特征模板，不建立身份库，不跨文件匹配人员，不上传、不遥测。任务完成/取消后释放内存，缓存按统一清理规则删除。
- 异常：无人脸、多人、侧脸、遮挡、低分辨率、内存不足、模型缺失和哈希不符分别使用明确 `InfoBar`；需要人工确认时不静默生成“合格”结论。
- 自定义边界：证件照辅助线画布是组合 `UserControl`；YuNet/MODNet 推理、哈希和内存门禁完全在后台模型服务中，控件不包含推理逻辑。

### 4.5 模板 5：PDF 页面管理工作台

适用：PDF 合并、拆分、排序、旋转、水印、图片互转。

- 页面栅格化只允许 `PDFtoImage 5.2.1 + PDFium 147.0.7690 + SkiaSharp 3.119.2`。`PDFtoImage` 负责托管调用，PDFium 负责页面渲染，SkiaSharp 承接位图；不存在 `Windows.Data.Pdf` fallback，也禁止 WebView 或浏览器 PDF 阅读器。
- PDF 职责固定分离：PDFsharp 只负责合并、拆分、排序、旋转、水印及 PDF 写入/编辑；PdfPig 只负责文本与结构提取；二者都不得替代 PDFium 做通用页面栅格化。
- 渲染和写入按单页串行：同一时刻最多栅格化 1 页，PDFsharp 构建/写入输出时也逐页处理；默认预览 150 DPI，导出默认不超过 300 DPI。每页完成绑定或编码后立即释放 `SKBitmap`、像素缓冲、流和 PDFium 页面句柄，不把整份 PDF 的所有页留在内存。
- 单页工作集预算 256 MiB、硬上限 512 MiB；超过 60 MP 的页面拒绝当前 DPI 并要求降低。扫描 PDF OCR 与缩略图必须复用同一 PDFium 单页串行管线，不能另建渲染路径。
- 固定资产与许可证目录：`Engines/PDFium/pdfium.dll`、`Licenses/PDFtoImage/LICENSE-MIT.txt`、`Licenses/SkiaSharp/LICENSE-MIT.txt`、`Licenses/PDFium/LICENSE`、`Licenses/PDFium/third-party/*`。`pdftoimage.5.2.1.nupkg` SHA-256 固定为 `0db8fd1ec91d711842ef61d7140c8900fc0555d9a7850d932256b703adb7d099`，PDFium `chromium/7690` x64 归档 SHA-256 固定为 `06ef95ac4f9b8897731224639ddf0f185693cb48bc9ee650f1e92f71e0d2a94e`，`pdfium.dll` SHA-256 固定为 `15df9dddd81eddc5a177946aa5e34cda821ebc46a51440ecb607f91e99644895`；完整第三方许可证目录、版本或哈希任一缺失即禁止发布和启动 PDF 能力。
- 页面缩略图：`GridView`；大量页面保持虚拟化，不放入外层纵向 `ScrollViewer`。
- 页面排序：拖动只是增强；键盘 Alt+Up/Alt+Down 调序，Home/End 跳首尾，Delete 进入删除确认。
- 页面选择：`SelectionMode="Multiple"`，Ctrl/Shift 按 Windows 习惯多选；操作范围在右栏以文字明确显示。
- 主预览：单页 `ScrollViewer` + `Image`，支持 Ctrl+滚轮缩放、Ctrl+0 适应、Ctrl++/Ctrl+- 缩放。
- 操作命令：旋转、删除、提取、排序恢复使用 `CommandBar`。候选输出若与任一输入同路径，输出层直接阻断且不可确认覆盖；只在覆盖非输入既有输出文件时使用 `ContentDialog`。
- 自定义边界：页缩略图和页画布均为原生集合/图像控件组合，不开发 PDF 排版编辑器；加密 PDF 只在本地请求密码且不持久化。

### 4.6 模板 6：OCR/二维码识别工作台

适用：图片 OCR、扫描 PDF OCR、二维码和条形码识别。

- 左侧输入：文件 `ListView`；PDF 展示页级子项。
- 中央预览：`Image` + `Canvas` 显示识别框；选择结果行时高亮对应框。
- 结果文本：`TextBox`，`AcceptsReturn=true`、`TextWrapping=Wrap`、`IsReadOnly` 由模式决定；Ctrl+A/C 可复制。
- 结构结果：`ListView` 或 `DataGrid` 不可用时使用表头 `Grid` + 虚拟化 `ListView`，不引入第三方表格控件。
- 二维码结果：类型、文本、纠错级别和区域以 `ListView` 呈现；“复制内容”“保存文本”使用 `CommandBar`。
- 识别进度：`ProgressBar`；逐页完成状态通过任务队列和 `InfoBar` 展示。
- 扫描 PDF OCR 前处理强制复用模板 5 的 PDFtoImage/PDFium/SkiaSharp 单页串行栅格化：150 DPI 预览、OCR 目标按规则不高于 300 DPI，每页渲染、识别、落盘后立即释放；禁止 `Windows.Data.Pdf` 和第二套 PDF 渲染器。
- 自定义边界：识别框画布为组合 `UserControl`；OCR 文本编辑只做校对和导出，不实现富文本或 Office 编辑。

### 4.7 模板 7：生成器与文件工具工作台

适用：二维码/条形码生成、创建重命名副本、校验和、颜色转换、剪贴板图片保存和元数据工具；不提供重复文件检测。

- 生成器表单：原生 `TextBox`、`NumberBox`、`ComboBox`、`ColorPicker`、`ToggleSwitch`；预览使用 `Image`。
- 二维码生成：多行 `TextBox` 或 UTF-8 TXT 输入，ZXing.Net 在本地生成实时二维码预览；输出格式为 PNG/SVG。纠错级别使用 `RadioButtons` 或四个互斥 `ToggleButton` 表达 L/M/Q/H 离散单选，不得使用连续质量滑块；预览、执行按钮和任务项使用同一个二维码语义图标，不得使用播放图标。
- `file.rename`/创建重命名副本：两列 `Grid` 数据模板显示源名称与输出副本名称；必须选择输出目录。执行时只创建新文件，不对源执行 rename/move/delete，不默认写回源目录；冲突、非法字符、重名以及候选输出与任一输入同路径均以行内图标+文字阻断。源路径同名冲突没有“仍然覆盖”操作。
- 校验和：只读 `TextBox` 显示 SHA-256/MD5，提供复制按钮；等宽字体使用本地 `Consolas` 回退。
- 不映射归档压缩/解压控件；媒体压缩继续由视频、图片和 PDF 模板各自承担。
- 自定义边界：二维码预览、颜色样本仅是原生 `Image`/`Border` 组合；不开发脚本控制台、不嵌入终端。

### 4.8 模板 8：设置弹窗、历史和缓存管理

适用：设置、处理历史、批量任务、缓存、许可证和离线安全说明。

- 设置弹窗：使用应用根 `XamlRoot` 上的自定义 `ContentDialog` 内容；模态遮罩覆盖完整 `XamlRoot`，包括顶部导航。标准宽度约 820，左侧 `ListView` 分类；900×600 使用约 720×488 的紧凑布局和顶部分类。设置不复用媒体预览、参数栏或任务队列。
- 唯一实现：只创建一个 `SettingsDialog`、一个 `SettingsDialogViewModel` 和一个 `SaveSettingsCommand`；标准、紧凑和高对比由同一视觉树的 `VisualStateManager` 与主题资源表达，不复制 XAML 页面或保存逻辑。
- 生命周期：顶部设置项与 Ctrl+, 调用同一命令；打开时保存触发控件，关闭/取消/Esc 后返回触发设置的控件；弹窗期间焦点被限制在弹窗内，背景不可交互。
- High Contrast：选中分类使用系统 `Highlight` / `HighlightText`；离线安全说明使用 `Window` / `WindowText` 中性样式；开关同时显示开/关文字，保存按钮和所有可操作项保留系统焦点矩形。
- 设置项：标题、说明和原生输入控件组成两列 `Grid`；紧凑布局改为单列。保存先调用 `ValidateSettingsUseCase`，失败时保持弹窗并聚焦首个错误；成功后原子写入 `settings.json` 再关闭。
- 历史：虚拟化 `ListView`，支持本地搜索 `AutoSuggestBox`、状态筛选 `ComboBox`、打开输出目录和手动重试；只显示路径与记录，不展示原媒体副本。
- 缓存：`ProgressBar`/数值文本显示占用；“清理缓存”使用危险按钮和 `ContentDialog`，明确不会删除原文件或输出文件。
- 许可证：`ListView` 展示组件名、版本、许可证和本地许可证文件；详情用只读 `TextBox`/`RichTextBlock`，所有链接只显示文本或复制地址，不在应用内联网打开。
- 离线说明：不可关闭的 `InfoBar` + `RichTextBlock`，明确无上传、无遥测、无自动更新、不写注册表。
- 自定义边界：设置项不引入 Toolkit SettingsControls；使用原生控件组合即可。

## 5. 文件拖放、选择器与本地预览

### 5.1 拖放

- 拖放目标设置 `AllowDrop="True"`，处理 `DragEnter`、`DragOver`、`DragLeave`、`Drop`。
- 仅当 `DataPackageView.Contains(StandardDataFormats.StorageItems)` 时接受；`AcceptedOperation=Copy` 只表示读取引用，不复制文件内容。
- `Drop` 中调用 `GetStorageItemsAsync()`，将本地 `StorageFile.Path` 交给输入校验服务；目录是否允许由工具描述决定。
- 拖入期间使用 `VisualStateManager` 切换 `Normal/DragOver/Rejected`；Rejected 必须显示允许的输入类型。
- 读取、探测和缩略图生成异步执行；拖放事件不直接调用 FFmpeg 或 OCR。

### 5.2 文件与路径选择

- 文件选择使用 `FileOpenPicker`，批量工具调用 `PickMultipleFilesAsync()`，单文件工具调用 `PickSingleFileAsync()`。
- 文件保存使用 `FileSavePicker`；输出目录使用 `FolderPicker`。
- Unpackaged WinUI 3 中，通过 `WinRT.Interop.WindowNative.GetWindowHandle(window)` 获取 HWND，并用 `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` 关联主窗口。
- 过滤器由 `ToolDescriptor` 的输入类型生成，UI 不硬编码媒体格式清单。
- 用户取消选择器不是错误；目录不可写、路径过长或磁盘不足由验证服务返回结构化错误并映射到 Error/Warning `InfoBar`。

### 5.3 预览实现边界

| 内容 | 首选实现 | 本地替代 | 明确不做 |
|---|---|---|---|
| 视频/音频 | `MediaPlayerElement` + `MediaPlayer` | FFmpeg 本地缩略图、波形、时长和媒体信息；仍允许处理 | WebView 播放器、在线转码、将“不能播放”误写成“不能处理” |
| 图片 | `Image` + `BitmapImage`/`SoftwareBitmapSource` | 后台生成降采样缓存预览 | 将原始超大图完整解码并长期占用 UI 内存 |
| PDF | `PDFtoImage 5.2.1 + PDFium 147.0.7690 + SkiaSharp 3.119.2` 单页串行栅格化，150 DPI 预览 | 缓存当前/邻近页缩略图；PdfPig 提取文本/结构；PDFsharp 写入/编辑 | `Windows.Data.Pdf` fallback、浏览器/PDF WebView、并发 PDFium 渲染、完整 PDF 排版编辑 |
| OCR/二维码 | `Image` + `Canvas` 识别框 | 缓存中间图和结构化结果 | 云 OCR、网页扫码器 |
| 时间轴 | `MediaPlayerElement` + 自定义 `Canvas` 时间轴 | FFmpeg 缩略图/波形缓存 | 多轨 NLE、第三方时间轴 UI 库 |

## 6. 对话框、消息与状态视觉

- `InfoBar`：页面内可恢复状态、引擎状态、输出状态、格式警告、磁盘空间和识别结果。
- `ContentDialog`：只用于需要用户决定的阻断操作，包括覆盖**非输入文件**的既有输出、清空缓存、删除页面、关闭运行中任务和批量移除。候选输出与任一输入同路径时由输出层无条件拒绝，不弹出可绕过的覆盖确认；一般校验错误也不得弹窗。
- `TeachingTip`：只用于首次使用的本地提示；不作为错误或长期状态载体。
- `ToolTip`：补充图标按钮含义；不能替代可见标签或自动化名称。
- 成功、警告、错误和离线状态必须同时包含图标、文字和主题色；颜色不是唯一信息通道。
- 任务取消分为 `Running → Cancelling → Cancelled`；点击取消后按钮立即禁用并显示“正在取消”，后台确认停止后才显示“已取消”。
- 批量部分失败保留成功输出，任务摘要显示成功数、失败数和“查看失败项”；不得把整个批次伪装成全部失败。

## 7. 900×600 VisualStateManager 规则

应用支持的最小客户区为 900×600。最小客户区由 HWND 的 `WM_GETMINMAXINFO`/可靠等效 Win32 机制执行；页面根 `Grid` 的 VisualStateManager 只负责达到断点后的重排与显隐，不负责阻止窗口缩小。高度不足时由各栏自身纵向滚动解决，底部任务队列保持可见。

| VisualState | AdaptiveTrigger | 布局规则 |
|---|---|---|
| `WideWorkbench` | `MinWindowWidth=1100` | 文件栏 224px；参数栏 300–340px；中央区占剩余宽度；显示常用预设卡、离线徽标完整文字、页面副标题和任务详情；标准间距 24px |
| `CompactWorkbench` | `MinWindowWidth=900` | 文件栏 184px；参数栏 280px；中央区占剩余宽度；间距收紧为 12–16px；隐藏非关键预设卡和重复说明；离线状态缩为图标+可访问名称；任务行隐藏次要元数据但保留进度、百分比和取消 |

`CompactWorkbench` 的共同变更：

- `NavigationView` 保持顶部分类，但缩短 Header 间距；设置仅显示齿轮图标并保留 `AutomationProperties.Name="设置"`。
- 左栏仅保留文件列表、最近、历史和批量任务的必要入口；收藏预设可移入页面命令溢出。
- 中央预览最小宽度不得低于 360 有效像素；工具栏自动进入 `CommandBar` overflow，不允许横向裁切。
- 右栏标签与控件上下排列；路径输入框保留浏览按钮，不隐藏关键参数。
- 底部任务队列高度 92px；多任务时内部滚动，主执行按钮不能被队列遮挡。
- PDF 页卡和证件照预设从多列 `GridView` 降为较少列；由集合控件拥有滚动，不嵌套冲突的 `ScrollViewer`。
- 不使用 `AppWindow.Changed` 事后恢复尺寸。窗口消息钩子按当前 DPI 将 900×600 **客户区**转换为外框最小跟踪尺寸；DPI、显示器或非客户区变化后重新计算，验收记录同时区分客户区尺寸和窗口外框尺寸。

高度规则：

- 600px 时顶部分类约 64px，主要工作区约 444px，任务队列约 92px。
- 参数栏与文件栏独立纵向滚动；主按钮可固定在右栏底部，但不得覆盖滚动内容，需为其预留布局行。
- 100%/125%/150% 显示缩放保持同一信息架构；200% 文本缩放时允许控件增高、文字换行和栏内滚动，不得固定文本容器高度导致裁切，也不得隐藏主操作、错误或焦点目标。

## 8. 浅色、深色与高对比规则

- 所有 TinyPix 颜色、间距、圆角和字号定义在共享 `ResourceDictionary`；颜色使用 `ThemeDictionaries` 的 `Light`、`Dark`、`HighContrast` 分支。
- 浅色保持黑色主按钮、酸橙激活/进度、灰白工作区、白色卡片和克制阴影。
- 深色使用 WinUI 系统表面与文字资源；黑色主按钮改用可与深色表面区分的高对比表面，酸橙色必须满足文本和图形对比要求。
- 高对比模式优先使用系统颜色：窗口背景、窗口文字、高亮、高亮文字、灰色文字；禁用 Mica、Acrylic、阴影、半透明边框和仅靠透明度的 Hover 效果。
- 高对比下选中、焦点、错误和进度使用系统高亮/系统文字并保留图标与文字；不得强制品牌酸橙覆盖系统高亮。
- 焦点使用原生 `FocusVisualPrimaryBrush`、`FocusVisualSecondaryBrush` 和 2px 可见轮廓；不以 CSS 式阴影模拟焦点。
- 图标采用 `FontIcon`/`SymbolIcon` 并继承前景色；装饰图标设置 `AccessibilityView="Raw"`，信息图标必须有名称。
- 普通文字对比度不低于 4.5:1，功能图标和控件边界不低于 3:1。酸橙色表面上的文字与功能图标使用 `OnAccentForeground` 近黑资源；白色或浅色按钮上的功能图标不得使用酸橙色。酸橙图标只允许出现在满足 3:1 的深色底或具有足够对比描边/容器的场景，且状态不得只靠颜色表达。
- Pencil `action-primary` / `on-action-primary` 映射为 WinUI `PrimaryActionBackground` / `PrimaryActionForeground`：浅色和普通深色均为深色底配酸橙前景，高对比改用系统 `ButtonFace` / `ButtonText`；`Button/Primary`、添加文件按钮和任务缩略动作统一引用，不在页面写死颜色。
- `on-danger` 映射为 `DangerActionForeground`：浅色危险背景使用白色，深色危险背景使用近黑，高对比交由系统按钮前景接管。危险状态还必须包含动作文字或警告图标。
- 普通深色主题仍使用 TinyPix 酸橙色表达分类激活和进度；纯黑背景配黄色高亮属于 Windows High Contrast 的系统资源结果，不视为普通深色主题色彩不一致。
- 系统字体优先 `Segoe UI Variable`、`Microsoft YaHei UI`；不打包远程字体。等宽内容使用 `Consolas` 回退。
- 正文和控件不得通过固定像素高度限制文本；中文、英文、150% 文本缩放和 Narrator 均需保持可读。

## 9. MVVM 绑定与视觉状态约定

每个模板 ViewModel 至少暴露：

- `ObservableCollection<InputFileViewModel> Files`
- `InputFileViewModel? SelectedFile`
- `ObservableCollection<JobViewModel> Jobs`
- `ToolDescriptor CurrentTool`
- `PreviewState PreviewState`
- `ValidationState ValidationState`
- `bool HasParameterChanges`
- `bool IsBusy`
- `IAsyncRelayCommand AddFilesCommand`
- `IAsyncRelayCommand ExecuteCommand`
- `IAsyncRelayCommand CancelCommand`
- `IRelayCommand ResetParametersCommand`

统一状态枚举不直接绑定颜色；由 `VisualStateManager` 和语义资源映射：

- 输入：`Empty`、`Ready`、`Multiple`、`Missing`、`Unsupported`。
- 参数：`Default`、`Modified`、`Invalid`、`LockedWhileBusy`。
- 任务：`Queued`、`Running`、`Cancelling`、`Succeeded`、`Failed`、`PartiallyFailed`、`Cancelled`、`Interrupted`。
- 环境：`HealthyOffline`、`PortableDirectoryNotWritable`、`EngineMissing`、`EngineCorrupt`、`PdfEngineInvalid`、`ModelMissing`、`ModelHashMismatch`、`ModelOutOfMemory`、`OutputNotWritable`、`OutputMatchesInput`、`LowDiskSpace`。

View 不根据错误字符串猜测状态；应用服务返回错误代码、用户文案和可恢复动作，ViewModel 再切换视觉状态。

## 10. 键盘与无障碍基线

- Tab 顺序遵循“顶部分类 → 页面标题/命令 → 左文件栏 → 中央预览/编辑 → 右参数栏 → 主执行按钮 → 任务队列”。
- F6 在顶部导航、左文件栏、中央区、右参数栏、任务队列五个区域循环；Shift+F6 反向循环。区域内部使用 Tab，列表、菜单、预设和时间轴使用方向键。
- Ctrl+O 添加文件；当前工具支持文件夹输入时 Ctrl+Shift+O 添加文件夹；输出目录通过路径选择器操作，不占用全局快捷键；Ctrl+Enter 执行；Ctrl+J 展开并聚焦任务队列；Esc 关闭 Flyout/Dialog，不直接取消任务。
- 编辑模板支持 Ctrl+Z/Ctrl+Y；PDF/列表排序支持 Alt+Up/Alt+Down；删除操作均需可恢复或确认。
- 每个页面标题使用 `AutomationProperties.HeadingLevel="Level1"`；参数组为 Level2；列表组名和计数可被 Narrator 读取。
- 每个图标按钮、画布手柄、预览控件和进度条都有唯一、稳定、非视觉化的自动化名称。
- 状态更新避免频繁打断朗读：普通进度按整数变化或每秒最多一次发布；阻断性失败才使用 Assertive。
- 自定义画布元素必须提供等价键盘操作；若无法提供 UI Automation peer，则相同能力必须通过旁边的原生控件完整可达。

## 11. 开发验收检查表

### 11.1 技术与依赖

- [ ] 项目锁定 C#、.NET 10、WinUI 3、Windows App SDK 2.2.0、CommunityToolkit.Mvvm。
- [ ] `TinyPix.App.csproj` 明确设置 `WindowsPackageType=None`、`WindowsAppSDKSelfContained=true`、`SelfContained=true`、`RuntimeIdentifier=win-x64`、`PublishSingleFile=false`。
- [ ] 自包含 unpackaged 启动禁用 framework-dependent Bootstrap 查找，启用 `WindowsAppSdkUndockedRegFreeWinRTInitialize` 自动初始化；不静默依赖系统 Windows App SDK Runtime。
- [ ] 发布为目录式 Unpackaged self-contained x64，解压后普通用户可直接运行，不产生单文件自解压路径。
- [ ] Release 输出包含 Windows App SDK 文件及 PDFium、SkiaSharp、OpenCV、ONNX Runtime、SQLite、FFmpeg/FFprobe 的 `win-x64` native 资产，构建清单逐项验证。
- [ ] UI 工程不存在 WebView、HTML/CSS、第三方 UI 框架、在线资源或运行时下载。
- [ ] 所有可变数据写入便携目录，不写注册表、不注册文件关联、不创建服务或计划任务。
- [ ] 全部路径从 `AppContext.BaseDirectory` 解析；软件目录不可写时阻断工具运行，不回退 AppData 或注册表。
- [ ] 全新普通用户 Windows 10 22H2/Windows 11 x64 离线 VM 未安装 .NET、Windows App SDK Runtime、WebView2、Python、FFmpeg 或数据库时，主窗口仍真实显示且本地资产自检通过。
- [ ] 所有耗时本地处理在后台执行，UI 线程持续响应。

### 11.2 Shell 与组件

- [ ] App Shell 使用原生 `NavigationView`、`Frame` 和单窗口结构。
- [ ] 阶段 C 已验证 Top `NavigationView.PaneHeader` 在 1200×800、900×600、100%/125%/150% 显示缩放、200% 文本缩放及中英文下不挤压分类；验证失败时已经使用固定的独立原生 Header fallback，未自定义 NavigationView 模板。
- [ ] 25 个 Pencil 组件均按本文件映射，未重复创建同义控件。
- [ ] grouped commands 使用 `CommandBar`，没有用松散 Button 行替代。
- [ ] 文件卡、任务卡、预设卡使用可虚拟化集合容器；滚动所有权明确。
- [ ] 只有时间轴、裁剪/选区、识别框和证件照辅助线使用必要的组合型自定义控件。
- [ ] 所有输入都有标签、单位、范围、默认值、错误文案和重置行为。

### 11.3 八类模板

- [ ] 转换/压缩模板覆盖真实文件、预览、参数、队列、取消、输出和错误。
- [ ] 时间轴模板可用键盘完成定位、入出点、分割、排序、删除和缩放。
- [ ] 图片模板支持本地预览、裁剪、旋转、参数调整和非破坏性输出。
- [ ] 证件照模板固定 YuNet 2023mar 负责人脸框/5 点/基础构图，固定 MODNet 负责 alpha matte；哈希、许可证、CPU session、内存和无身份库边界均通过。
- [ ] PDF 模板只使用 PDFtoImage 5.2.1 + PDFium 147.0.7690 + SkiaSharp 3.119.2 栅格化；150 DPI 预览、单页串行、逐页释放、PDFsharp 写入编辑、PdfPig 文本结构和完整许可证目录均通过。
- [ ] OCR/二维码模板支持本地识别框、结果复制、逐页状态和失败隔离；扫描 PDF OCR 复用同一 PDFium 单页串行管线。
- [ ] 生成器/文件工具模板将 `file.rename` 显示为“创建重命名副本”，预览源名称/副本名称、同路径阻断、重复组和校验值；不存在归档压缩入口。
- [ ] 设置/历史/缓存模板只保存路径和记录，许可证与离线说明可离线查看。

### 11.4 响应式、主题与无障碍

- [ ] 1200×800 使用 `WideWorkbench`；900×600 使用 `CompactWorkbench`，均无重叠、裁切或水平滚动。
- [ ] HWND `WM_GETMINMAXINFO`/可靠等效机制按当前 DPI 保证最小 **客户区** 900×600；没有使用 `AppWindow.Changed` 事后纠正，VSM 只负责 900/1100 断点。
- [ ] 100%、125%、150% 显示缩放及 200% 文本缩放下，三栏、底部任务队列和主执行按钮均可用；允许换行与滚动但不得裁切关键内容。
- [ ] 浅色、深色和高对比模式下文字、焦点、选中、错误、进度均清晰。
- [ ] 高对比模式不依赖酸橙色、阴影、透明度或背景图表达状态。
- [ ] 全流程仅用键盘可完成；F6 区域切换、Tab 顺序和快捷键与本文件一致。
- [ ] Narrator 能读出页面标题、文件名、参数名/单位、任务状态/进度、错误和可恢复动作。
- [ ] 自定义画布交互均有原生控件或键盘等价路径。

### 11.5 文件、预览与状态

- [ ] 拖放与选择器只读取文件引用，不改写原文件；除用户显式执行“创建重命名副本”外，不产生原媒体副本。
- [ ] 所有输出服务在打开输出前无条件拒绝候选输出与任一输入同路径/同文件标识；UI、重试、历史和 `ContentDialog` 均无法绕过。
- [ ] `file.rename` 只向用户指定输出目录创建重命名副本，不 rename/move/delete/overwrite 源文件，也不默认写回源目录。
- [ ] 添加受支持文件后立即显示真实本地预览或明确的本地缩略图替代，不回到空拖放态。
- [ ] 视频原生不可播放时仍明确提示 FFmpeg 可以继续处理。
- [ ] PDF、OCR、二维码、图片和时间轴预览均无联网路径；代码和依赖中不存在 `Windows.Data.Pdf` 渲染 fallback。
- [ ] 无文件、批量、参数修改、等待、运行、取消、成功、单项失败、部分失败、格式不支持、原文件丢失、目录不可写、磁盘不足、引擎/模型损坏、缓存和许可证状态均有对应视觉与可恢复动作。
- [ ] 只有覆盖非输入既有输出、清缓存、删除、关闭运行中任务使用 `ContentDialog`；输入同路径输出永远直接阻断，一般校验错误使用 `InfoBar`。

### 11.6 最终对照

- [ ] WinUI 页面逐页与 `design/exports/1200x800` 对照布局、层级、状态和组件。
- [ ] 紧凑页面逐页与 `design/exports/900x600` 对照可见内容和断点行为。
- [ ] 设计图中的黑色主动作、酸橙激活/进度、灰白工作区、白色卡片和克制边界均通过主题资源实现。
- [ ] 任何偏离本映射的实现均有已验证的平台限制证据，并先回到 Pencil 与本文件同步更新。
