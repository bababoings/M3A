"""Breaking-point (stress) test for the M3A Ticketing API.

Runs the same end-to-end journey as `locustfile.py`, but drives it with a stepped ramp instead of
a fixed user count, with shorter think time and a per-request timeout, so load keeps rising until
the deployment fails instead of stopping at a level it handles comfortably.

    .venv-loadtests/bin/locust -f loadtests/stress.py StressJourneyUser \
        --host http://127.0.0.1:8080 --headless --processes 4 \
        --stop-timeout 30s --csv <dir>/locust --html <dir>/locust-report.html

The user class name must be given on the command line: the module imports
`TicketingJourneyUser`, and naming the class keeps Locust from running that one as well.

Tunables (environment variables):
    STRESS_STEPS        comma-separated user counts, one per step (default 10,25,50,100,200,300,400)
    STRESS_STEP_SECONDS seconds per step                         (default 90)
    STRESS_SPAWN_RATE   users started per second when stepping up  (default 10)
    STRESS_TIMEOUT      per-request timeout in seconds             (default 10)
    STRESS_HOST_HEADER  Host header to send, e.g. m3a.local to match the Traefik Ingress rule
                        without an /etc/hosts entry (default: none, derived from --host)
"""

from __future__ import annotations

import os
import sys

from locust import LoadTestShape, between

sys.path.insert(0, os.path.dirname(__file__))
from locustfile import TicketingJourneyUser  # noqa: E402

STEPS = [int(s) for s in os.getenv("STRESS_STEPS", "10,25,50,100,200,300,400").split(",")]
STEP_SECONDS = int(os.getenv("STRESS_STEP_SECONDS", "90"))
SPAWN_RATE = float(os.getenv("STRESS_SPAWN_RATE", "10"))
TIMEOUT = float(os.getenv("STRESS_TIMEOUT", "10"))
HOST_HEADER = os.getenv("STRESS_HOST_HEADER")


class StressJourneyUser(TicketingJourneyUser):
    """Same journey, about a quarter of the think time, and requests that give up after TIMEOUT s.

    Without a timeout a saturated API makes users hang forever and the offered load silently drops;
    with one, an overloaded request is recorded as a failure and the user moves on.
    """

    wait_time = between(0.25, 0.75)

    def on_start(self) -> None:
        if HOST_HEADER:
            self.client.headers["Host"] = HOST_HEADER
        request = self.client.request

        def with_timeout(method, url, **kwargs):
            kwargs.setdefault("timeout", TIMEOUT)
            return request(method, url, **kwargs)

        self.client.request = with_timeout


class SteppedRamp(LoadTestShape):
    """Holds each user count in STEPS for STEP_SECONDS, then stops."""

    def tick(self):
        step = int(self.get_run_time() // STEP_SECONDS)
        if step >= len(STEPS):
            return None
        return STEPS[step], SPAWN_RATE
