# Database (local kind cluster)

Files:
- `init.sql` — creates `m3a_admin`, `m3a_svc` (both password `password`, local only),
  the `m3a_db` database and the `venues` / `events` / `tickets` tables.
- `postgres.yml` — headless Service + StatefulSet `m3a-db` in namespace `m3a`.

## Deploy

Run from the repo root, against the kind cluster created from `kind.yaml`.

```sh
# 1. Namespace
kubectl create namespace m3a --dry-run=client -o yaml | kubectl apply -f -

# 2. Superuser password — generated, never committed
kubectl -n m3a get secret m3a-db-superuser >/dev/null 2>&1 || \
  kubectl -n m3a create secret generic m3a-db-superuser \
    --from-literal=password="$(openssl rand -hex 16)"

# 3. Initialization script
kubectl -n m3a create configmap m3a-db-init \
  --from-file=01-m3a.sql=db/init.sql \
  --dry-run=client -o yaml | kubectl apply -f -

# 4. Postgres
kubectl apply -f db/postgres.yml
kubectl -n m3a rollout status statefulset/m3a-db --timeout=180s
kubectl -n m3a logs m3a-db-0
```

Inside the cluster the API connects with `DB_HOST=m3a-db.m3a.svc.cluster.local`,
`DB_PORT=5432`, `DB_NAME=m3a_db`, `DB_USERNAME=m3a_svc`, `DB_PASSWORD=password`.

From your machine: `kubectl -n m3a port-forward svc/m3a-db 5432:5432`.

## Notes

- **Do not set `POSTGRES_DB=m3a_db`** — `init.sql` creates the database itself and would
  fail on `CREATE DATABASE`.
- Initialization runs **only on the first start with an empty data directory**. Editing
  `init.sql` and re-applying the ConfigMap does nothing to an existing database; to
  re-initialize, delete the data:
  `kubectl -n m3a delete statefulset m3a-db && kubectl -n m3a delete pvc data-m3a-db-0`, then
  re-apply.
- Data survives Pod restarts, but **deleting the kind cluster deletes its local storage**.
  This is not a backup.
