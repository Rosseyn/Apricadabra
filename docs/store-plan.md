# UV Etch / UV Print E-Commerce — Implementation Plan

> Status: **Draft for approval**. Target home: **new Bitbucket repo** (separate from Apricadabra).
> Date: 2026-05-31.
> Decision finalized: **custom app deployed to DigitalOcean** (Squarespace dropped).

## 1. Decision: custom web app, not Squarespace

**Decided:** custom app on DigitalOcean with the stack in §2. Squarespace is no longer
under consideration. The scorecard below is retained as the rationale — it failed every
requirement that makes this business distinctive (Qualpay, backend inventory validation,
the design tool, and the performance budget).

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

**Conclusion:** build a custom app on DigitalOcean. Nothing about Squarespace carries over.

### Production hardware (drives the design pipeline, §6)

- **Laser:** ComMarker Omni 1, **10 W UV galvo** laser, driven by **LightForge** software.
  Uses: light surface engraving, moderate depth removal ("shoveling"), and cutting thin
  material. Galvo optics mean the beam angle increases toward the edges of the field — this
  edge-angle matters for cut accuracy/bevel once material thickness is non-trivial.
- **Printer:** eufyMake **E1** UV flatbed, driven by **eufyMake Studio** (RIP;
  snapshot-camera alignment; Amass3D layered white-ink/texture). Has a **max print-head
  height gap** that must clear any engraved relief beneath it.

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

Fulfillment: the app emits **machine-ready files + a job sheet** against a **shared zero
point** (§6). Laser files go to **LightForge** (ComMarker Omni 1); print files go to
**eufyMake Studio** (E1). The app does not drive the machines directly — but every output
references the same standardized origin so the two machines register to one another.

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
- **Material** — name, substrate type, **thickness**, **enabled** flag (operator can hide
  troublesome materials from the customer list), `veryThin` flag (gates cut-after-print),
  **surface-energy target** + **required treatment recipe** (steps/params for the operator),
  **laser profile** (engrave/shovel/cut params per operation), **print profile** (white
  underbase, passes, Amass3D), `maxReliefDepthMm` (the unified depth/gap limit, §6),
  `skirtDefault`/`skirtAllowed` (placement-guide outline policy, §6.1a), notes.
  Backs both the **customer-facing material picker** and the **operator treatment matrix**.
- **MachineProfile** — per-machine constants. **Zero-point definition** (eufyMake E1 =
  **bottom-right**; ComMarker Omni 1 = **center**) and axis directions. For the Omni:
  **swappable lens set** (FoV **70 / 150 / 300 mm**, default **70**), each lens defining a
  square field; **radial focus-falloff model** (focus weakens toward the field edge, worst
  in the **corners** of the rectangular work area). **Doghole grid spacing** (definable
  per machine — different lasers differ) for incremental, low-handling placement. Plus
  galvo **edge-angle model**, max usable cut thickness, and the printer **max head-gap**.
  Used for validation, registration/offset math (§6.3), focus-aware placement, edge-angle
  compensation, and output generation.
- **DesignAsset** — uploaded file or chosen template; sanitized, dimensions, color profile;
  **anchored to the standardized zero point**.
- **DesignJob** — the ordered **operation pipeline** (§6) + chosen Material + computed price
  + generated preview + machine-ready artifacts (LightForge + eufyMake Studio) + job sheet.
  Stores zero-point, per-op placement (incl. distance-from-center), and relief depth.
  Belongs to a cart line / order line.
- **Cart / CartLine** — line references product/variant + optional DesignJob; reservation id.
- **Order / OrderLine** — snapshot of cart at purchase; immutable.
- **Reservation** — stock held with TTL (Redis + DB), released on expiry/cancel.
- **Payment** — Qualpay token, auth/capture ids, status.
- **Invoice** — generated per order (Qualpay invoicing or internal + PDF).

## 6. The design tool (the differentiator) — ezgif-style chained pipeline

A **DesignJob is an ordered list of operations**, each consuming the previous output —
exactly the "chain operations on the previous result" UX. The pipeline is governed by the
ordering, registration, depth, and edge-angle rules below.

### 6.1 Operation types

