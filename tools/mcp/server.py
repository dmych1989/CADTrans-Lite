#!/usr/bin/env python3
# server.py — CADTrans Lite MCP server (方案 B)
#
# 两层架构（参考 D:\GitHub\CADMcp）：
#   1) C# 无头桥接服务 CADTransLite.McpBridge（TCP JSON-RPC，端口 8090）承载真实
#      CAD 翻译管线（提取 -> 翻译 -> 写回 .dwg/.dxf）。
#   2) 本文件是 Python MCP 服务器，把 MCP 工具调用转发给上面的 C# 桥接服务。
#
# AI 客户端（Claude Desktop / Cursor / VS Code MCP 扩展等）通过 stdio 与本服务器通信，
# 本服务器再通过 TCP 与 C# 桥接服务通信。
#
# 环境变量：
#   CADTRANS_BRIDGE_HOST  桥接服务地址（默认 127.0.0.1）
#   CADTRANS_BRIDGE_PORT  桥接服务端口（默认 8090）
#   CADTRANS_BRIDGE_EXE   可选：C# 桥接 exe 路径，若设置且连接失败会自动拉起
#   CADTRANS_AUTO_ARGOS   设为 1 时自动启动本地 Argos 翻译服务（tools/py/argos_server.py）
#   CADTRANS_PY_DIR       可选：指定 tools/py 目录（默认从本文件向上定位）
#   CADTRANS_MCP_LOG      可选：日志文件路径（默认打到 stderr）

import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

# ---- 定位仓库与依赖 -------------------------------------------------------
HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]  # tools/mcp -> tools -> repo root
sys.path.insert(0, str(HERE))

HOST = os.environ.get("CADTRANS_BRIDGE_HOST", "127.0.0.1")
PORT = int(os.environ.get("CADTRANS_BRIDGE_PORT", "8090"))
BRIDGE_EXE = os.environ.get("CADTRANS_BRIDGE_EXE", "")
AUTO_ARGOS = os.environ.get("CADTRANS_AUTO_ARGOS", "") == "1"
PY_DIR = os.environ.get("CADTRANS_PY_DIR", str(REPO_ROOT / "tools" / "py"))

_bridge_proc = None
_argos_proc = None


def log(msg: str) -> None:
    """打印日志到 stderr（MCP 的 stdout 必须只用于协议消息）。"""
    print(f"[cadtrans-mcp] {msg}", file=sys.stderr, flush=True)


