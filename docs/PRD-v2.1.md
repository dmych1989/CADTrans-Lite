# CADTrans Lite 产品需求文档（PRD）

> **当前版本**: v2.8 | **目标版本**: v3.0

## 项目信息

| 字段 | 值 |
|------|-----|
| 项目名称 | `cadtrans_lite` |
| 编程语言 | C# / .NET 9 (WPF) |
| UI 框架 | WPF + CommunityToolkit.Mvvm |
| 项目路径 | `E:\CADTrans Lite\src\` |
| v2.1 原始需求 | 合并相同文本、三列 Excel、DWG 支持、翻译 API、语言选择、UI 重构 |
| v3.0 升级来源 | 对比 DocuTranslate-for-engineer 项目，识别 12 项功能差距 |

---

## 产品目标

1. **减少重复翻译工作量**：合并相同文本为单行，翻译一次自动回填所有相同文本，显著减少 Excel 行数和人工翻译量
2. **支持完整 CAD 文件格式**：从仅 DXF 扩展到 DWG（通过 ODA File Converter），覆盖 AutoCAD 主流文件格式
3. **提供端到端翻译工作流**：集成翻译 API，实现"提取 → 机器翻译 → 回填"一键完成，消除手动翻译环节
4. **保障翻译质量**（v3.0 新增）：智能过滤不可翻译内容（工程编码、数字、符号等），管理 Unicode 字体避免乱码，重建 MTEXT 格式避免溢出

---

## 用户故事

1. **As a** CAD 翻译员，**I want** 相同文本自动合并为一行（id 列记录所有 Handle），**so that** 我只需翻译一次，所有相同文本自动回填，避免重复劳动
2. **As a** CAD 翻译员，**I want** 支持打开 .dwg 文件，**so that** 我不需要先手动用 AutoCAD 另存为 DXF 格式
3. **As a** CAD 翻译员，**I want** 在软件内选择源语言和目标语言并一键调用翻译 API，**so that** 我可以快速获得机器翻译初稿，只需微调而非从零翻译
4. **As a** CAD 翻译员，**I want** 三步式工作流界面（提取导出 / 一键翻译 / 导入回填），**so that** 操作流程清晰、步骤明确，不会混淆
5. **As a** CAD 翻译员，**I want** Excel 中 id 列标识每行文本对应的 CAD 实体，**so that** 我可以在 CAD 中通过 Handle 定位到具体文字对象进行验证
6. **As a** CAD 翻译员，**I want** 软件自动过滤掉工程编码、尺寸标注、纯数字等不可翻译内容，**so that** 翻译 API 不被浪费在无需翻译的文本上，且工程数据不被错误改写
7. **As a** CAD 翻译员，**I want** 中文翻译写回 DXF 文件时自动配置 CJK 字体样式，**so that** 翻译结果不会显示为问号或乱码
8. **As a** CAD 翻译员，**I want** MTEXT 翻译后自动按视觉宽度换行，**so that** 翻译文字不会溢出原始边界框

---

## 需求池

### P0 — Must Have（核心功能，v2.1 必须）

| # | 需求 | 说明 | 涉及模块 |
|---|------|------|----------|
| P0-1 | 合并相同文本 | 提取时，`OriginalText` 相同的条目合并为一行；`TranslationItem` 新增 `CadHandles: List<string>` 和 `IdString` 属性 | Core / DwgExtractor |
| P0-2 | 三列 Excel 格式 | A=id（`@_Handle_&f0` 格式），B=原文，C=译文（浅黄底色）。id 列用逗号分隔多个 Handle | Core / ExcelHandler |
| P0-3 | id 格式规范 | 单 Handle：`@_2D07D5_&f0`；多 Handle（合并）：`@_2E9C72_&f0,@_2E9D68_&f0`。回填时解析 id 列还原所有 Handle | Core / ExcelHandler |
| P0-4 | DWG 文件支持 | 通过 ODA File Converter 实现 DWG→DXF 转换→netDxf 处理→DXF→DWG 回转 | Core / 新增 OdaConverter |
| P0-5 | 语言选择器 | 支持 13 种语言（6 主要 + 7 扩展），源语言/目标语言独立选择 | Core / Models / UI |
| P0-6 | 翻译 API 集成 | DeepL / 百度翻译 / 腾讯翻译 API，接口 `ITranslationApi.TranslateAsync` / `TranslateBatchAsync`，批量 50 条/批，支持 CancellationToken | Core / 新增 TranslationApi |
| P0-7 | 三步工作流 UI | Step 1 提取导出 / Step 2 一键翻译 / Step 3 导入回填，含进度显示和统计信息 | UI |
| P0-8 | TranslationTask 数据模型 | 封装提取结果：`SourceFilePath`, `FileType(DWG/DXF)`, `SourceLanguage`, `TargetLanguage`, `Items` | Core / Models |

### P1 — Should Have（重要，提升体验）

| # | 需求 | 说明 | 涉及模块 |
|---|------|------|----------|
| P1-1 | ODA File Converter 路径配置 | 默认路径 `C:\Program Files\ODA\ODAFileConverter\ODAFileConverter.exe`，支持在设置中自定义 | UI / Core |
| P1-2 | ODA 未安装提示 | 检测 ODA 是否可用，未安装时明确提示用户安装，不静默失败 | UI |
| P1-3 | 翻译 API Key 配置 | 设置界面配置 DeepL / 百度 / 腾讯 API Key，持久化到本地配置文件 | UI / Core |
| P1-4 | Excel 导入校验升级 | 适配三列格式：校验 A 列 id 未被篡改、B 列原文未修改、C 列译文合法；合并行回填时展开所有 Handle | Core / ExcelHandler |
| P1-5 | 统计信息面板 | 显示：提取条目数、合并后行数、翻译进度、回填成功/警告/未找到数 | UI |
| P1-6 | 文件类型自动识别 | 根据扩展名（.dwg / .dxf）自动设置 `CadFileType`，UI 拖放/选择对话框同时支持两种格式 | Core / UI |
| P1-7 | 原文保留 `\P` 等格式码 | Excel 原文列保留 `\P` 段落分隔符等格式码，译文列同样支持 `\P`，回填时正确还原 | Core / ExcelHandler |

### P2 — Nice to Have（锦上添花）

| # | 需求 | 说明 | 涉及模块 |
|---|------|------|----------|
| P2-1 | 翻译 API 限流重试 | API 调用失败时指数退避重试，最多 3 次 | Core / TranslationApi |
| P2-2 | DWG 文件损坏/权限不足处理 | ODA 转换失败时区分文件损坏、权限不足、格式不支持等错误类型 | Core / OdaConverter |
| P2-3 | 多文件批量处理 | 支持一次拖入多个 DWG/DXF 文件，依次处理 | UI / Core |
| P2-4 | 翻译缓存 | 已翻译文本本地缓存，避免重复调用 API | Core |
| P2-5 | 导出/导入设置持久化 | 记住上次使用的源语言、目标语言、ODA 路径、API 选择等 | UI |

---

## v3.0 需求池（基于 DocuTranslate 对比分析）

> 来源：`docs/comparison-and-update-plan.md`，识别出 12 项功能差距

### Phase 1 — 核心翻译质量修复（🔴 P0 级）

| # | 需求 | 差距等级 | 说明 | 涉及模块 |
|---|------|---------|------|----------|
| V3-1.1 | 文本清洗/过滤 | 🔴 严重 | 新增 `DxfTextCleaner`，过滤空白/数字/符号/工程编码/目标语言/非源语言文本，避免工程数据被错误翻译 | Core / 新增 DxfTextCleaner |
| V3-1.2 | Unicode 字体样式管理 | 🔴 严重 | 新增 `DxfStyleManager`，翻译写回时自动创建 CJK/Arabic 等文字样式，解决中文乱码问题 | Core / 新增 DxfStyleManager |
| V3-1.3 | MTEXT 回写增强 | 🔴 严重 | 新增 `MTextRebuilder`，保留 MTEXT 命令骨架+注入译文+按视觉宽度换行，替代简单的 placeholder 替换 | Core / 新增 MTextRebuilder |

### Phase 2 — 实体类型扩展（🔴 P0 级）

| # | 需求 | 差距等级 | 说明 | 涉及模块 |
|---|------|---------|------|----------|
| V3-2.1 | ACAD_TABLE 支持 | 🔴 严重 | 扩展 `EntityType` 枚举和 `TranslationItem`，提取/回写表格单元格文字 | Core / DwgExtractor + DwgWriter |
| V3-2.2 | MLEADER 支持 | 🔴 严重 | 提取/回写多重引线标注文字 | Core / DwgExtractor + DwgWriter |

### Phase 3 — 增强导出与去重（🔴 严重 → 🟡 中等）

| # | 需求 | 差距等级 | 说明 | 涉及模块 |
|---|------|---------|------|----------|
| V3-3.1 | 富元数据 Excel 导出 | 🔴 严重 | 从 2 列升级到 12 列：Handle, EntityType, Layer, BlockName, AttributeTag, TableRow/Col, OriginalText, CleanedText, TranslatedText, Status, FilterReason, Remark | Core / ExcelHandler |
| V3-3.2 | 清洗后去重 | 🟡 中等 | 在现有 TranslationMerger 基础上，增加按 CleanedText 二次去重，相同清洗后文本只翻译一次 | Core / TranslationMerger |

### Phase 4 — 高级功能（🟡 中等）

| # | 需求 | 差距等级 | 说明 | 涉及模块 |
|---|------|---------|------|----------|
| V3-4.1 | 布局自适应 | 🟡 中等 | 翻译后文字过长时自动缩放字高（最小 0.65x），刷新 MTEXT 边界 | Core / 新增 DxfLayoutAdjuster |
| V3-4.2 | 术语表系统 | 🟡 中等 | 内置术语管理 + 翻译时约束替换 | Core / 新增 GlossaryManager |
| V3-4.3 | AI 智能过滤 | 🟡 中等 | AI 判定文本 KEEP/SKIP，保护表头，自定义 prompt | Core / 新增 AiTextFilter |
| V3-4.4 | DWG 往返闭环 | 🟡 中等 | 翻译后 DXF 自动转回 DWG，支持 DWG 输出版本选择 | Core / ViewModel + OdaConverter |

### Phase 5 — 体验增强（🟢 低）

| # | 需求 | 差距等级 | 说明 | 涉及模块 |
|---|------|---------|------|----------|
| V3-5.1 | 插入模式 | 🟢 低 | replace/append/prepend 三种模式 | Core / DwgWriter |
| V3-5.2 | ODA 路径自动发现 | 🟢 低 | 5 级降级：配置→环境变量→which→默认路径→glob 搜索 | Core / OdaConverter |

### 实施优先级

```
1. V3-1.2 (Unicode 字体管理)  → 解决翻译后乱码，最紧迫
2. V3-1.1 (文本清洗过滤)      → 减少无效翻译，提升质量
3. V3-1.3 (MTEXT 回写增强)    → 解决 MTEXT 溢出/格式丢失
4. V3-2.x (实体类型扩展)      → 扩大图纸文字覆盖率
5. V3-3.x (导出增强+去重)     → 提升数据可追溯性
6. V3-4.x (高级功能)          → 按需实施
7. V3-5.x (体验增强)          → 最后打磨
```

---

## 数据模型变更摘要

### TranslationItem（已有，需扩展）

```
+ CadHandles: List<string>     // 多 Handle（合并场景）
+ IdString: string             // 生成 "@_Handle_&f0" 格式，多 Handle 逗号分隔
  Handle: string               // 保留，用于非合并场景的单一 Handle
