# 作业票切分工具（TicketSplitter）

把**扫描件作业票 PDF**（图像型、无文字层）自动切分成单张票，按 `票类型\区域` 归档，并输出 `作业票清单.csv`。

用 **.NET 10 + WinUI 3** 重写自原有的 Python 流水线（`ticket_split.py` / `split_tool.py` / `ticket_tool.py`），在保持产出**逐字节兼容**的前提下，补上了老流水线缺失的能力。

---

## 它解决什么问题

原 Python 流水线能跑，但有三个硬伤（由 `D:\codex\UNZYP` 的历史日志和产出反推得出）：

| 老流水线的问题 | 证据 | 本项目的做法 |
|---|---|---|
| **无重试、无断点续跑**，任一块解析失败就整体退出，只能手写一次性 resume 脚本 | `PDF合并_run.log` 里 "块 2 解析失败: parsing failed, please try again later"；随后有人写了 `_旧脚本/resume3.py`（5 次重试）和 `resume_chunks.py` | `MineruClient` 内置**指数退避重试** + `parse_state.json` **断点续跑**，已完成的块直接跳过 |
| **API Token 明文写在脚本里** | `_旧脚本/resume3.py:6`、`resume_chunks.py:6` 硬编码了 `sk-...` | Token 只存内存；勾选"记住"时用 **DPAPI（当前用户）加密**后落盘，绝不写日志 |
| **产出不校验**，"1 页残片"照样报"完成" | `1FX`/`1FXX`/`20260903154222043_split` 里存在 1 页票（作业票必然含签字页与施工后检查页） | 内置**产出体检**：单页残片、缺时间、缺区域、页覆盖缺口/重叠全部显式告警 |
| **版式判定只看前 8 页**，混合版式静默按半幅处理 | `split_tool.py: needs_halves(doc, sample=8)`，`land == n` 全为横版才算整版 | 统计**全部页**的整版占比，识别出 `Mixed` 并提示人工确认 |
| **产物无版本管理**，靠复制目录 + 文件名加数字 | `_旧脚本` 里 16 个历史脚本；`1FX` 先 54 票后被 56 票覆盖 | Git + 单元测试 + 可复现的 CLI |

---

## 功能

- **版式自适应**：自动识别「整版（一页并排两张 A5 表）」与「半幅（每页一张）」，整版自动拆成左右半幅
- **MinerU 云解析**：分块上传（按体积估算，守 200MB / 180 页限制）→ 轮询 → 合并 `content_list`
- **切票**：按票标题（`承揽商/高处/动火作业申请表`，容忍 OCR 错字）切分，并提取施工时间、施工区域
- **可视化人工校正**：页面缩略图与票列表**双向联动**（点票跳到该票首页、点页选中所属票）；支持「从此页切开 / 并入上一张 / 删除选中票 / 恢复自动切分」
- **归档导出**：`票类型\区域\ticket_NNN_..._pageX-Y.pdf` + `作业票清单.csv`（UTF-8 BOM，Excel 直接打开）
- **产出体检**：导出前给出需要人工核对的票

---

## 界面布局

采用「顶部命令栏 + 主工作区 + 底部状态栏」三段式，而不是把配置项堆在常驻侧栏里：

```
┌──────────────────────────────────────────────────────────────┐
│ 作业票切分工具                                                │  标题栏
├──────────────────────────────────────────────────────────────┤
│ [选择PDF] 文件名 [体积·页数] [版式] [版式▾]   [页面] [Token] [开始解析] [取消] [导出…] │  命令栏
├──────────────────────────────┬───────────────────────────────┤
│ 切分结果 共56张 · 覆盖148页   │ 页面            选中 第14页    │
│ ⚠ 发现 2 处需要关注           │ [切开][并入上张][删除]         │
│ 序号 票类型 区域 时间 起止页 页数 状态 │                        │
│ ...                          │  缩略图（带票号色块）           │
├──────────────────────────────┴───────────────────────────────┤
│ 识别到 56 张票                    ▓▓▓░░░  C:\...\out           │  状态栏
└──────────────────────────────────────────────────────────────┘
```