# ---- 本地 Argos 翻译服务（可选自动拉起） ----------------------------------
def ensure_argos():
    if not AUTO_ARGOS:
        return
    argos_py = Path(PY_DIR) / "argos_server.py"
    if not argos_py.exists():
        log(f"未找到 argos_server.py: {argos_py}")
        return
    global _argos_proc
    if _argos_proc is not None and _argos_proc.poll() is None:
        return
    try:
        log(f"启动本地 Argos 翻译服务: {argos_py}")
        # 用仓库自带的可嵌入 python（如存在）否则用系统 python
        py_exe = "python.exe" if (Path(PY_DIR) / "python.exe").exists() else "python"
        _argos_proc = subprocess.Popen(
            [py_exe, str(argos_py)],
            cwd=str(PY_DIR),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        # 等待服务就绪（最多 150s，首启加载模型较慢）
        for _ in range(150):
            if _probe_argos():
                log("Argos 已就绪")
                return
            time.sleep(1)
        log("等待 Argos 就绪超时（仍将继续，翻译可能失败）")
    except Exception as e:  # noqa: BLE001
        log(f"启动 Argos 失败: {e}")


def _probe_argos() -> bool:
    try:
        with socket.create_connection((HOST, 5001), timeout=1):
            return True
    except OSError:
        return False


# ---- C# 桥接服务进程管理 --------------------------------------------------
def ensure_bridge():
    global _bridge_proc
    if _bridge_proc is not None and _bridge_proc.poll() is None:
        return
    if not BRIDGE_EXE:
        return
    try:
        log(f"启动 C# 桥接服务: {BRIDGE_EXE}")
        _bridge_proc = subprocess.Popen(
            [BRIDGE_EXE, f"--port={PORT}"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        for _ in range(30):
            if send_command("get_status", {}):
                log("C# 桥接服务已就绪")
                return
            time.sleep(0.5)
        log("等待 C# 桥接服务就绪超时")
    except Exception as e:  # noqa: BLE001
        log(f"启动 C# 桥接服务失败: {e}")


# ---- TCP JSON-RPC 客户端 --------------------------------------------------
def send_command(command: str, params: dict, timeout: float = 180.0) -> dict | None:
    """向 C# 桥接服务发送一条命令并返回解析后的响应 dict；失败返回 None。"""
    payload = json.dumps({"command": command, "params": params or {}}) + "\n"
    try:
        with socket.create_connection((HOST, PORT), timeout=5) as sock:
            sock.settimeout(timeout)
            sock.sendall(payload.encode("utf-8"))
            buf = b""
            while b"\n" not in buf:
                chunk = sock.recv(65536)
                if not chunk:
                    break
                buf += chunk
            line = buf.split(b"\n", 1)[0]
        return json.loads(line.decode("utf-8"))
    except Exception as e:  # noqa: BLE001
        log(f"send_command({command}) 失败: {e}")
        return None


def call(command: str, params: dict, timeout: float = 180.0) -> dict:
    """调用桥接命令并统一处理 success/error，失败抛出 RuntimeError。"""
    ensure_argos()
    ensure_bridge()
    resp = send_command(command, params, timeout)
    if resp is None:
        raise RuntimeError(
            f"无法连接 C# 桥接服务（{HOST}:{PORT}）。请先启动 CADTransLite.McpBridge.exe，"
            f"或在环境变量 CADTRANS_BRIDGE_EXE 中指定其路径以便自动拉起。"
        )
    if not resp.get("success", False):
        raise RuntimeError(resp.get("error", "未知错误"))
    return resp.get("data", {})


# ---- MCP 服务器（FastMCP） ------------------------------------------------
try:
    from mcp.server.fastmcp import FastMCP
except ImportError:
    log("未安装 mcp 包，请运行: pip install -r requirements.txt")
    raise

mcp = FastMCP("CADTrans Lite")


@mcp.tool()
def list_engines() -> str:
    """列出 CADTrans Lite 支持的所有翻译引擎（含本地引擎默认地址与配置说明）。"""
    data = call("list_engines", {})
    return json.dumps(data, ensure_ascii=False, indent=2)


@mcp.tool()
def list_language_pairs(argos_url: str = "http://127.0.0.1:5001") -> str:
    """查询本地 Argos 翻译服务已安装的可用语言对数量（需要 Argos 服务已启动）。"""
    data = call("list_language_pairs", {"argos_url": argos_url})
    return json.dumps(data, ensure_ascii=False, indent=2)


@mcp.tool()
def get_status() -> str:
    """返回桥接服务与本地 Argos 服务的就绪状态。"""
    data = call("get_status", {})
    return json.dumps(data, ensure_ascii=False, indent=2)


@mcp.tool()
def translate_text(
    text: str,
    source: str = "en",
    target: str = "zh",
    engine: str = "Argos Translate (本地)",
) -> str:
    """翻译一段纯文本。参数：text 待翻译文本；source/target 语言代码(en,zh,ja,ko...);
    engine 翻译引擎名（见 list_engines）。"""
    data = call("translate_text", {
        "text": text, "source": source, "target": target, "engine": engine,
    })
    return json.dumps(data, ensure_ascii=False, indent=2)


@mcp.tool()
def read_drawing_entities(file_path: str) -> str:
    """读取 CAD 图纸（.dwg/.dxf）中的可翻译文字实体列表。
    返回每个实体的 id、handle、entity_type、original_text、layer、block 与总数。
    调用后这些实体会被缓存，供 write_translation 使用。"""
    data = call("read_entities", {"file_path": file_path}, timeout=120.0)
    return json.dumps(data, ensure_ascii=False, indent=2)


@mcp.tool()
def write_translation(
    file_path: str,
    translations: list[dict],
    enable_layout_adjust: bool = True,
) -> str:
    """把译文写回 CAD 图纸。translations 为列表，每项形如
    {"id": "...", "translated": "中文"} 或 {"original": "English", "translated": "中文"}
    或 {"handle": "...", "translated": "中文"}。
    会基于 read_drawing_entities 缓存的实体进行匹配，返回输出路径与更新/未命中统计。
    若原文件为 .dwg 且 ODA 可用，会自动转回 DWG。"""
    data = call("write_translation", {
        "file_path": file_path,
        "translations": translations,
        "enable_layout_adjust": enable_layout_adjust,
    }, timeout=120.0)
    return json.dumps(data, ensure_ascii=False, indent=2)


@mcp.tool()
def translate_drawing(
    file_path: str,
    source: str = "en",
    target: str = "zh",
    engine: str = "Argos Translate (本地)",
    enable_layout_adjust: bool = True,
    oda_path: str = "",
) -> str:
    """完整管线：提取图纸文字 -> 调用翻译引擎 -> 写回文件。
    参数：file_path 图纸路径(.dwg/.dxf)；source/target 语言代码；engine 引擎名；
    enable_layout_adjust 是否启用布局自适应；oda_path 可选，指定 ODA File Converter 路径
    （仅 .dwg 需要）。返回输出文件路径、实体总数、已翻译数与日志。"""
    data = call("translate_drawing", {
        "file_path": file_path,
        "source": source,
        "target": target,
        "engine": engine,
        "enable_layout_adjust": enable_layout_adjust,
        "oda_path": oda_path,
    }, timeout=300.0)
    return json.dumps(data, ensure_ascii=False, indent=2)


if __name__ == "__main__":
    log("CADTrans Lite MCP server 启动")
    mcp.run()
