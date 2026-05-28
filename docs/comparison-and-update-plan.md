# CADTrans Lite 对比分析与更新方案

> 基于 `DocuTranslate-for-engineer` (Python/FastAPI) 与 `CADTrans Lite` (C#/WPF) 的全面对比

---

## 一、功能差距总览

| #   | 能力维度             | 参考项目 (DocuTranslate)                                           | 当前项目 (CADTrans Lite)                                               | 差距等级  |
| --- | ---------------- | -------------------------------------------------------------- | ------------------------------------------------------------------ | ----- |
| 1   | **文本清洗/过滤**      | DxfTextCleaner: 空文本/数字/符号/工程编码/目标语言/非源语言过滤                     | ❌ 无过滤，所有文本直接送翻译                                                    | 🔴 严重 |
| 2   | **AI 智能过滤**      | AI 判定 KEEP/SKIP，保护表头，自定义 prompt                                | ❌ 无                                                                | 🟡 中等 |
| 3   | **实体类型覆盖**       | TEXT, MTEXT, ATTRIB, ACAD_TABLE, MLEADER, 块内文字                 | TEXT, MTEXT, ATTRIB                                                | 🔴 严重 |
| 4   | **MTEXT 回写**     | rebuild_mtext_content() 保留命令骨架 + 按视觉宽度换行                       | 简单 placeholder 替换，无换行处理                                            | 🔴 严重 |
| 5   | **布局自适应**        | DxfLayoutAdjuster: 翻译后文字过长自动缩放                                 | ❌ 无                                                                | 🟡 中等 |
| 6   | **Unicode 字体管理** | 自动创建 CJK/Arabic/Devanagari 等文字样式                               | ❌ 无（中文写回可能乱码）                                                      | 🔴 严重 |
| 7   | **翻译去重**         | 按 cleaned_text 去重，相同文本只翻译一次                                    | TranslationMerger 按 (EntityType, OriginalText, RawOriginalText) 合并 | 🟡 部分 |
| 8   | **术语表系统**        | 内置术语管理 + 自动生成 + 翻译时约束替换                                        | ❌ 无                                                                | 🟡 中等 |
| 9   | **插入模式**         | replace/append/prepend 三种模式                                    | 仅 replace                                                          | 🟢 低  |
| 10  | **导出格式**         | CSV: 15+ 字段 (handle, type, layout, cleaned, status, remark...) | Excel: 2 列 (原文, 译文)                                                | 🔴 严重 |
| 11  | **DWG 往返**       | DWG→DXF→翻译→DXF→DWG 完整闭环                                        | DWG→DXF→翻译→DXF (无 DWG 回写)                                          | 🟡 中等 |
| 12  | **ODA 路径解析**     | 5 级降级：配置→环境变量→which→默认路径→glob 搜索                               | OdaSettings 静态默认路径                     | 🟢 低  |

---

## 二、逐项差距详细分析

### 2.1 文本清洗/过滤（🔴 严重）

**参考项目实现** (`dxf_text_cleaner.py`):

- `filter_empty`: 过滤空白文本
- `filter_number`: 识别带单位的数字 (mm, cm, m, kg, kpa, mpa, pa, v, kv, a, hz, kw, w, %, deg, °)、数字范围、比值
- `filter_symbol`: 过滤纯符号文本
- `filter_code`: 8 种工程编码正则 (如 `^[A-Z]{1,8}-\d{1,6}[A-Z]?(?:-\d+)?$`)
- `filter_target_lang`: 过滤已是目标语言的文本 (通过 Unicode 范围检测)
- `filter_non_source_lang`: 过滤非源语言文本
- `_is_engineering_tag()`: 过滤设备标签、型号 (必须包含大写字母+数字或标签符号)

**当前项目现状**:

- `DwgExtractor.Extract()` 仅跳过 `IsNullOrWhiteSpace`
- 所有文本（包括尺寸标注 "Ø25"、工程编码 "A-001"、纯数字 "380V"）全部送翻译
- 翻译 API 会错误处理这些不需要翻译的内容

**影响**: 大量无意义文本被翻译，浪费 API 调用，且工程编码/数字可能被错误改写

---

### 2.2 实体类型覆盖（🔴 严重）

**参考项目支持**:

- `ACAD_TABLE_CELL`: 表格单元格 (行/列索引)
- `MLEADER/MULTILEADER`: 引线标注文字
- 块内文字 (Block 内部嵌套的 TEXT/MTEXT)
- `DxfTableCellTarget`: dataclass 记录 row/col/tag
- `DxfMLeaderTextTarget`: dataclass 记录 leader text 位置

**当前项目现状**:

- `EntityType` 枚举仅有 `Text`, `MText`, `Attribute`
- `DwgExtractor` 不遍历 ACAD_TABLE 和 MLEADER
- 建筑图纸中表格和引线标注非常普遍，缺失这些实体意味着大量文字丢失

---

### 2.3 MTEXT 回写机制（🔴 严重）

**参考项目实现** (`dxf_mtext.py`):

```
rebuild_mtext_content():
  1. 保留原始 MTEXT 命令骨架 ({\H1.5x;...}, {\C6;...})
  2. 仅替换可见文字槽位
  3. wrap_mtext_plain_text(): 按视觉字符宽度自动换行
     - 空格宽度 0.35
     - ASCII 宽度 0.55
     - CJK 宽度 1.0
     - 在 MTEXT 实体宽度内自动插入 \P 换行
```

**当前项目现状**:

- `MTextCodec.StripFormatCodes()`: 用 placeholder 替换格式码 → 翻译 → `RestoreFormatCodes()` 还原
- **核心问题**: 翻译后文字长度变化，placeholder 可能错位
- 无视觉宽度计算，无自动换行
- 翻译后 MTEXT 可能溢出边界框

---

### 2.4 Unicode 字体管理（🔴 严重）

**参考项目实现**:

- `_style_family_for_text()`: 检测字符脚本 (CJK, Arabic, Devanagari, Thai, Greek, Latin, Korean, Japanese)
- `_ensure_unicode_style()`: 自动创建 DXF 文字样式
  - CJK → simsun.ttc
  - Arabic → arial.ttf
  - Devanagari → mangal.ttf
  - Thai → tahoma.ttf
  - etc.
- `_encode_dxf_unicode()`: 非 ASCII 字符编码为 `\U+XXXX`

**当前项目现状**:

- 无任何字体样式管理
- 中文翻译写回 DXF 时可能因缺少 CJK 字体样式而显示为 `???`
- 这是用户反馈 "翻译后中文乱码" 的根本原因

---

### 2.5 导出格式（🔴 严重）

**参考项目 CSV 字段**:

```
id, entity_handle, entity_type, layout_name, original_text, cleaned_text,
translated_text, mtext_rebuilt_text, block_name, attrib_tag,
table_row, table_col, status, remark
```

**当前项目 Excel 格式**:

```
A=原文, B=译文 (仅2列)
```

**问题**:

- 无法追溯翻译对应的 CAD 实体
- 无法记录清洗前后差异
- 无法标注翻译状态 (translated/skipped/error)
- 无法记录 block_name、attrib_tag 等元数据
- Excel 导入时强制行数校验，用户不能增删行

---

### 2.6 翻译去重（🟡 部分差距）

**参考项目**: 按 `cleaned_text` 去重 — 清洗后相同的文本只翻译一次

**当前项目**: `TranslationMerger` 按 `(EntityType, OriginalText, RawOriginalText)` 合并

**差异**:

- 当前项目保留 RawOriginalText 作为合并键的一部分，导致格式码不同但实际文字相同的条目不会合并
- 例如 `{\H1.5x;Hello}` 和 `Hello` 不会被合并，但清洗后都是 `Hello`
- 已有的合并机制方向正确，但缺少清洗后的二次去重

---

### 2.7 布局自适应（🟡 中等）

**参考项目实现** (`dxf_layout.py`):

```python
min_scale = 0.65
text_length_threshold = 1.2    # TEXT: 翻译后/前 > 1.2 才缩放
mtext_length_threshold = 1.5   # MTEXT: 翻译后/前 > 1.5 才缩放
visual_length: ASCII=1.0, non-ASCII=1.6
scale = max(min_scale, threshold / ratio)
```

**当前项目现状**: 无任何布局调整，翻译后文字可能溢出或遮挡其他实体

---

### 2.8 术语表系统（🟡 中等）

**参考项目**: 内置术语管理 + 自动生成 + 翻译时约束替换

**当前项目**: 无术语表功能

**影响**: 专业术语翻译不一致，无法强制特定术语的翻译结果

---

## 三、当前项目已有优势

在对比中也发现当前项目的一些优势，更新时应保留：

| 优势                    | 说明                                              |
| --------------------- | ----------------------------------------------- |
| **桌面原生体验**            | WPF 桌面应用，操作直观，无需部署服务器                           |
| **多引擎支持**             | 百度/腾讯/微软/DeepL/DeepLX/CustomAI 6 种翻译引擎          |
| **语言代码映射**            | `LanguageInfo.GetProviderCode()` 精确映射各 API 语言格式 |
| **Excel 交互**          | 导出-编辑-导入的工作流，对非技术用户友好                           |
| **ImportSettings**    | 可配置的导入选项 (冻结/锁定/关闭图层、MTEXT 整段/分段)               |
| **MTextCodec**        | placeholder 机制设计良好，可在此基础上增强                     |
| **TranslationMerger** | 已有合并机制，可扩展为清洗后去重                                |
| **OdaConverter**      | DWG↔DXF 双向转换已实现，DxfToDwgAsync 方法已存在             |

---

## 四、分阶段更新方案

### Phase 1: 核心翻译质量修复（最高优先级）

> 目标：解决 "翻译后乱码/丢失/溢出" 等根本问题

#### 1.1 文本清洗过滤器 `DxfTextCleaner`

**新建文件**: `Services/DxfTextCleaner.cs`

```
DxfTextCleanerConfig:
  - SourceLang: string (zh/en/ja/ko)
  - TargetLang: string
  - FilterEmpty: bool = true
  - FilterNumber: bool = true
  - FilterSymbol: bool = true
  - FilterCode: bool = true
  - FilterTargetLang: bool = true
  - FilterNonSourceLang: bool = false
  - CustomSkipPatterns: List<string> (用户自定义正则)

DxfTextCleaner:
  - Clean(text, config) → (cleanedText, wasFiltered, filterReason)
  - IsNumber(text): 识别数字+单位、范围、比值
  - IsCode(text): 8种工程编码正则
  - IsEngineeringTag(text): 设备标签/型号
  - MatchesLanguage(text, lang): Unicode 范围检测
```

**正则模式** (从参考项目移植):

```regex
# 数字+单位
^\d+[.,]?\d*\s*(mm|cm|m|kg|kpa|mpa|pa|v|kv|a|hz|kw|w|%|deg|°)$
# 数字范围
^\d+[.,]?\d*\s*[~\-–]\s*\d+[.,]?\d*$
# 比值
^\d+[.,]?\d*\s*[:/]\s*\d+[.,]?\d*$
# 工程编码 (8种)
^[A-Z]{1,8}-\d{1,6}[A-Z]?(?:-\d+)?$
^[A-Z]{2,5}\d{2,6}[A-Z]?$
# etc.
```

#### 1.2 Unicode 字体样式管理 `DxfStyleManager`

**新建文件**: `Services/DxfStyleManager.cs`

```
DxfStyleManager:
  - EnsureUnicodeStyle(doc, text, styleName): 
      检测字符脚本 → 查找/创建对应文字样式 → 返回样式名
  - DetectScript(text): CJK/Arabic/Devanagari/Thai/Greek/Latin/Korean/Japanese
  - EncodeDxfUnicode(text): 非 ASCII 编码为 \U+XXXX
  - ScriptFontMap: Dictionary<Script, FontFile>
```

**字体映射表**:

```csharp
CJK → "simsun.ttc"
Arabic → "arial.ttf"
Devanagari → "mangal.ttf"
Thai → "tahoma.ttf"
Greek → "arial.ttf"
Korean → "batang.ttc"
Japanese → "msmincho.ttc"
```

#### 1.3 MTEXT 回写增强 `MTextRebuilder`

**新建文件**: `Services/MTextRebuilder.cs`

```
MTextRebuilder:
  - RebuildMtextContent(originalRaw, translatedText, entityWidth):
      保留命令骨架 → 替换可见文字 → 按视觉宽度换行
  - WrapPlainText(text, entityWidth, charWidths):
      空格=0.35, ASCII=0.55, CJK=1.0
      在 entityWidth 内插入 \P
  - ReplaceVisibleText(originalSkeleton, translatedText):
      仅替换文字字符，保留所有 MTEXT 命令
```

**集成方式**: 修改 `DwgWriter` 的 MTEXT 回写分支，用 `MTextRebuilder.RebuildMtextContent()` 替代 `MTextCodec.RestoreFormatCodes()`

---

### Phase 2: 实体类型扩展

> 目标：覆盖建筑图纸中最常见的 ACAD_TABLE 和 MLEADER

#### 2.1 扩展 EntityType 枚举

```csharp
public enum EntityType
{
    Text,
    MText,
    Attribute,
    TableCell,      // 新增：ACAD_TABLE 单元格
    MLeader,        // 新增：多重引线标注
}
```

#### 2.2 扩展 TranslationItem 模型

```csharp
// 新增属性
public string? BlockName { get; set; }       // 所属块名
public string? AttributeTag { get; set; }     // ATTRIB 的 tag
public int TableRow { get; set; }            // ACAD_TABLE 行索引 (-1 = 无)
public int TableColumn { get; set; }         // ACAD_TABLE 列索引 (-1 = 无)
public string? FilterReason { get; set; }    // 过滤原因 (skipped/translated/etc.)
public string? CleanedText { get; set; }     // 清洗后的文本
public string? Status { get; set; }          // translated/skipped/error
public string? Remark { get; set; }          // 备注
```

#### 2.3 DwgExtractor 扩展

```csharp
// 新增遍历逻辑
foreach (var table in doc.Entities.AcadTables)  // ACAD_TABLE
{
    for (int row = 0; row < table.Rows.Count; row++)
        for (int col = 0; col < table.Columns.Count; col++)
        {
            var cell = table.Cells[row][col];
            // 提取单元格文字
        }
}

foreach (var leader in doc.Entities.MultiLeaders)  // MLEADER
{
    // 提取引线标注文字
}
```

#### 2.4 DwgWriter 扩展

```csharp
// 新增回写逻辑
case EntityType.TableCell:
    // 通过 handle 定位 table → 按 row/col 写回
    break;
case EntityType.MLeader:
    // 通过 handle 定位 mleader → 写回 Text/ContentType
    break;
```

---

### Phase 3: 增强导出与去重

> 目标：从 2 列 Excel 升级到富元数据 CSV/Excel，实现清洗后去重

#### 3.1 增强 ExcelHandler 导出

**新列布局**:
| 列 | 字段 | 说明 |
|----|------|------|
| A | Handle | 实体句柄 |
| B | EntityType | TEXT/MTEXT/ATTRIB/TABLE/MLEADER |
| C | Layer | 图层名 |
| D | BlockName | 块名 (可选) |
| E | AttributeTag | 属性标签 (可选) |
| F | TableRow/Col | 表格行列 (可选) |
| G | OriginalText | 原始文本 |
| H | CleanedText | 清洗后文本 |
| I | TranslatedText | 译文 |
| J | Status | translated/skipped/error |
| K | FilterReason | 过滤原因 |
| L | Remark | 备注 |

#### 3.2 增强 TranslationMerger

```csharp
// 新增：按 CleanedText 二次去重
// 第一步：现有合并 (EntityType, OriginalText, RawOriginalText)
// 第二步：清洗后去重 — CleanedText 相同的条目共享翻译结果
```

---

### Phase 4: 高级功能

> 目标：布局自适应、术语表、AI 过滤、DWG 往返闭环

#### 4.1 布局自适应 `DxfLayoutAdjuster`

```csharp
DxfLayoutConfig:
  MinScale = 0.65
  TextLengthThreshold = 1.2
  MTextLengthThreshold = 1.5

DxfLayoutAdjuster:
  Adjust(doc, items):
    计算翻译后/前视觉长度比
    超过阈值 → 缩放文字高度 (min=0.65x)
    刷新 MTEXT 的 rect_width/rect_height
```

#### 4.2 术语表系统 `GlossaryManager`

```csharp
GlossaryEntry: SourceTerm, TargetTerm, IsProtected
GlossaryManager:
  Load(path): 加载术语表
  Apply(text, entries): 翻译前替换已知术语
  AutoGenerate(items): 从高频词自动生成术语候选
```

#### 4.3 AI 智能过滤

```csharp
AiTextFilter:
  FilterAsync(items, prompt): 
    批量发送文本给 AI
    AI 返回 KEEP/SKIP 列表
    保护表头 (ACAD_TABLE 第一行)
    保守策略：默认 KEEP
```

#### 4.4 DWG 往返闭环

```csharp
// 已有 DxfToDwgAsync，需要在 ViewModel 中集成
// 工作流：DWG → DXF → 提取 → 翻译 → 写回 DXF → 转 DWG
// 增加 DWG 输出版本选择 (ACAD2007/2010/2013/2018)
```

---

### Phase 5: 用户体验增强

#### 5.1 插入模式

- Replace (默认): 替换原文
- Append: 原文 + 译文
- Prepend: 译文 + 原文

#### 5.2 ODA 路径自动发现

- 5 级降级: 配置路径 → 环境变量 `ODA_FILE_CONVERTER` → `which` → 默认路径 → glob 搜索

#### 5.3 翻译进度可视化

- 按 API 调用批次显示进度
- 显示跳过的文本数量和原因
- 翻译失败时允许重试

---

## 五、实施优先级矩阵

```
影响 ↑
│  Phase 1        Phase 2
│  (核心修复)     (实体扩展)
│  ★★★★★         ★★★★☆
│
│  Phase 3        Phase 4
│  (导出增强)     (高级功能)
│  ★★★☆☆         ★★☆☆☆
│
└──────────────────────────→ 工作量
   小              大
```

**推荐实施顺序**:

1. **Phase 1.2** (Unicode 字体管理) — 解决翻译后乱码，最紧迫
2. **Phase 1.1** (文本清洗过滤) — 减少无效翻译，提升质量
3. **Phase 1.3** (MTEXT 回写增强) — 解决 MTEXT 溢出/格式丢失
4. **Phase 2** (实体类型扩展) — 扩大图纸文字覆盖率
5. **Phase 3** (导出增强+去重) — 提升数据可追溯性
6. **Phase 4** (高级功能) — 按需实施
7. **Phase 5** (体验增强) — 最后打磨

---

## 六、技术风险与注意事项

| 风险                               | 影响              | 缓解措施                                            |
| -------------------------------- | --------------- | ----------------------------------------------- |
| netDxf 对 ACAD_TABLE 的支持可能不完整     | Phase 2 可能受限    | 先验证 netDxf 的 AcadTable API；如不支持，考虑直接操作 DXF CODE |
| MTEXT 格式码极其复杂，rebuild 难以覆盖所有边界情况 | Phase 1.3 可能有遗漏 | 保留现有 MTextCodec 作为 fallback，渐进替换                |
| Unicode 字体文件在用户机器上可能不存在          | Phase 1.2 字体缺失  | 使用系统通用字体 + 降级策略 + 配置化字体映射                       |
| 翻译后文字宽度计算需要考虑字体和字高               | Phase 4.1 精度有限  | 使用近似值 (ASCII/CJK 宽度比)，配合最小缩放下限                  |
| Excel 多列导出与现有导入逻辑不兼容             | Phase 3 破坏性变更   | 版本化导出格式，保留 2 列兼容模式                              |

---

## 七、总结

CADTrans Lite 作为一个桌面翻译工具，在用户交互和多引擎支持方面已经做得不错。但与 DocuTranslate 相比，在 **翻译质量保障** 层面存在明显短板：

1. **不清洗就翻译** → 大量无效文本被翻译
2. **不管理字体** → 中文写回可能乱码
3. **不重建 MTEXT** → 格式丢失/溢出
4. **不覆盖表格和引线** → 遗漏大量文字

Phase 1 聚焦这四个核心问题，预计工作量 2-3 周，完成后翻译质量将有质的提升。Phase 2-5 是锦上添花，可根据用户反馈按需实施。
