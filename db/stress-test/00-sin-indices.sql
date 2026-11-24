-- Stress test, starting point: no secondary indexes. Only the primary keys stay (they back the
-- PRIMARY KEY constraints). Run as postgres or m3a_admin:
--   kubectl -n m3a exec -i m3a-db-0 -- psql -U postgres -d m3a_db < db/stress-test/00-sin-indices.sql
DROP INDEX IF EXISTS "IX_tickets_EventId";
DROP INDEX IF EXISTS "IX_tickets_EventId_Status";
DROP INDEX IF EXISTS "IX_events_VenueId";
ANALYZE;
