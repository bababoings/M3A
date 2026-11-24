"""Seeds the M3A API with a large data set, so load tests run against realistic table sizes.

Everything goes through the public API, so every business rule (capacity <= venue capacity,
future dates, Issued -> Purchased -> Redeemed) holds for the seeded rows exactly as it does
for real traffic. Standard library only — no extra dependencies.

    python3 loadtests/seed.py                                  # kind: http://127.0.0.1:8080
    python3 loadtests/seed.py --host http://localhost:5080     # dotnet run
    python3 loadtests/seed.py --venues 50 --events 150         # smaller run

What it creates (defaults):
  * --venues   venues, capacity 200-2000, located in a set of real cities
  * --events   events spread over the venues, dated 1-365 days ahead; each POST /events issues
               its whole inventory (one ticket per place, capacity 50-300 by default)
  * --purchase fraction of all tickets purchased, and --redeem fraction of those redeemed

Seeded venues have a location starting with "Seed · ", which is how the script detects an
earlier run: it stops instead of doubling the data unless --append is given. Locust journeys
only delete what they created, so the seed survives load tests.

Note: GET /tickets has no paging or filter and every Locust journey calls it, so the ticket
count directly sets the size of that response. Tune --min-capacity/--max-capacity to taste.
"""

from __future__ import annotations

import argparse
import json
import random
import sys
import time
import urllib.error
import urllib.request
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone

SEED_PREFIX = "Seed · "

CITIES = [
    "Mexico City, CDMX", "Guadalajara, JAL", "Monterrey, NL", "Puebla, PUE", "Querétaro, QRO",
    "Mérida, YUC", "Tijuana, BC", "León, GTO", "Cancún, QROO", "Oaxaca, OAX",
    "Austin, TX", "Chicago, IL", "Denver, CO", "Seattle, WA", "Miami, FL",
    "Madrid, ES", "Barcelona, ES", "Buenos Aires, AR", "Bogotá, CO", "Santiago, CL",
]
VENUE_KINDS = ["Arena", "Auditorio", "Stadium", "Theater", "Hall", "Forum", "Pavilion", "Club"]
ACTS = [
    "The Midnight Owls", "Orquesta Solar", "Neon Coyotes", "Los Vientos", "Paper Satellites",
    "Ruby Canyon", "Marea Alta", "Static Bloom", "Jaguar Jazz Trio", "Glass Harbor",
    "Cumbia Cósmica", "Northern Lights FC", "Ballet Folclórico", "Comedy Night Live",
    "Silver Lining Quartet", "Desert Echoes", "La Sonora Nueva", "Velvet Machines",
]
FORMATS = ["Live", "World Tour", "Unplugged", "Festival", "Anniversary Show", "Final Match",
           "Gala", "Matinee", "Late Show", "Season Opener"]


class Api:
    def __init__(self, host: str, timeout: float) -> None:
        self.host = host.rstrip("/")
        self.timeout = timeout

    def call(self, method: str, path: str, body: dict | None = None, *, expect: int = 200,
             retries: int = 3):
        data = json.dumps(body).encode() if body is not None else None
        for attempt in range(1, retries + 1):
            request = urllib.request.Request(
                self.host + path, data=data, method=method,
                headers={"Content-Type": "application/json", "Accept": "application/json"})
            try:
                with urllib.request.urlopen(request, timeout=self.timeout) as response:
                    payload = response.read()
                    if response.status != expect:
                        raise RuntimeError(f"{method} {path}: expected {expect}, got {response.status}")
                    if "json" not in response.headers.get("Content-Type", ""):
                        return payload.decode(errors="replace")  # e.g. /health returns text
                    return json.loads(payload) if payload else None
            except urllib.error.HTTPError as error:
                detail = error.read().decode(errors="replace")[:300]
                # 4xx is a real answer (business rule / validation) — retrying won't change it.
                if error.code < 500 or attempt == retries:
                    raise RuntimeError(f"{method} {path}: {error.code} {detail}") from None
            except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
                if attempt == retries:
                    raise RuntimeError(f"{method} {path}: {error}") from None
            time.sleep(0.5 * attempt)
        raise AssertionError("unreachable")


