# CADTrans Lite — MCP 控制 CAD 翻译（方案 B）

参考 `D:\GitHub\CADMcp` 的两层架构，把 CADTrans Lite 的本地翻译管线以 MCP 暴露给任意 AI 客户端（Claude Desktop / Cursor / VS Code MCP 扩展等），用自然语言驱动「提取图纸文字 → 翻译 → 写回」。

## 架构

```
AI 客户端 (自然语言)
      │ stdio (MCP JSON-RPC)
      ▼
tools/mcp/server.py          (Python MCP 服务器，FastMCP)
      │ TCP JSON-RPC (127.0.0.1:8090)
      ▼
CADTransLite.McpBridge.exe   (C# 无头桥接，复用 CADTransLite.Core 管线)
      │  DwgExtractor → TranslationService → DwgWriter (+ ODA 处理 .dwg)
      ▼
本地翻译引擎 (Argos/LibreTranslate/NLLB/DeepLX) 或 远程引擎 (百度/腾讯/Microsoft/DeepL/自定义AI)
```

## 构建 C# 桥接

```powershell
cd src
dotnet build CADTransLite.McpServer/CADTransLite.McpServer.csproj -c Release
# 产物: src/CADTransLite.McpServer/bin/Release/net9.0-windows/CADTransLite.McpBridge.exe
```

## 启动

1. **启动 C# 桥接**（默认监听 `127.0.0.1:8090`）：
   ```powershell
   CADTransLite.McpBridge.exe --port=8090
   ```
2. **（可选）启动本地 Argos 翻译服务**（离线翻译用），把模型放进 `argos_packages/` 后：
   ```powershell
   python tools\py\argos_server.py
   ```
   或在 MCP 配置里设 `CADTRANS_AUTO_ARGOS=1` 让 server.py 自动拉起。

## 在 AI 客户端里接入

把 `mcp.json` 的内容并入你的 MCP 配置（`mcp.json` 里已给出 Claude Desktop / Cursor / VS Code 的写法）。
关键环境变量：
- `CADTRANS_BRIDGE_EXE`：C# 桥接 exe 路径；设置后若未运行会自动拉起。
- `CADTRANS_AUTO_ARGOS=1`：自动启动本地 Argos。
- `CADTRANS_BRIDGE_HOST/PORT`：桥接地址（默认 127.0.0.1:8090）。

> 依赖：`pip install -r requirements.txt`（需要 `mcp` 包）。

## 可用工具（Tools）

| 工具 | 说明 |
|------|------|
| `list_engines` | 列出全部翻译引擎与本地引擎默认地址 |
| `list_language_pairs` | 查询本地 Argos 已安装语言对数量 |
| `get_status` | 桥接与 Argos 就绪状态 |
| `translate_text` | 翻译纯文本 |
| `read_drawing_entities` | 读取 .dwg/.dxf 可翻译文字实体（含 id/handle/原文/图层） |
| `write_translation` | 按 id/原文/handle 把译文写回图纸（支持 .dwg 自动转回） |
| `translate_drawing` | 完整管线：提取→翻译→写回，返回输出路径与统计 |

## 示例自然语言指令

- “把 `D:\drawings\part.dwg` 里所有英文标注翻译成中文，用 Argos 引擎，存到 `D:\out\`”
- “读取 `D:\a.dxf` 的可翻译文字，列出原文”
- “把这张图里 'BOLT' 翻译成 '螺栓'、'SHAFT' 翻译成 '轴' 并写回”

## 说明

- DWG 文件需要 **ODA File Converter**；可在调用时通过 `oda_path` 参数或在 UI 设置里指定，否则 `translate_drawing`/`read_entities` 会返回明确错误。
- 本地引擎（Argos/LibreTranslate/NLLB/DeepLX）需要对应 HTTP 服务已启动；远程引擎需提供 API 密钥（通过工具参数或 UI 设置配置）。
- 与 CADMcp 的区别：本方案直接复用 CADTrans Lite 已有的 `CADTransLite.Core` 翻译管线（多引擎、DWG/DXF、布局自适应），无需另行实现 CAD 处理逻辑。
