# UV Etch / UV Print E-Commerce — Implementation Plan

> Status: **Draft for approval**. Target home: **new Bitbucket repo** (separate from Apricadabra).
> Date: 2026-05-31.

## 1. Decision: custom web app, not Squarespace

Squarespace covers the commodity commerce layer well but fails every requirement that
makes this business distinctive. Scorecard:

| Requirement | Squarespace | Custom build |
| --- | --- | --- |
| Catalog / product pages / accounts / order history / invoicing | ✅ native | ✅ build |
| Inventory management | 🟡 basic, no reservation hooks | ✅ full control |
| **Payments via Qualpay** | ❌ Stripe/PayPal/Square only | ✅ Qualpay Embedded Fields |
| **Backend inventory validation before payment** | ❌ checkout rejects code | ✅ core of flow |
| **Customer design upload tied to product/cart** | ❌ upload only in disconnected Form Blocks | ✅ first-class |
| **Template picker + interactive editor** | ❌ no app runtime | ✅ canvas editor |
| **ezgif-style chained engrave→print workflow** | ❌ | ✅ pipeline model |
| **FCP / small lazy bundles / cache by category** | ❌ unoptimized injection | ✅ full control |

**Conclusion:** build a custom app. Squarespace is only viable as a throwaway placeholder
catalog; nothing carries over.

## 2. Stack (confirmed)

- **Frontend:** Angular (19+) with SSR + (incremental) hydration, standalone components,
  route-level lazy loading, `@defer` blocks, Angular PWA service worker.
- **Backend:** Node + **NestJS** (TypeScript). Shared DTO/types with the frontend.
- **Payments:** **Qualpay** — Embedded Fields (iframe) for card capture → tokenize →
  server-side authorize. Keeps cardholder data off our servers (low PCI scope) while
  customers stay on-site.
- **Hosting:** **DigitalOcean App Platform** (managed). Components: web (SSR), api,
  worker, managed **Postgres**, managed **Redis**, **Spaces** (S3-compatible) + CDN for
  assets/uploads/renders.
- **Repo layout:** **Nx monorepo** so Angular app, NestJS API, and shared libs
  (DTOs, validation schemas, pricing rules) live together and stay type-safe.

## 3. High-level architecture

```
Browser (Angular SSR + PWA)
   │  HTTPS / REST (+ SSE for render progress)
   ▼
NestJS API  ──► Postgres (catalog, inventory, orders, users, designs, jobs)
   │            Redis    (sessions, cart reservations w/ TTL, render queue)
   │            Spaces   (uploads, template assets, generated previews, print files)
   ├──► Qualpay API (tokenize → authorize → capture → refund; invoices)
   └──► Render worker (sharp / ImageMagick / vector tooling)
                 generates authoritative previews + print-ready artifacts
```

Fulfillment: the app emits **print-ready files + a job sheet**; the operator imports
them into **eufyMake Studio** (RIP for the E1, snapshot-camera alignment, Amass3D
white-ink/texture layering). The app does not drive the printer directly.

## 4. Monorepo structure (Nx)

```
apps/
  web/            Angular storefront + design editor + account area
  admin/          Angular admin (or lazy-loaded area in web behind role guard)
  api/            NestJS REST API
  worker/         NestJS render/fulfillment worker (BullMQ on Redis)
libs/
  shared-types/   DTOs, enums, zod schemas shared FE/BE
  pricing/        deterministic pricing engine (used FE for estimates, BE authoritative)
  design-spec/    design pipeline model + validation
  ui/             shared Angular components / design system
```

## 5. Data model (core entities)

- **User** (auth, roles: customer/admin), **Address**.
- **Product** — type (`blank`, `finished`, `custom-base`), category, media, base price,
  decoration zones, allowed operations (etch / print / both).
- **Variant / SKU** — attributes (size, material), price delta.
- **InventoryItem** — stock-on-hand, reserved, reorder threshold; tracks **blanks/materials
  and consumables** (a custom product decrements its blank + consumables).
- **DesignAsset** — uploaded file or chosen template; sanitized, dimensions, color profile.
- **DesignJob** — the ordered **operation pipeline** (§6) + computed price + generated
  preview + print-ready artifacts. Belongs to a cart line / order line.
- **Cart / CartLine** — line references product/variant + optional DesignJob; reservation id.
- **Order / OrderLine** — snapshot of cart at purchase; immutable.
- **Reservation** — stock held with TTL (Redis + DB), released on expiry/cancel.
- **Payment** — Qualpay token, auth/capture ids, status.
- **Invoice** — generated per order (Qualpay invoicing or internal + PDF).

## 6. The design tool (the differentiator) — ezgif-style chained pipeline

A **DesignJob is an ordered list of operations**, each consuming the previous output —
exactly the "chain operations on the previous result" UX.

Operation types:
- **UV Laser Etch** — params: target zone, art (vector preferred), engrave depth/power
  profile, grayscale→depth mapping. Preview = monochrome/relief simulation.
- **UV Print (eufyMake E1)** — params: zone, art (raster/vector), CMYK + **white
  underbase**, optional **Amass3D texture/relief** layers. Preview = full-color composite.

