import socket, json, sys

HOST, PORT = "127.0.0.1", 8090

def send(cmd, params=None, timeout=10.0):
    payload = (json.dumps({"command": cmd, "params": params or {}}) + "\n").encode()
    with socket.create_connection((HOST, PORT), timeout=5) as s:
        s.settimeout(timeout)
        s.sendall(payload)
        buf = b""
        while b"\n" not in buf:
            c = s.recv(65536)
            if not c:
                break
            buf += c
        return json.loads(buf.split(b"\n", 1)[0])

for cmd, params in [
    ("list_engines", {}),
    ("get_status", {}),
    ("translate_text", {"text": "Hello world", "source": "en", "target": "zh", "engine": "Argos Translate (本地)"}),
]:
    try:
        r = send(cmd, params)
        print(f"=== {cmd} ===")
        print(json.dumps(r, ensure_ascii=False, indent=2)[:800])
    except Exception as e:
        print(f"=== {cmd} ERROR: {e}")