```

### v3.0 新增/扩展类

| 类名 | Phase | 说明 |
|------|-------|------|
| `DxfTextCleaner` | Phase 1 | 文本清洗过滤器：过滤空白/数字/符号/工程编码/目标语言文本 |
| `DxfTextCleanerConfig` | Phase 1 | 清洗配置：SourceLang, TargetLang, FilterEmpty/Number/Symbol/Code/TargetLang/NonSourceLang |
| `DxfStyleManager` | Phase 1 | Unicode 字体样式管理：检测字符脚本→创建 DXF 文字样式→编码为 \U+XXXX |
| `MTextRebuilder` | Phase 1 | MTEXT 回写增强：保留命令骨架→替换可见文字→按视觉宽度换行 |
| `DxfLayoutAdjuster` | Phase 4 | 布局自适应：翻译后文字过长自动缩放字高 |
| `GlossaryManager` | Phase 4 | 术语表管理：加载/应用/自动生成术语 |
| `AiTextFilter` | Phase 4 | AI 智能过滤：AI 判定 KEEP/SKIP |

### TranslationItem v3.0 扩展字段

```
+ BlockName: string?           // 所属块名
+ AttributeTag: string?         // ATTRIB 的 tag
+ TableRow: int                // ACAD_TABLE 行索引 (-1 = 无)
+ TableColumn: int             // ACAD_TABLE 列索引 (-1 = 无)
+ FilterReason: string?        // 过滤原因 (skipped/translated/etc.)
+ CleanedText: string?         // 清洗后的文本
+ Status: string?              // translated/skipped/error
+ Remark: string?              // 备注
```

### EntityType 枚举扩展

```
Text, MText, Attribute, TableCell, MLeader  // v3.0 新增 TableCell 和 MLeader
```

### Excel 格式规范（v2.1 — 2 列模式）

| 列 | 表头 | 说明 | 示例 |
|----|------|------|------|
| A | 原文 | 原始文本（保留 `\P` 等格式码） | `Side wall insulation\PDouble-sided...` |
| B | 译文 | 翻译文本（浅黄底色提示） | `侧墙保温板\P双面...` |

### Excel 格式规范（v3.0 — 12 列富元数据模式）

| 列 | 表头 | 说明 | 示例 |
|----|------|------|------|
| A | Handle | 实体句柄 | `2D07D5` |
| B | EntityType | 实体类型 | TEXT / MTEXT / ATTRIB / TABLE / MLEADER |
| C | Layer | 图层名 | `0` / `Text Layer` |
| D | BlockName | 块名 (可选) | `Block1` |
| E | AttributeTag | 属性标签 (可选) | `TAG1` |
| F | Row/Col | 表格行列 (可选) | `3/5` |
| G | OriginalText | 原始文本 | `Side wall insulation` |
| H | CleanedText | 清洗后文本 | `Side wall insulation` |
| I | TranslatedText | 译文 | `侧墙保温` |
| J | Status | 状态 | translated / skipped / error |
| K | FilterReason | 过滤原因 | `number` / `code` / `target_lang` |
| L | Remark | 备注 | |

---

## 当前代码与目标版本的差距

| 模块 | 当前状态 (v2.8) | v3.0 目标 |
|------|---------|-----------|
| DwgExtractor | TEXT/MTEXT/ATTRIB，无过滤 | + ACAD_TABLE/MLEADER + DxfTextCleaner 清洗 |
| DwgWriter | placeholder 替换回写 | MTextRebuilder 骨架重建 + DxfStyleManager 字体管理 |
| ExcelHandler | 2 列 (原文/译文) | 12 列富元数据 + 2 列兼容模式 |
| TranslationMerger | 按 (EntityType, OriginalText, RawOriginal) 合并 | + CleanedText 二次去重 |
| TranslationItem | 基础字段 | + BlockName, AttributeTag, TableRow/Col, CleanedText, Status, FilterReason, Remark |
| EntityType | Text, MText, Attribute | + TableCell, MLeader |
| 无文本清洗 | — | DxfTextCleaner + DxfTextCleanerConfig |
| 无字体管理 | — | DxfStyleManager (CJK/Arabic/Devanagari/Thai 等) |
| 无 MTEXT 重建 | — | MTextRebuilder (骨架保留+视觉换行) |

---

## 版本规划

| 版本 | 功能 | 状态 |
|------|------|------|
| **v1.0** | 核心：DWG/DXF 提取 + Excel 导出/导入 + 回填 | ✅ 完成 |
| **v2.0** | 扩展：集成翻译 API + 多语言支持 | ✅ 完成 |
| **v2.1-v2.8** | 增量：合并相同文本、DWG 支持、多引擎翻译、设置持久化 | ✅ 完成 |
| **v3.0** | 质量：文本清洗/过滤 + Unicode 字体管理 + MTEXT 重建 + 实体扩展 + 富导出 | 🚧 开发中 |

---

## 测试文件

| 类型 | 路径 |
|------|------|
| DWG | `E:\CADTrans Lite\英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dwg` |
| DXF | `E:\CADTrans Lite\英文版汉韬尼日利亚104x13.4x4蛋鸡舍方案图及土建图.dxf` |
| 参考 Excel | `E:\CADTrans Lite\20260526090802_en-zh_all.xlsx`（96 行，已合并相同文本） |

---

## 语言列表

**主要语言（6 种）**：中文、英文、俄语、西班牙语、葡萄牙语、阿拉伯语

**扩展语言（7 种）**：法语、越南语、韩语、日语、意大利语、德语、泰语
