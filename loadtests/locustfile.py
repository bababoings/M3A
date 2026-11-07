"""End-to-end load test for the M3A Ticketing API.

Every simulated user repeatedly runs one complete journey that touches all 18 endpoints:

     1. GET    /health/live, /health/ready
     2. POST   /venues                      create a venue
     3. GET    /venues/{id}, PUT /venues/{id}, GET /venues
     4. POST   /events                      create an event -> issues `capacity` tickets
     5. GET    /events/{id}, GET /events
     6. GET    /tickets                     find this event's issued tickets
     7. DELETE /tickets/{id}                free one place ...
     8. POST   /tickets                     ... and issue a ticket into it
        POST   /tickets                     again -> 422 sold out        (expected rule)
     9. GET    /tickets/{id}
    10. POST   /tickets/{id}/redeem         before purchase -> 422       (expected rule)
        POST   /tickets/{id}/purchase, /redeem
    11. PUT    /events/{id}                 grow capacity -> more tickets issued
    12. DELETE /events/{id}                 with sold tickets -> 422     (expected rule)
        DELETE sold tickets, DELETE /events/{id}, DELETE /venues/{id}

The journey cleans up everything it created, so the database (and therefore the cost of
GET /tickets, which has no filter) stays bounded however long the test runs.

Run from the repository root (see loadtests/README.md):

    locust -f loadtests/locustfile.py --host http://127.0.0.1:8080
"""

from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone

from locust import HttpUser, between, task

EVENT_CAPACITY = 3
GROWN_CAPACITY = 5
VENUE_CAPACITY = 100


class JourneyFailed(Exception):
    """Raised when a step fails, so the rest of the journey is skipped."""