设计取舍：

- **一次性操作（选文件 / Token / 导出目录）不占常驻空间**：Token 与"复用已有解析结果"收进「解析设置」对话框，导出目录与"清空"收进「导出」对话框
- **主体是切分结果表**，因此占据左侧主区并可伸缩；页面缩略图作为**辅助校验**放右侧，可用命令栏的切换按钮折叠
- **告警不藏在折叠线以下**：汇总条固定在票表上方，「状态」列在行内直接写出「单页残片 / 缺施工时间」等原因
- 空状态、按钮禁用态、进度条位置都按当前是否可用呈现，不再是"永远可点的四个按钮"

---

## 项目结构

```
TicketSplitter.slnx
├── src/
│   ├── TicketSplitter.Core/          # 与 UI 无关的核心库（net10.0）
│   │   ├── Pdf/                      # 版式分析、半幅几何、页区间导出
│   │   │   ├── PdfGeometry.cs        #   PageBox / PageInfo（旋转感知、退化 CropBox 回退）
│   │   │   ├── PdfSource.cs          #   打开一次、导出多张票
│   │   │   ├── HalfSheetSplitter.cs  #   未旋转坐标系下的左右半幅几何
│   │   │   └── PdfLayoutAnalyzer.cs  #   整版 / 半幅 / 混合
│   │   ├── Mineru/                   # MinerU v4 客户端
│   │   │   ├── MineruClient.cs       #   上传/轮询/下载 + 重试
│   │   │   ├── ParseStateStore.cs    #   断点续跑状态
│   │   │   └── PdfChunker.cs         #   按体积+页数分块
│   │   ├── Tickets/                  # 切票规则（Python 逻辑的忠实移植）
│   │   │   ├── TicketDetector.cs     #   标题页 -> 票边界
│   │   │   ├── AreaNormalizer.cs     #   FAB东西区N层 归一化（含 OCR 纠错）
│   │   │   ├── TimeRangeParser.cs
│   │   │   ├── TicketValidator.cs    #   产出体检
│   │   │   └── ...
│   │   ├── Export/                   # 归档 + CSV
│   │   └── Pipeline/                 # 串起全链路的 SplitPipeline
│   ├── TicketSplitter.Cli/           # 无界面命令行（便于脚本化与回归）
│   └── TicketSplitter.App/           # WinUI 3 桌面应用
└── tests/TicketSplitter.Core.Tests/  # 13 个测试，含真实语料黄金比对
```

**分层原则**：所有业务逻辑在 `Core`，可以脱离 WinUI 单独测试；`App` 只负责 UI 与编排；`Cli` 复用同一套 `Core`。

---

## 构建与运行

需要 .NET 10 SDK 与已安装的 Windows App Runtime 1.7。

```powershell
# 全部构建
dotnet build TicketSplitter.slnx -c Release

# 跑测试（含与老流水线产出的黄金比对）
dotnet test tests/TicketSplitter.Core.Tests

# 命令行
dotnet run --project src/TicketSplitter.Cli -- analyze "D:\path\scan.pdf"
dotnet run --project src/TicketSplitter.Cli -- split "D:\path\scan.pdf" --out D:\out --token $env:MINERU_TOKEN

# 桌面应用
dotnet build src/TicketSplitter.App
.\src\TicketSplitter.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\TicketSplitter.exe
```

> ⚠️ WinUI 项目**必须**保持 `<EnableMsixTooling>true</EnableMsixTooling>`（同时 `WindowsPackageType=None`，仍然是非打包应用、不需要证书）。
> 设为 `false` 会让 MSBuild 走 `MrtCore.PriGen.targets`，该目标硬编码从 Visual Studio 安装目录加载
> `Microsoft.Build.Packaging.Pri.Tasks.dll`，dotnet CLI 下必然 `MSB4062` 构建失败，且不会产出 `resources.pri`。
> 构建是否健康，看输出目录里有没有 `resources.pri`。

---

## 验证

### 1. 单元测试（13 个，全部通过）

