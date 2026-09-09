# BigFourApp — Product Spec

This document describes the **features and behavior** of the existing
BigFourApp (an event-ticketing web app, à la Ticketmaster) so it can be
rebuilt from scratch with a different stack while preserving the same
functionality. It intentionally avoids naming the current implementation's
frameworks, libraries, or storage engines — those are being replaced. What
matters is the behavior and rules below, not how the old version implemented
them.

## Domain summary

A ticketing platform with three actors:

- **Guest** — browses events, cannot buy or manage anything.
- **User** (authenticated) — browses events, selects seats, pays, receives
  e-tickets/receipts by email, views purchase history and notifications.
- **Event Manager** (authenticated, role-based) — creates/edits/cancels their
  own events, defines venue seating layout and pricing, uploads event/seatmap
  images. A manager only ever sees and edits events they own.

Core end-to-end flow: **browse events → view event detail → pick seats on an
interactive seat map → cart → checkout via hosted payment page → paid →
tickets/receipt generated → confirmation email sent → tickets appear in "My
Tickets" → reminder email sent automatically before the event starts.**

## Feature list

### Authentication & authorization
- Email/password registration and login (no external OAuth providers).
- Password policy is intentionally relaxed: no digit/uppercase/lowercase/
  special-character requirement (min length only) — a deliberate choice to
  keep signup friction low; revisit if that's no longer desired.
- Two roles: `User` and `EventManager`, seeded on first run if missing.
- Role-gated areas: the event-manager dashboard/CRUD is restricted to the
  `EventManager` role; checkout, tickets, and notifications require any
  authenticated user.
- Login/register pages share the same visual design/layout as the rest of
  the site (this consistency was a specific late fix — don't regress it by
  letting auth pages look like a generic default template again).

### Event catalog
- Home page lists all non-cancelled events, ordered by date, each showing
  venue name/city/state and a classification (segment/genre/sub-genre —
  modeled after Ticketmaster's taxonomy).
- Event detail page shows full event info, venue, and classification.
- The catalog can be pre-populated from a bundled fixture/seed data set on
  first run, in addition to events created by managers, so the app isn't
  empty out of the box.
- An event can be marked **cancelled**, which hides it from seat
  selection/checkout (soft-delete, not a hard delete — historical
  tickets/sales stay intact and queryable).

### Seating & venue model
- A **Venue** belongs to an event and has one or more **Sections**, each
  with: code, display name, base price, seat count, seats-per-row.
- Concrete **Seats** are generated from the section definitions — not
  hand-entered one by one. Regenerating an event's layout (e.g. after an
  edit) must preserve seats that are already sold/held and only add/remove
  from the *available* pool.
- Seat price is **not flat per section** — it's row-aware: rows closer to
  the "front" (lower row number) are priced higher than back rows, computed
  as `basePrice + (rowsFromBack * adjustment)`, floored at a minimum price.
  This produces a realistic front-is-pricier seat map from a small set of
  inputs (base price + seat count + seats-per-row) instead of requiring
  per-seat pricing data entry.
- Seat states: Available / Occupied. Occupied seats are never re-offered.
- If every seat for an event is taken, users get a dedicated **Sold Out**
  page instead of an empty seat map.
- The seat map groups seats into sections → rows → seats for rendering, and
  supports an optional venue seatmap background image.

### Cart & checkout
- Seat selection produces a cart of chosen seats (label + price per line
  item, subtotal). Cart state only needs to survive the checkout flow itself
  — it is not a persistent, resumable entity tied to the user's account.
- Checkout collects buyer name and payment method, then hands off to a
  **hosted payment page** for the actual card payment — the app itself
  never touches raw card data.
- On successful payment, the server must independently re-verify the
  payment actually succeeded (never trust a client-side success redirect
  alone) before: marking the purchased seats Occupied, creating a Sale
  record, one line item per seat, and one Ticket per seat (each ticket
  gets a unique code, e.g. for QR/lookup) — then show a receipt.
- A cancelled/abandoned payment redirects home with a cancellation notice;
  no order or tickets are created.
- After purchase, the user can also trigger a "resend receipt to my email"
  action on demand — this should also log a notification record for the
  user.

### Tickets ("My Tickets")
- Authenticated users can view a list of everything they've purchased
  (across all events/sales), newest first, and drill into a single ticket's
  detail (seat, event, venue, sale info) — must only ever show the current
  user's own purchases.

### Notifications
- An in-app notification history per user (e.g. "receipt", "reminder"
  types) with a message and timestamp.
- Users can view their notification history and delete individual entries.
- A **recurring background job** scans upcoming tickets: any ticket for an
  event starting within the next 30 minutes that hasn't been notified yet
  gets an automatic "your event is starting soon" reminder email, and is
  flagged so it's never sent twice.