Both machines accept **vector and raster** on import. (One exception: the laser only uses
raster for a dedicated **photo-engraving mode** — **stubbed/ignored for now**, to add later.)

- **Laser Engrave** (ComMarker Omni 1 / LightForge) — light surface engrave through
  moderate **shovel** (depth removal). Params: zone, art (vector or raster), depth/power
  profile, grayscale→depth mapping. Depth capped by `maxReliefDepthMm` (§6.4).
- **Laser Cut** (Omni 1 / LightForge) — cut thin material to shape. Params: cut path,
  thickness (from Material), kerf, tabs. Subject to galvo edge-angle handling (§6.5).
- **UV Print** (eufyMake E1 / eufyMake Studio) — zone, art (vector or raster), CMYK +
  **white underbase**, optional **Amass3D** texture/relief. Preview = full-color composite.

### 6.1a Skirt / placement-guide outline (optional, default OFF)

A **vector trace around the design** (a "skirt") can be emitted as a registration/placement
guide — useful when **repeating a job** (for added quantity or after an error) so the piece
re-seats in exactly the same spot.

- Available for **both** machines (laser scores a light outline; printer lays a thin guide
  pass), since both benefit from the same re-registration trick.
- **Optional toggle, defaults to OFF.** It can **mar materials not meant to be cut/offcut
  in post** (e.g. scoring glass, a guide pass on canvas) — so it must be opt-in per job.
- Generated as a separate **vector layer** in the output so the operator can run or skip it
  independently; the job sheet notes whether a skirt is present and that it's a guide, not
  part of the artwork.
- Default behavior is configurable per **Material** (a material may force-disable skirt when
  it can never tolerate one).

### 6.2 Ordering rules (the engine enforces these)

- **Laser engrave goes *before* print.** Laser *after* print is avoided by default — it
  would mar the printed surface.
- **Exception — Laser Cut may be the final step *after* print, but only on `veryThin`
  materials.** Cutting the finished piece to shape is a valid last operation for thin stock.
- So the canonical chain is **engrave → print → (optional) cut-thin**. The editor offers
  **engrave only / print only / both**, and the validator rejects illegal orderings
  (e.g. print → engrave, or cut-after-print on non-thin material) with a clear reason.

### 6.3 Standardized zero point + cross-machine registration

The two machines do **not** share the same physical origin, so registration is a defined
**coordinate transform**, not a single shared point:

- **eufyMake E1 zero = bottom-right** corner of its bed.
- **ComMarker Omni 1 zero = center** of the active lens field.

The app holds **one canonical job-space origin** (and axis convention) that all
**templates** and the **DesignJob** geometry are authored against. At output time the
generator transforms job-space coordinates into each machine's native frame:
- → **eufyMake Studio** file: referenced from the **bottom-right** origin.
- → **LightForge** file: referenced from the **center** origin, for the **selected lens
  field** (70 / 150 / 300 mm).

**Why their natural placements differ — and how we exploit it:**
- **Printing favors the edge/corner.** The E1 benefits from starting at the corner of its
  area, so prints anchor near the bottom-right origin.
- **Engraving favors the center.** The Omni's focus is sharpest at field center and the
  galvo angle is smallest there, so the laser wants the artwork centered.
- For a **small** design these are reconcilable with **minimal handling**: print it close
  to the E1 origin, then move the blank to the laser positioned near the laser origin with
  roughly a **−50% (center) offset** so the same artwork lands centered in the Omni field.

**Doghole-quantized offset (operator-friendly registration):**
- The laser bed has **dogholes on a fixed grid** (spacing per `MachineProfile`, definable
  per machine). Rather than demanding an exact −50% offset, the app suggests shifting the
  fixture by a **whole number of doghole increments**, computed **independently on X and Y**
  (the snap residual has separate `dx`/`dy` components, so tolerance is evaluated per-axis,
  not as a single distance).
- The operator shifts/pins the fixture by *N* dogholes in X and *M* in Y — fast, repeatable,
  far less manual measuring — and the app folds the small per-axis residual into the laser
  file so engrave still lands over the print.
- **Encouraged, not forced.** The doghole snap is a *suggestion* surfaced through the
  **editor overlay** (§6.5 indicators): it shows the nearest doghole position, the resulting
  per-axis residual, and whether that lands in-tolerance — but the operator/user is free to
  place it elsewhere. Nothing is hard-blocked here.