Workflow:
1. Pick a base product (defines decoration zones + allowed ops).
2. **Upload** a design (validated/sanitized) **or pick a template**.
3. Add operation steps; each step's input is the previous step's composite. Choose
   **etch only / print only / both** (ordering matters — e.g. etch then print).
4. **Live preview** after each step (client canvas via Konva/Fabric for interactivity;
   **server render is authoritative**).
5. Price updates per step via the shared `pricing` engine (base + per-op + area/material).
6. On add-to-cart, persist the **DesignJob spec** (operation list, placements, assets,
   machine params). Backend re-renders authoritative preview + queues generation of
   **print-ready artifacts + job sheet** for fulfillment.

Extensible: new operation types (e.g. cut, foil) plug into the same pipeline; future
non-decorated goods simply have an empty pipeline.

## 7. Inventory + checkout + payment flow (hard requirement)

Three checkpoints, backend-authoritative:

1. **Entering cart view** — query live stock for every line; flag shortfalls in UI.
2. **Checkout start** — re-check; create **reservations with TTL** (Redis + DB row),
   block checkout on shortfall.
3. **Before payment (backend, authoritative)** — inside a DB transaction:
   re-validate + decrement/confirm reservation with row locks → **Qualpay authorize**
   → on success capture/confirm order; **on any failure release reservation**. No
   payment is ever authorized without a held, validated reservation.

Idempotency keys on the payment endpoint prevent double-charge on retry.

## 8. Invoicing

Per-order invoice generated on completion. Option A: Qualpay invoicing API. Option B:
internal invoice model + server-rendered **PDF** stored in Spaces, emailed to customer
and visible in account → order history. Start with B for control; layer A if Qualpay's
invoicing fits the accounting workflow.

## 9. Performance strategy (FCP / interaction / cache-by-category)

- **SSR + hydration** so first paint is server HTML; hydrate incrementally.
- **Route-level lazy loading** per top-level area (storefront, category, product,
  design editor, account, checkout, admin) → small initial bundle.
- **`@defer`** the heavy **design editor** (canvas libs) and below-the-fold blocks,
  with `on viewport` / `on interaction` triggers.
- **Service worker caches by browsing category** — prefetch + cache category bundles and
  product data as the user browses, so subsequent category nav is instant.
- **Image CDN (Spaces + CDN)**, responsive `srcset`, AVIF/WebP, lazy images.
- Performance budget enforced in CI (Lighthouse CI: FCP, TTI, bundle size gates).

## 10. Admin interface

Role-guarded area: product/variant CRUD, inventory adjustments + low-stock dashboard,
order queue with **DesignJob preview + downloadable print files + job sheet**, order
status transitions (received → in production → shipped), refunds (Qualpay), customer
lookup, template management. Built with the shared `ui` lib; lazy-loaded.

## 11. Security & compliance

- Card data via **Qualpay Embedded Fields iframe** → tokenization → server authorize
  (minimizes PCI scope; aim SAQ-A / A-EP).
- **Upload hardening:** type/size validation, image re-encoding/sanitization, EXIF strip,
  malware scan, render in isolated worker; serve from CDN, never execute.
- Auth: hashed credentials (argon2), httpOnly cookies, CSRF protection, rate limiting.
- Secrets in DO App Platform env/secret store; least-privilege DB roles.

## 12. Phased roadmap

- **Phase 0 — Foundations:** Bitbucket repo, Nx scaffold, CI/CD to DO App Platform,
  Postgres/Redis/Spaces provisioned, auth + accounts.
- **Phase 1 — Catalog & cart:** products/variants/inventory, storefront with SSR +
  lazy loading + SW caching, cart with checkpoint #1.
- **Phase 2 — Checkout & Qualpay:** reservations, the 3-checkpoint flow, Qualpay
  embedded fields + authorize/capture, order creation, order history, invoicing (PDF).
- **Phase 3 — Design tool:** upload + templates, pipeline editor (etch/print/both),
  live preview, authoritative server render, pricing, DesignJob persistence.
- **Phase 4 — Fulfillment & admin:** print-ready artifact + job-sheet generation,
  admin order queue + inventory dashboard, refunds, status workflow.
- **Phase 5 — Hardening & launch:** Lighthouse/perf budgets in CI, security review,
  load test of checkout, 3D-printed-parts product type, then launch.

## 13. Bitbucket setup (manual — no Bitbucket access from here)

1. Create the repo in Bitbucket (e.g. `uv-store`).
2. `git init` from the Nx scaffold; add Bitbucket as `origin`; push `main`.
3. Set up Bitbucket Pipelines (or GitHub Actions mirror) → deploy to DO App Platform.
4. Configure DO App Platform app spec (web/api/worker components + managed DB/Redis).

## 14. Open questions

- Invoicing: Qualpay invoicing API vs internal PDF (default: internal PDF first)?
- Brand/store name + domain.
- Tax/shipping provider (e.g. TaxJar, Shippo) — affects checkout/invoicing.
- Guest checkout vs account-required.
- Do etch and print ever target *different* base products in one order, or always one
  base per DesignJob (assumed: one base per job, multiple jobs per order).
