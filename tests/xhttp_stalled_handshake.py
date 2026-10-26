"""Local regression: a stalled TLS handshake must not spawn one carrier per app."""
import concurrent.futures, json, pathlib, socket, subprocess, tempfile, threading, time
ROOT = pathlib.Path(__file__).resolve().parents[1]

def port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]

def run(version):
    stopped = threading.Event()
    peers = []
    server = socket.socket()
    server.bind(("127.0.0.1", 0)); server.listen(100); server.settimeout(.2)
    target = server.getsockname()[1]
    def accept():
        while not stopped.is_set():
            try: peers.append(server.accept()[0])
            except socket.timeout: pass
            except OSError: break
    worker = threading.Thread(target=accept); worker.start()
    socks = port()
    config = {"log":{"loglevel":"none"}, "inbounds":[{"listen":"127.0.0.1","port":socks,"protocol":"socks","settings":{"auth":"noauth"}}],
      "outbounds":[{"protocol":"vless","settings":{"vnext":[{"address":"127.0.0.1","port":target,"users":[{"id":"10000000-0000-0000-0000-000000000001","encryption":"none"}]}]},
        "streamSettings":{"network":"xhttp","security":"tls","tlsSettings":{"serverName":"example.com","alpn":["h2"]},"xhttpSettings":{"path":"/","mode":"packet-up","extra":{"xmux":{"maxConnections":2}}}}}]}
    proc = None
    try:
        with tempfile.TemporaryDirectory(dir=ROOT / "tests") as directory:
            cfg = pathlib.Path(directory)/"config.json"; cfg.write_text(json.dumps(config))
            proc = subprocess.Popen([str(ROOT / "vendor" / ("xray-"+version) / "xray.exe"),"run","-c",str(cfg)], stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            for _ in range(100):
                try:
                    with socket.create_connection(("127.0.0.1",socks),timeout=.1): break
                except OSError: time.sleep(.05)
            def client(_):
                with socket.create_connection(("127.0.0.1",socks),timeout=5) as c:
                    c.settimeout(6); c.sendall(bytes([5,1,0])); c.recv(2)
                    host=b"example.com"; c.sendall(bytes([5,1,0,3,len(host)])+host+bytes([0,80])); c.recv(32)
                    c.sendall(b"GET / HTTP/1.1\r\nHost: example.com\r\n\r\n")
                    try: c.recv(128)
                    except socket.timeout: pass
            with concurrent.futures.ThreadPoolExecutor(max_workers=30) as pool:
                futures=[pool.submit(client,i) for i in range(30)]
                for future in futures: future.result()
            count=len(peers)
            print(f"Xray {version}: 30 parallel sessions, {count} physical connections, alive={proc.poll() is None}",flush=True)
            return count
    finally:
        if proc is not None: proc.terminate(); proc.wait(timeout=10)
        stopped.set(); server.close(); worker.join()
        for peer in peers: peer.close()

if __name__ == "__main__":
    old=run("26.9.9")
    current=(ROOT/"scripts/xray-version.txt").read_text().strip()
    new=run(current)
    assert old>2, "Baseline did not reproduce the regression"
    assert new<=2, "XHTTP in-flight dials are not deduplicated"