- The job sheet states the exact move ("shift +2 dogholes left / +1 up") plus the per-axis
  residual.
- The overlay also indicates when the active lens is too small for the chosen position and
  suggests a **larger lens** (150 / 300 mm) or re-centering — again as guidance, not a gate.

### 6.4 Unified depth / print-gap limit

- Engrave/shovel depth and the printer's **max head-gap** are reconciled into **one**
  `maxReliefDepthMm` constant (per Material / MachineProfile), set so it satisfies *both*
  the laser's practical shovel depth and the E1's head clearance.
- Rationale: when printing **on** an engraving, the relief must clear the print head, and
  collapsing the two limits into one value saves operator time and prevents the
  "engraved too deep to print over it" failure.
- The validator blocks any pipeline whose engrave depth (where a later print covers it)
  would exceed `maxReliefDepthMm`, and the editor shows remaining depth budget live.

### 6.5 Galvo focus + edge-angle handling (lens-aware)

Two related off-center effects on the Omni, both worst in the **corners** of a rectangular
work area, both modeled per **selected lens** (70 / 150 / 300 mm field):

- **Radial focus falloff** — focus weakens as the beam approaches the field edge. Placement
  for **engrave/shovel/cut** is biased toward **center** to keep the design in the sharp
  zone; the editor shades a focus-quality heat map per lens and warns when geometry
  (especially fine engraving or thin-material cuts) strays into weak-focus corners.
- **Edge-angle (bevel)** — incidence angle grows with distance from center, beveling cut
  edges; the effect scales with **material thickness**.