def run_parallel(label: str, jobs: list, worker, workers: int) -> list:
    """Runs worker(job) for every job on a thread pool, printing progress; returns the results
    of the jobs that succeeded and reports (but tolerates) the ones that failed."""
    results, failures = [], []
    total = len(jobs)
    step = max(1, total // 20)
    with ThreadPoolExecutor(max_workers=workers) as pool:
        futures = [pool.submit(worker, job) for job in jobs]
        for done, future in enumerate(as_completed(futures), start=1):
            try:
                results.append(future.result())
            except Exception as error:  # noqa: BLE001 — summarised below
                failures.append(str(error))
            if done % step == 0 or done == total:
                print(f"\r  {label}: {done}/{total}", end="", flush=True)
    print()
    if failures:
        print(f"  {len(failures)} {label} failed, e.g.: {failures[0]}", file=sys.stderr)
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--host", default="http://127.0.0.1:8080")
    parser.add_argument("--venues", type=int, default=100)
    parser.add_argument("--events", type=int, default=300)
    parser.add_argument("--min-capacity", type=int, default=50, help="tickets per event, min")
    parser.add_argument("--max-capacity", type=int, default=300, help="tickets per event, max")
    parser.add_argument("--purchase", type=float, default=0.35, help="fraction of tickets purchased")
    parser.add_argument("--redeem", type=float, default=0.2, help="fraction of purchases redeemed")
    parser.add_argument("--workers", type=int, default=16, help="concurrent requests")
    parser.add_argument("--timeout", type=float, default=60.0, help="per-request timeout, seconds")
    parser.add_argument("--seed", type=int, default=42, help="random seed, for repeatable data")
    parser.add_argument("--append", action="store_true", help="seed even if seed data exists")
    args = parser.parse_args()

    if not 0 < args.min_capacity <= args.max_capacity:
        parser.error("need 0 < --min-capacity <= --max-capacity")
    if args.max_capacity > 2000:
        parser.error("--max-capacity cannot exceed the largest venue capacity (2000)")

    rng = random.Random(args.seed)
    api = Api(args.host, args.timeout)
    started = time.monotonic()

    # ------------------------------------------------------------------ preflight
    try:
        api.call("GET", "/health/ready")
    except RuntimeError as error:
        print(f"API not ready at {args.host}: {error}", file=sys.stderr)
        return 1

    existing = [v for v in api.call("GET", "/venues") if v["location"].startswith(SEED_PREFIX)]
    if existing and not args.append:
        print(f"{len(existing)} seeded venues already exist at {args.host}; "
              "pass --append to add more anyway.", file=sys.stderr)
        return 1

    # ------------------------------------------------------------------ venues
    print(f"Seeding {args.host}")
    venue_specs = [
        {"location": f"{SEED_PREFIX}{rng.choice(VENUE_KINDS)} {i + 1:04d}, {rng.choice(CITIES)}",
         "capacity": rng.randint(max(200, args.max_capacity), 2000)}
        for i in range(args.venues)
    ]
    venues = run_parallel("venues", venue_specs,
                          lambda spec: api.call("POST", "/venues", spec, expect=201), args.workers)
    if not venues:
        return 1

    # ------------------------------------------------------------------ events
    now = datetime.now(timezone.utc)
    event_specs = []
    for i in range(args.events):
        venue = rng.choice(venues)
        when = now + timedelta(days=rng.randint(1, 365), hours=rng.randint(0, 23))
        when = when.replace(minute=rng.choice([0, 30]), second=0, microsecond=0)
        event_specs.append({
            "name": f"{rng.choice(ACTS)} — {rng.choice(FORMATS)} #{i + 1}",
            "dateTime": when.isoformat(),
            "eventCapacity": rng.randint(args.min_capacity, min(args.max_capacity, venue["capacity"])),
            "venueId": venue["id"],
        })
    events = run_parallel("events", event_specs,
                          lambda spec: api.call("POST", "/events", spec, expect=201), args.workers)
    event_ids = {e["id"] for e in events}

    # ------------------------------------------------------------------ tickets
    # POST /events issued every ticket already; there's no per-event filter, so list them all.
    print("  listing tickets...")
    by_event: dict[str, list[str]] = defaultdict(list)
    for ticket in api.call("GET", "/tickets"):
        if ticket["eventId"] in event_ids and ticket["status"] == "Issued":
            by_event[ticket["eventId"]].append(ticket["id"])
    issued = sum(len(ids) for ids in by_event.values())

    to_purchase = []
    for ids in by_event.values():
        # Vary demand per event: some nearly sold out, some barely touched.
        share = min(1.0, max(0.0, rng.gauss(args.purchase, args.purchase / 2)))
        to_purchase += rng.sample(ids, round(len(ids) * share))
    purchased = run_parallel(
        "purchases", to_purchase,
        lambda tid: api.call("POST", f"/tickets/{tid}/purchase", expect=200), args.workers)

    to_redeem = [t["id"] for t in purchased if rng.random() < args.redeem]
    redeemed = run_parallel(
        "redemptions", to_redeem,
        lambda tid: api.call("POST", f"/tickets/{tid}/redeem", expect=200), args.workers)

    # ------------------------------------------------------------------ summary
    elapsed = time.monotonic() - started
    print(f"\nDone in {elapsed:.0f}s: {len(venues)} venues, {len(events)} events, "
          f"{issued} tickets ({len(purchased)} purchased, {len(redeemed)} of them redeemed).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