class TicketingJourneyUser(HttpUser):
    """A user that runs the full venue -> event -> ticket lifecycle, then cleans up."""

    wait_time = between(1, 3)

    @task
    def full_journey(self) -> None:
        created: dict = {"venue": None, "event": None, "sold": []}
        try:
            self._journey(created)
        except JourneyFailed:
            pass  # the failing request is already recorded in the statistics
        finally:
            self._cleanup(created)
            # Kubernetes Services balance per TCP connection, not per request. Dropping the
            # keep-alive connection makes the next journey reconnect, so it can land on Pods
            # the HPA added since this user started instead of staying pinned to the first one.
            self.client.close()

    # ------------------------------------------------------------------ journey

    def _journey(self, created: dict) -> None:
        tag = uuid.uuid4().hex[:8]

        # 1. Health
        self._call("GET", "/health/live", "/health/live", expect=200, json_body=False)
        self._call("GET", "/health/ready", "/health/ready", expect=200, json_body=False)

        # 2-3. Venue lifecycle
        venue = self._call(
            "POST", "/venues", "/venues", expect=201,
            json={"location": f"Load Test Arena {tag}", "capacity": VENUE_CAPACITY},
        )
        created["venue"] = venue["id"]
        venue_path = f"/venues/{venue['id']}"

        self._call("GET", venue_path, "/venues/{id}", expect=200)
        updated_venue = self._call(
            "PUT", venue_path, "/venues/{id}", expect=200,
            json={"location": f"Load Test Arena {tag} (renamed)", "capacity": VENUE_CAPACITY + 50},
        )
        self._check(updated_venue["capacity"] == VENUE_CAPACITY + 50, "venue capacity not updated")
        self._call("GET", "/venues", "/venues", expect=200)

        # 4-5. Event lifecycle
        event_date = (datetime.now(timezone.utc) + timedelta(days=30)).isoformat()
        event = self._call(
            "POST", "/events", "/events", expect=201,
            json={
                "name": f"Load Test Event {tag}",
                "dateTime": event_date,
                "eventCapacity": EVENT_CAPACITY,
                "venueId": venue["id"],
            },
        )
        created["event"] = event["id"]
        event_path = f"/events/{event['id']}"

        fetched = self._call("GET", event_path, "/events/{id}", expect=200)
        self._check(fetched["venue"]["id"] == venue["id"], "event did not include its venue")
        self._call("GET", "/events", "/events", expect=200)

        # 6. The event's inventory was issued on creation
        tickets = self._event_tickets(event["id"])
        self._check(len(tickets) == EVENT_CAPACITY,
                    f"expected {EVENT_CAPACITY} issued tickets, found {len(tickets)}")
        self._check(all(t["status"] == "Issued" for t in tickets), "new tickets not Issued")

        # 7-8. Free a place, issue a ticket into it, then confirm the event is sold out again
        self._call("DELETE", f"/tickets/{tickets[-1]['id']}", "/tickets/{id}", expect=204, json_body=False)
        new_ticket = self._call("POST", "/tickets", "/tickets", expect=201, json={"eventId": event["id"]})
        self._call("POST", "/tickets", "/tickets [sold out]", expect=422, json={"eventId": event["id"]})

        # 9. Read it back
        self._call("GET", f"/tickets/{new_ticket['id']}", "/tickets/{id}", expect=200)

        # 10. Status transitions: Issued -> Purchased -> Redeemed, out of order is rejected
        first = tickets[0]["id"]
        self._call("POST", f"/tickets/{first}/redeem", "/tickets/{id}/redeem [not purchased]", expect=422)
        purchased = self._call("POST", f"/tickets/{first}/purchase", "/tickets/{id}/purchase", expect=200)
        self._check(purchased["status"] == "Purchased", "ticket not Purchased")
        created["sold"].append(first)
        redeemed = self._call("POST", f"/tickets/{first}/redeem", "/tickets/{id}/redeem", expect=200)
        self._check(redeemed["status"] == "Redeemed", "ticket not Redeemed")

        self._call("POST", f"/tickets/{new_ticket['id']}/purchase", "/tickets/{id}/purchase", expect=200)
        created["sold"].append(new_ticket["id"])

        # 11. Growing capacity issues the extra tickets
        self._call(
            "PUT", event_path, "/events/{id}", expect=200,
            json={
                "name": f"Load Test Event {tag} (updated)",
                "dateTime": event_date,
                "eventCapacity": GROWN_CAPACITY,
                "venueId": venue["id"],
            },
        )
        self._check(len(self._event_tickets(event["id"])) == GROWN_CAPACITY,
                    f"expected {GROWN_CAPACITY} tickets after growing capacity")

        # 12. An event with sold tickets cannot be deleted (cleanup handles the real deletes)
        self._call("DELETE", event_path, "/events/{id} [has sold tickets]", expect=422)

    # ------------------------------------------------------------------ cleanup

    def _cleanup(self, created: dict) -> None:
        """Delete sold tickets, then the event (removes unsold tickets), then the venue."""
        for ticket_id in created["sold"]:
            self._call("DELETE", f"/tickets/{ticket_id}", "/tickets/{id}", expect=204,
                       json_body=False, raise_on_failure=False)
        if created["event"]:
            self._call("DELETE", f"/events/{created['event']}", "/events/{id}", expect=204,
                       json_body=False, raise_on_failure=False)
        if created["venue"]:
            self._call("DELETE", f"/venues/{created['venue']}", "/venues/{id}", expect=204,
                       json_body=False, raise_on_failure=False)

    # ------------------------------------------------------------------ helpers

    def _event_tickets(self, event_id: str) -> list[dict]:
        # There is no ?eventId= filter, so this lists every ticket in the system.
        all_tickets = self._call("GET", "/tickets", "/tickets", expect=200)
        return [t for t in all_tickets if t["eventId"] == event_id]

    def _call(self, method: str, path: str, name: str, *, expect: int,
              json: dict | None = None, json_body: bool = True,
              raise_on_failure: bool = True):
        """Sends a request, grouped under `name` in the stats, and fails it unless the
        status is `expect`. Expected 4xx answers (business rules) count as successes."""
        with self.client.request(method, path, name=name, json=json, catch_response=True) as response:
            if response.status_code != expect:
                detail = response.text[:200] if response.text else ""
                response.failure(f"expected {expect}, got {response.status_code} {detail}")
                if raise_on_failure:
                    self._fail()
                return None

            body = None
            if json_body and response.status_code < 300:
                try:
                    body = response.json()
                except ValueError:
                    response.failure("response body is not JSON")
                    if raise_on_failure:
                        self._fail()
                    return None
            response.success()
            return body

    def _check(self, condition: bool, message: str) -> None:
        """Records a failed assertion as its own entry in the statistics."""
        if not condition:
            self.environment.events.request.fire(
                request_type="CHECK", name=message, response_time=0,
                response_length=0, exception=AssertionError(message), context={},
            )
            self._fail()

    @staticmethod
    def _fail() -> None:
        raise JourneyFailed()