For these the engine: (a) **biases placement toward field center** (reconciled with the
§6.3 doghole offset), (b) **warns** (does **not** block) when geometry's focus-quality or
thickness × off-center angle exceeds tolerance, (c) suggests a **larger lens** when the
design won't fit the sharp zone of the default 70 mm field, and (d) records expected
focus/edge-angle and the chosen lens on the **job sheet**. (Software constrains and informs;
it can't correct the optics.)

**Over-tolerance = warn + omit from output (equipment safety).** Exceeding tolerance is
never hard-blocked at the UI — the user/operator is warned but may proceed. However, any
geometry that falls outside the safe/in-tolerance envelope is **omitted from the generated
machine file** so the machine never attempts a pass that could damage equipment or material
(e.g. cutting too thick off-center, or firing in a region the lens can't safely reach). The
job sheet explicitly lists what was **dropped and why**, so the operator can re-run those
portions with a corrected setup (larger lens, re-center, thinner stock) if desired. Net:
the warning informs the human; the omission protects the hardware.

### 6.6 Weeding minimization (post-processing goal)

Bake low-weeding into output generation: prefer engrave settings/paths that leave minimal
residue, avoid tiny isolated islands, add tabs and group cut waste so it lifts out cleanly,
and surface a weeding/cleanup note on the job sheet. Treated as a first-class objective of
the artifact generator, not an afterthought.

### 6.7 Material selection + treatment matrix

- Customer picks a material from a list (eufyMake-Studio-style) showing **only `enabled`
  materials**; the operator can **disable** any material to hide it from customers when it's
  too troublesome/time-consuming.
- The chosen Material drives pricing, laser/print profiles, `maxReliefDepthMm`, `veryThin`
  cut eligibility, and the **surface-energy treatment recipe** (e.g. clean/IPA, primer,
  plasma/flame, adhesion promoter) printed on the operator job sheet so the right surface
  prep is applied before printing.

### 6.8 Authoring workflow

1. Pick a base product + **material** (defines zones, allowed ops, limits).
2. **Upload** a design (validated/sanitized) **or pick a template** — anchored to zero.
3. Add operation steps; each step's input is the previous step's composite. Choose
   **engrave / print / both** (+ optional thin cut). The validator enforces §6.2–6.5.
4. **Live preview** after each step (client canvas via Konva/Fabric; **server render is
   authoritative**), with depth-budget, lens focus-quality, and doghole-offset indicators.
5. Price updates per step via the shared `pricing` engine (base + per-op + area + material).
6. On add-to-cart, persist the **DesignJob spec**. Backend re-renders the authoritative
   preview and queues generation of **LightForge + eufyMake Studio files + a job sheet**
   (treatment recipe, weeding notes, chosen lens, focus/edge-angle, doghole move +
   residual, zero-point references) for **operator import** into each machine's software.

Extensible: new operation types (foil, etc.) plug into the same pipeline; future
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
order queue with **DesignJob preview + downloadable LightForge/eufyMake files + job sheet**,
order status transitions (received → in production → shipped), refunds (Qualpay), customer
lookup, and **template management** (templates authored against the shared zero point).
Plus **materials management**: CRUD the Material matrix, **enable/disable** materials
(toggles their visibility in the customer picker), edit per-material treatment recipes,
laser/print profiles, `maxReliefDepthMm`, and `veryThin` eligibility; and **MachineProfile**
config (zero point, field size, edge-angle model, head-gap). Built with shared `ui` lib;
lazy-loaded.

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
- **Phase 3 — Design tool:** material picker + MachineProfile/zero-point, pipeline editor
  (engrave/print/both + thin cut) with ordering/depth/edge-angle validation, live preview,
  authoritative server render, pricing, DesignJob persistence.
- **Phase 4 — Fulfillment & admin:** LightForge + eufyMake-Studio file + job-sheet
  generation (treatment recipe, weeding notes, edge-angle, zero-point), materials matrix
  admin (enable/disable), admin order queue + inventory dashboard, refunds, status workflow.
- **Phase 5 — Hardening & launch:** Lighthouse/perf budgets in CI, security review,
  load test of checkout, 3D-printed-parts product type, then launch.

## 13. Bitbucket setup (manual — no Bitbucket access from here)

1. Create the repo in Bitbucket (e.g. `uv-store`).
2. `git init` from the Nx scaffold; add Bitbucket as `origin`; push `main`.
3. Set up Bitbucket Pipelines (or GitHub Actions mirror) → deploy to DO App Platform.
4. Configure DO App Platform app spec (web/api/worker components + managed DB/Redis).

## 14. Resolved decisions (locked)

- **Integration point = operator import.** The app outputs files the operator manually
  imports into LightForge (laser) and eufyMake Studio (printer). No hot-folder/CLI assumed.
- **Zero points (asymmetric):** eufyMake E1 = **bottom-right**; ComMarker Omni 1 =
  **center of the active lens field**. Registration is a coordinate transform (§6.3), not a
  shared point.
- **Omni lenses:** 70 / 150 / 300 mm fields, **70 mm default**; focus is sharpest at center
  and falls off radially (worst in corners) — modeled per lens (§6.5).
- **Doghole-quantized offset:** print→laser offset snaps to whole doghole increments,
  computed **per-axis (X and Y independently)**; spacing per-`MachineProfile` (§6.3).
- **Snap is encouraged, not forced** — surfaced via the editor overlay (residual + in/out
  of tolerance per axis); the user may place elsewhere. Nothing hard-blocked (§6.3).
- **Over-tolerance = warn + omit from output.** Never UI-blocked; the offending geometry is
  **dropped from the generated machine file** (equipment safety) and listed on the job sheet
  with the reason (§6.5).
- **`maxReliefDepthMm`:** per-material, with a global default.

## 15. Open questions

- Invoicing: Qualpay invoicing API vs internal PDF (default: internal PDF first)?
- Brand/store name + domain.
- Tax/shipping provider (e.g. TaxJar, Shippo) — affects checkout/invoicing.
- Guest checkout vs account-required.
- Do etch and print ever target *different* base products in one order, or always one
  base per DesignJob (assumed: one base per job, multiple jobs per order).
- Both machines accept **vector + raster** on import (laser raster only for photo-engrave
  mode, deferred). Still need the exact **container formats/units** each expects (e.g. SVG/
  DXF/AI vs PNG/TIFF, mm units, origin expression) to finalize the output generator.
- Numeric **per-axis tolerance values** (the residual `dx`/`dy` thresholds that flip the
  overlay from in- to out-of-tolerance, and the focus/edge-angle limits that trigger
  omission) — behavior is decided (§6.3/§6.5); the actual numbers come from machine testing.
