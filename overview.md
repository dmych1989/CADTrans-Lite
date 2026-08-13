# Phase 2 交付概览 — ACAD_TABLE + MULTILEADER 支持

## TL;DR
Phase 2 双通道架构实现完成，CADTrans Lite 现可提取和回写 ACAD_TABLE 单元格文本及 MULTILEADER 多重引线文本。

## 交付状态
- **编译**：0 errors
- **测试**：163 通过 / 1 跳过 / 0 失败（新增 56 个 Phase 2 专项测试）
- **源码 Bug**：0
- **已知设计权衡**：3 个低风险项（编码回退策略、默认 R2007+ 格式）

## 文件清单

### 新增文件（3个）
| 文件 | 路径 | 说明 |
|------|------|------|
| DxfRawEntity.cs | `src/CADTransLite.Core/Models/` | ACAD_TABLE/MULTILEADER 数据模型 |
| DxfRawParser.cs | `src/CADTransLite.Core/Services/` | DXF 原始文本解析器 |
| DxfTextReplacer.cs | `src/CADTransLite.Core/Services/` | 按行号精准文本替换器 |

### 修改文件（4个）
| 文件 | 路径 | 改动 |
|------|------|------|
| ImportSettings.cs | `src/CADTransLite.Core/Models/` | +ImportAcadTables, +ImportMultiLeaders |
| DwgExtractor.cs | `src/CADTransLite.Core/Services/` | +ACAD_TABLE/MLEADER 提取逻辑 |
| DwgWriter.cs | `src/CADTransLite.Core/Services/` | +TableCell/MLeader 回写路径 |
| ExcelHandler.cs | `src/CADTransLite.Core/Services/` | CloneItem() 补全新字段 |

### 架构设计文档
| 文件 | 路径 |
|------|------|
| phase2-architecture.md | `docs/` |

## 架构要点
- **双通道设计**：主通道(netDxf)处理 TEXT/MTEXT/Attribute + 辅助通道(原始DXF文本解析)处理 ACAD_TABLE/MULTILEADER
- **行号定位**：DxfRawParser 记录组码值行号，DxfTextReplacer 按行号精准替换，避免字符串搜索误替换
- **版本自适应**：自动检测 $ACADVER，R2004 用组码 171/1/3，R2007+ 用组码 301/302
- **Handle 命名**：TableCell=`{tableHandle}::R{row}::C{col}`, MLeader=`{handle}::CTX`

## 下一步建议
1. **集成测试**：用含 ACAD_TABLE/MULTILEADER 的真实 DWG 文件端到端验证
2. **Phase 3 开发**：富元数据列 + 清洗后去重
3. **编码策略优化**：可考虑检测 DXF BOM 自动选择 UTF-8，降低编码回退风险
