"""Local test console for the M3A API.

Serves index.html and forwards every /api/* request to the API, so the browser only ever
talks to one origin and the API needs no CORS configuration.

    python3 ui/server.py                          # http://localhost:5090 -> http://localhost:5080
    API_URL=http://localhost:5000 PORT=8000 python3 ui/server.py
"""

import os
import urllib.error
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

API_URL = os.environ.get("API_URL", "http://localhost:5080").rstrip("/")
PORT = int(os.environ.get("PORT", "5090"))
ROOT = os.path.dirname(os.path.abspath(__file__))


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=ROOT, **kwargs)

    def _proxy(self):
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length) if length else None
        request = urllib.request.Request(API_URL + self.path[len("/api"):], data=body, method=self.command)
        if self.headers.get("Content-Type"):
            request.add_header("Content-Type", self.headers["Content-Type"])

        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                status, headers, payload = response.status, response.headers, response.read()
        except urllib.error.HTTPError as error:  # 4xx/5xx are answers to show, not failures
            status, headers, payload = error.code, error.headers, error.read()
        except OSError as error:
            status, headers = 502, {"Content-Type": "application/problem+json"}
            payload = (
                '{"title":"API unreachable","status":502,"detail":"%s"}'
                % f"Could not reach {API_URL}: {error}".replace('"', "'")
            ).encode()

        self.send_response(status)
        for name in ("Content-Type", "Location"):
            if headers.get(name):
                self.send_header(name, headers[name])
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def _route(self):
        if self.path.startswith("/api/"):
            self._proxy()
        elif self.command == "GET":
            super().do_GET()
        else:
            self.send_error(405)

    do_GET = do_POST = do_PUT = do_DELETE = _route

    def log_message(self, fmt, *args):
        if self.path.startswith("/api/"):
            super().log_message(fmt, *args)


if __name__ == "__main__":
    print(f"M3A test console: http://localhost:{PORT}  (API: {API_URL})")
    ThreadingHTTPServer(("localhost", PORT), Handler).serve_forever()