最关键的是**黄金比对**：用 C# 移植的切票逻辑重跑老流水线的 `content_list.json`，
逐行比对它当时产出的 `作业票清单.csv`（序号 / 票类型 / 施工区域 / 施工时间 / 起始页 / 结束页 / 页数 七列全比）。

- 随仓库携带的真实 fixture：`1014` 数据集，146 张票，**全部一致**
- 本机语料存在时，自动跑**全部 6 个已产出数据集**（`1014` / `1FX` / `1FXX` / `NEw` / `PDF合并_split` / `20260903154222043_split`），全部一致

同时覆盖真实扫描件的几何回归：`PDF合并.pdf`（已是半幅）判为 HalfSheet；`作业票\20260916212450093.pdf`（含退化 `/CropBox = [0 0 0 0]`）判为整版并正确拆成 76 页。

### 2. 端到端

```
老流水线输出 D:\codex\UNZYP\PDF合并_split\作业票清单.csv  (28759 字节)
新 CLI 输出      %TEMP%\tsp_e2e\out\作业票清单.csv          (28759 字节)
=> 逐字节完全一致，146 张票

老流水线输出 D:\codex\UNZYP\1FXX\作业票清单.csv           (10943 字节)
新 GUI 导出       %TEMP%\tsp_ui_out\作业票清单.csv           (10943 字节)
=> 逐字节完全一致，56 张票
```

### 3. 体检确实抓到了老流水线漏掉的问题

用 `1FXX` 的真实解析结果跑 GUI，应用自动报出：

> 发现 2 处需要关注
> 票 #4 只有 1 页（动火作业申请表 FAB东区1层），很可能是切分残片，建议人工核对。
> 票 #6 只有 1 页（动火作业申请表 FAB东区1层），很可能是切分残片，建议人工核对。

而老流水线对同一批数据的输出是"完成，缺施工时间 0"，完全没有察觉。

---

## 实现要点（踩过的坑）

1. **`/CropBox` 可能退化**。真实扫描件 `作业票\20260916212450093.pdf` 的 `/CropBox` 是 `[0 0 0 0]`，
   直接使用会把整页算成 0×0。`PageInfo.Box` 会检测并回退到 `MediaBox`。
2. **必须用 `PdfPage.CropBox` 属性，不能用 `Elements["/CropBox"]`**。后者返回原始 `PdfArray`，
   `is PdfRectangle` 判断永远为假 —— 这个 bug 曾让所有 CropBox 被忽略，是真实数据测试抓出来的。
3. **半幅裁剪要在未旋转坐标系里算**。页面 `/Rotate=90/270` 时，显示空间的"左右"对应未旋转空间的"上下"，
   所以要按 y 切。推导结果与老流水线产出的 CropBox（`(0,421,595,841)` / `(0,1,595,422)`）完全吻合。
4. **图片 XObject 不能翻倍**。用 `AddPage` 把同一源页添加两次再设不同 CropBox，PDFsharp 会复用图片对象：
   实测 8 页 13.58MB → 拆成 16 页 13.80MB。
5. **同一个 Import 模式 `PdfReader` 可以连续写多个输出文档**，不会串页 —— 因此 146 张票只需打开源文件一次。
6. **尺寸抖动要归一**。同一批扫描纸会出现 420 与 421 这种 1pt 差异，按 5pt 分箱后再做"尺寸是否统一"的判断，
   否则会误报"混入了异形页"。
7. **未打包 WinUI 应用没有 `ApplicationData.Current`**，设置要自己落到 `%LOCALAPPDATA%\TicketSplitter`。
8. **文件/文件夹选择器要先用窗口句柄初始化**（`InitializeWithWindow.Initialize`），否则直接抛异常。

---

## 已知限制

- 解析依赖 **MinerU 云服务**（按需求明确不做本地 OCR），需要 Token 与联网
- 不做 A4 水印与"每日合并件"（按需求本次只做切分）；老流水线的这两步在 `_旧脚本/ticket_tool.py` 与 `make_a4_native3.py` 里
- 只处理"按标题切票"这一种模式，没有做固定页数切分/书签切分
- 缩略图为启动时一次性渲染，超大文档（>1500 页）首次载入会偏慢