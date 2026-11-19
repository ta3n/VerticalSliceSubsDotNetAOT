"""Release pinning must include every service and fail closed if any digest is missing."""
import hashlib
import os
import re
import subprocess
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path


class Registry(BaseHTTPRequestHandler):
    invalid_digest = False
    connect_targets = []

    def do_HEAD(self):
        self.send_response(200)
        digest = hashlib.sha256(self.path.encode()).hexdigest()
        self.send_header("Docker-Content-Digest", "invalid" if self.invalid_digest else f"sha256:{digest}")
        self.end_headers()

    def do_CONNECT(self):
        self.connect_targets.append(self.path)
        self.send_error(502)

    def log_message(self, *_):
        pass


server = HTTPServer(("127.0.0.1", 0), Registry)
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
try:
    proxy = f"http://127.0.0.1:{server.server_port}"
    registry = proxy.removeprefix("http://")
    command = [str(Path(__file__).with_name("image-values.sh").resolve()), registry, "release-42", "custom-api", "custom-migrator", "custom-web"]
    result = subprocess.run(command, text=True, capture_output=True, check=True)
    repositories = re.findall(r'repository: "([^"]+)"', result.stdout)
    assert repositories == [f"{registry}/{name}" for name in ["custom-api", "custom-migrator", "custom-web", "subscription-management-billing", "subscription-management-payments", "subscription-management-relay"]]
    assert len(re.findall(r'digest: "sha256:[a-f0-9]{64}"', result.stdout)) == 6
    assert result.stdout.count('tag: "release-42"') == 6
    assert "frontend:\n  image:" in result.stdout and "s2s:\n  billing:\n    image:" in result.stdout
    Registry.invalid_digest = True
    result = subprocess.run(command, text=True, capture_output=True)
    assert result.returncode != 0 and "Missing/invalid digest" in result.stderr
    command[3] = 'bad"repository'
    result = subprocess.run(command, text=True, capture_output=True)
    assert result.returncode != 0 and "Invalid image reference" in result.stderr
    Registry.invalid_digest = False
    command[1] = "registry.example"
    command[3] = "custom-api"
    env = dict(os.environ, http_proxy=proxy, https_proxy=proxy, no_proxy="", NO_PROXY="")
    result = subprocess.run(command, env=env, text=True, capture_output=True)
    assert result.returncode != 0 and "Missing/invalid digest" in result.stderr
    assert Registry.connect_targets == ["registry.example:443"]
    print("Image release values: six digests, repository overrides, failure checks and HTTPS for remote registries passed.")
finally:
    server.shutdown()
    server.server_close()
    thread.join()
