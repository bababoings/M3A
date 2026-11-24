-- Stress test, optimization 1: indexes for every query that filters by a foreign key.
--   kubectl -n m3a exec -i m3a-db-0 -- psql -U postgres -d m3a_db < db/stress-test/01-indices.sql
--
-- tickets ("EventId", "Status") serves, with one index:
--   * SELECT count(*) ... WHERE "EventId" = $1                 (POST /tickets capacity check)
--   * SELECT count(*) ... WHERE "EventId" = $1 AND "Status" <> $2  (sold tickets, PUT/DELETE event)
--   * SELECT ...          WHERE "EventId" = $1 AND "Status" = $2   (unsold tickets to remove)
--   * SELECT ...          WHERE "EventId" = $1                 (GET /tickets?eventId=)
--   * the FK check on DELETE FROM events (tickets referencing the event)
-- It makes a separate single-column index on "EventId" redundant (leading column).
-- events ("VenueId") serves the FK check on DELETE FROM venues and lookups of a venue's events.
CREATE INDEX IF NOT EXISTS "IX_tickets_EventId_Status" ON tickets ("EventId", "Status");
CREATE INDEX IF NOT EXISTS "IX_events_VenueId" ON events ("VenueId");
ANALYZE;
