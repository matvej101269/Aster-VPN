"""Bounded throughput check through the existing VPN, without starting a peer.

Never prints runtime credentials, profile addresses or subscription data.
"""
import concurrent.futures
import http.client
import json
import os
from pathlib import Path
import socket
import ssl
import struct
import sys
import time


def exact(sock, size):
    data = bytearray()
    while len(data) < size:
        part = sock.recv(size - len(data))
        if not part:
            raise OSError("short SOCKS response")
        data.extend(part)
    return bytes(data)


def main():
    runtime = Path(os.environ["LOCALAPPDATA"]) / "AsterVPN/runtime.json"
    config = json.loads(runtime.read_text(encoding="utf-8-sig"))
    probe = next(i for i in config["inbounds"] if i["tag"] == "probe")
    user = probe["users"][0]
    tls = ssl.create_default_context()

    def connect(host):
        if "--system" in sys.argv:
            sock = socket.create_connection((host, 443), 15)
            try:
                return tls.wrap_socket(sock, server_hostname=host)
            except BaseException:
                sock.close()
                raise
        sock = socket.create_connection(("127.0.0.1", probe["listen_port"]), 15)
        try:
            sock.sendall(b"\x05\x01\x02")
            if exact(sock, 2) != b"\x05\x02":
                raise OSError("SOCKS method")
            username, password = user["username"].encode(), user["password"].encode()
            sock.sendall(b"\x01" + bytes([len(username)]) + username + bytes([len(password)]) + password)
            if exact(sock, 2) != b"\x01\x00":
                raise OSError("SOCKS auth")
            name = host.encode("idna")
            sock.sendall(b"\x05\x01\x00\x03" + bytes([len(name)]) + name + struct.pack("!H", 443))
            reply = exact(sock, 4)
            if reply[1] != 0:
                raise OSError("SOCKS connect")
            size = {1: 4, 4: 16}.get(reply[3])
            if reply[3] == 3:
                size = exact(sock, 1)[0]
            exact(sock, size + 2)
            return tls.wrap_socket(sock, server_hostname=host)
        except BaseException:
            sock.close()
            raise

    def fetch(size):
        host = "speed.cloudflare.com"
        start = time.monotonic()
        with connect(host) as sock:
            sock.sendall((f"GET /__down?bytes={size} HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n").encode())
            response = http.client.HTTPResponse(sock)
            response.begin()
            if response.status != 200:
                raise OSError(f"HTTP {response.status}")
            received = 0
            while received <= size:
                if time.monotonic() - start > 45:
                    raise TimeoutError("download budget")
                block = response.read(min(65536, size + 1 - received))
                if not block:
                    break
                received += len(block)
            if received != size:
                raise OSError("unexpected response size")
            return received

    for parallel in (1, 4):
        label = "System route" if "--system" in sys.argv else "Active VPN proxy"
        start = time.monotonic()
        try:
            with concurrent.futures.ThreadPoolExecutor(max_workers=parallel) as pool:
                results = list(pool.map(fetch, [2_000_000] * parallel))
            elapsed = time.monotonic() - start
            print(f"{label}: {parallel} stream(s), {sum(results)} bytes, {sum(results)*8/elapsed/1_000_000:.1f} Mbps, {elapsed:.2f}s", flush=True)
        except Exception as error:
            print(f"{label}: {parallel} stream(s), {type(error).__name__}", flush=True)


if __name__ == "__main__":
    main()
