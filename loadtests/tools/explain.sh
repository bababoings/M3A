#!/usr/bin/env bash
# EXPLAIN (ANALYZE, BUFFERS) of the queries the journey runs that filter by a foreign key, against
# a seeded event with many tickets. Read-only except the DELETE, which runs in a rolled-back
# transaction.   loadtests/tools/explain.sh > loadtests/results/<run>/explain.txt
set -euo pipefail
psql() { kubectl -n m3a exec -i m3a-db-0 -- psql -U postgres -d m3a_db "$@"; }
ev=$(psql -tAc "select \"EventId\" from tickets group by 1 order by count(*) desc limit 1")
psql -v ev="$ev" <<'SQL'
\echo '== indexes'
select indexname, indexdef from pg_indexes where schemaname='public' order by 1;
\echo '== POST /tickets: capacity check'
explain (analyze, buffers, costs off) select count(*)::int from tickets t where t."EventId" = :'ev';
\echo '== PUT/DELETE /events: sold tickets'
explain (analyze, buffers, costs off) select count(*)::int from tickets t where t."EventId" = :'ev' and t."Status" <> 'Issued';
\echo '== PUT/DELETE /events: unsold tickets to remove'
explain (analyze, buffers, costs off) select t."Id", t."EventId", t."Status" from tickets t where t."EventId" = :'ev' and t."Status" = 'Issued';
\echo '== GET /tickets?eventId='
explain (analyze, buffers, costs off) select t."Id", t."EventId", t."Status" from tickets t where t."EventId" = :'ev';
\echo '== GET /tickets (unfiltered)'
explain (analyze, buffers, costs off) select t."Id", t."EventId", t."Status" from tickets t;
\echo '== DELETE /events: FK check on tickets (rolled back; event without tickets)'
begin;
insert into events ("Id","Name","DateTime","EventCapacity","VenueId")
  select 'explain-probe', 'probe', now(), 1, "Id" from venues limit 1;
explain (analyze, buffers, costs off) delete from events where "Id" = 'explain-probe';
rollback;
SQL