### Event Manager dashboard
- Manager-only dashboard lists only the events *that manager owns*, with a
  per-event summary: name, date, venue, city/state, cancelled flag, cover
  image, section count, total seat count, and live available-seat count.
- Create/Edit event form captures: name, URL/slug, date + time (validated
  strictly), classification (segment/genre/sub-genre), a "safe/verified
  ticket" boolean flag, cancelled flag, venue name/city/state, and a dynamic
  list of sections (name, code, base price, seat count, seats-per-row).
- Editing an event regenerates its seat inventory to match the new section
  definitions without destroying already-sold seats (see seating model
  above).
- Managers can upload a **seatmap background image** and an **event cover
  image**; only one cover image is kept per event (a new upload replaces
  and removes the old one from storage). Removing an image is supported
  from the edit form.
- "Cancel event" is a soft action (sets a flag), not a delete, and only the
  owning manager can do it.
- Ownership is enforced at the data-access level everywhere — a manager can
  never see or edit another manager's events, even by guessing/forging an
  event ID.

### File / image storage
- Uploaded images (event cover, seatmap) go through a storage abstraction
  with two interchangeable backends, selected automatically based on
  configuration:
  - a **cloud object storage** backend when configured, producing a public,
    permanent URL per file.
  - a **local fallback** for development when no cloud storage is
    configured — this must not require any cloud account to develop the
    app day to day.
- Uploads are organized under a folder-per-purpose convention (e.g. one
  folder for event covers, one for seatmaps) with generated, non-guessable,
  collision-proof filenames — never the user-supplied original filename.
- Replacing or removing an image also deletes the old file from storage so
  it doesn't accumulate orphaned uploads.

### Payments
- Payment processor credentials (publishable/public key + secret key) are
  read from configuration and must never be committed to source control —
  populated via a local secrets mechanism in dev and a real secret store in
  production.
- Uses a **hosted checkout page** (redirect flow) rather than embedding raw
  card-collection UI in the app — minimizes PCI scope and frontend payment
  code.
- The server always re-verifies payment status server-side on the
  success redirect before granting tickets.

### Email
- Transactional email (purchase receipt, upcoming-event reminder) is sent
  through an SMTP-capable mail account using host/port/credentials from
  configuration.
- Email sending is abstracted behind a simple interface so the transport can
  be swapped later without touching the code that calls it.

### Client-side / UI behavior worth preserving
- Interactive seat map: click to select/deselect seats, with a running
  subtotal.
- Client-side generation of a downloadable PDF receipt after purchase.
- A consistent shared layout/nav across all pages, including the auth
  pages — don't regress this.
- Distinct pages for: event list, event detail, seat selection, sold-out,
  cart, payment summary, receipt, my tickets (list + detail), notification
  history, manager dashboard, manager create/edit event form.

## Data model (entities to preserve, names can change)

- **User** — email/password login, roles, owns Sales, owns Notifications,
  owns Events (if manager).
- **Event** — id, name, url/slug, date, seatmap image url, cover image url,
  "safe/verified ticket" flag, cancelled flag, owning manager, one Venue,
  Classifications, Seats.
- **Venue** — name, city, state, belongs to one Event, has many Sections.
- **VenueSection** — code, display name, base price, seat count, seats per
  row; belongs to a Venue.
- **Classification** — segment/genre/sub-genre; belongs to an Event.
- **Seat** — event id, section id, seat number, state (Available/Occupied).
- **Sale** — user id, date, payment method, total; has many line items.
- **SaleLineItem** — sale id, seat id, ticket id, quantity, unit price.
- **Ticket** — unique code, "should still notify" flag; belongs to one
  SaleLineItem (i.e. one ticket per purchased seat).
- **Notification** — user id, message, type, timestamp.

## Non-functional characteristics to keep

- **Local-first dev experience**: the app must run locally with no cloud
  credentials — schema setup and seed data load automatically on startup;
  storage/email/payments degrade gracefully or are easily stubbed when not
  configured.
- **Secrets never committed**: payment processor keys, mail password,
  storage connection string are placeholders in committed config and
  supplied via a local secrets mechanism/environment/secret manager.
- **Authorization is enforced on every query at the data layer**, not just
  hidden in the UI (especially: manager-owns-event, user-owns-ticket/sale/
  notification).
- **Idempotent-ish notifications**: never email the same reminder twice for
  the same ticket.
- **Soft cancellation** everywhere instead of destructive deletes for
  events, so historical sales/tickets remain valid records.

## Explicitly out of scope / known gaps in the original (don't carry over blindly)

- A "Create Account" navigation entry point existed as a dead placeholder
  with no real functionality — not a feature to reproduce.
- Only a single event cover image is supported (no multi-image gallery),
  despite the edit-form data shape suggesting multiple — decide fresh
  whether multi-image is worth doing properly this time instead of
  half-supporting it.
- No automated test suite existed.
- No admin/moderation role beyond "manager owns their own events" — there's
  no platform-wide admin.
