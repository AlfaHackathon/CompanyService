# Current-company knowledge and source declarations

Additive CompanyService facade, base `485cbbdd`. Existing `BaseAuthController` receives the gateway's trusted identity; gateway/network/authentication policy is unchanged. Context remains internal. No model, embedding, parser or acquisition call is introduced.

## Five public operations

| Method and path | Access | Result |
| --- | --- | --- |
| GET `/company-service/companies/me/knowledge` | Current member | Current company profile at server time, explicit public projection |
| PUT `/company-service/companies/me/knowledge` | Current creator only | Immutable sourced profile revision |
| GET `/company-service/companies/me/materials?limit=20&cursor=…` | Current member | Bounded canonical company-material metadata page |
| POST `/company-service/companies/me/materials` | Current creator only | New immutable **user declaration**, not verified banking evidence |
| GET `/company-service/companies/me/materials/{materialId}/versions/{versionId}` | Current member + exact source scope | Exact permitted company original and safe provenance |

All use current membership from local `ICompanyRepository.GetByUserAsync`, including receipt replay. Missing membership/profile/source is 404. Noncreator writes are 403. Knowledge source references are re-resolved on GET too: a previously saved internal profile does not bypass current company/bank ACL. Responses carry `Cache-Control: private, no-store`. Bearer validation remains in the existing gateway; existing Company authentication requires valid trusted identity. This patch does not make direct internet access to trusted-header endpoints safe.

PUT and POST require exactly one `Idempotency-Key`: 1–128 visible ASCII characters, no spaces. Successful writes return 200. Missing/bad key, unknown JSON fields, unknown/repeated query parameters or bounds violations return 400. A key reused with different normalized input is 409. Dependency/SQL failures return sanitized 503; they never become empty success. A 503 write can have an unknown outcome: retry identical input with the **same key**.

```http
POST /company-service/companies/me/materials
Authorization: Bearer <user-token-through-existing-gateway>
Idempotency-Key: declaration-example-1
Content-Type: application/json

{"title":"Описание зависимости","text":"Пользователь сообщил о конкретной операционной зависимости."}
```

Title is nonblank, <=1000 UTF-16 units; text is nonblank, <=200000. The HTTP body is limited to 1 MiB. The actual downstream JSON is preflighted against the same 1 MiB bound **before reservation**; default JSON escaping can make large Cyrillic inputs exceed it even within the character bound. Split a document through a separately designed ingestion flow rather than assuming truncation.

Server generates material/version IDs and time. It fixes `kind=company`, `sourceId=user-declaration`, `companyIds=[currentCompany]`, `bankProfileIds=[]`, `provenance={acquisition:uploaded,sourceUrl:null,retrievedAt:serverTime,textCompleteness:unknown,originVersionId:null,locator:null}`. Clients cannot supply company IDs, source/version IDs, content hash, dates, banking scope or provenance. Context's immutable writer assigns the canonical hash. This endpoint accepts text declarations, not files or URLs, and does not claim exposure was independently verified.

```http
PUT /company-service/companies/me/knowledge
Idempotency-Key: profile-example-1
Content-Type: application/json

{"description":"Введённое пользователем описание компании","industryCodes":["45.11"],"exposures":["Зависимость из собственного материала"],"sourceRefs":[{"materialId":"<returned-material-id>","versionId":"<returned-version-id>"}]}
```

Description is nonblank, <=16000. Industry codes: <=64 nonblank entries, <=100 each. Exposures: <=64 nonblank entries, <=1000 each. Sources: 1–64 exact own-company references; client provides IDs only, server resolves hash. Arrays are normalized, deduplicated and sorted for input hashing. Company name and bank bridge come from the operational company, not client payload. Revision ID/time are server assigned. All references must resolve to `kind=company`, match exact IDs/hash/time and current bank scope. A public market/news source alone cannot establish this company profile. The immutable existing `KnowledgeStore.Save` writes the revision and change event together; source checks run in the new public use case with its HTTP budget rather than repeating the old unbounded internal resolver.

GET exact accepts company material only; foreign-company, public-news/market and foreign-bank materials are 404 before projection. Public responses exclude company/bank grant arrays and internal receipt payloads. Safe provenance permits HTTP(S) source URLs without userinfo; unsafe URLs and internal locator content are omitted/null. Existing immutable sources with absent provenance remain unknown.

## List dependency and snapshot

Internal Context dependency:

```text
GET /context-service/internal/companies/{trustedCompany}/materials
    ?asOf=<server ISO instant with exact ticks>&limit=1..20
    [&cursor=<Context cursor>][&bankProfileId=<current membership bridge>]
```

Context returns `{companyId,asOf,items,nextCursor,coverage:"canonical_changes_only"}`. Metadata contains material/version/source IDs, kind, title, knownAt/publishedAt/correctsVersionId, hash, provenance/completeness and `latestVersionCount`. It contains no original text or grants. Context selects exact company material plus empty bank scope or the trusted current bridge; absent bridge permits only empty bank scope. It freezes the canonical change watermark, retains ambiguous latest versions and bounds pages. Canonical change coverage excludes manually inserted legacy rows without canonical change events; it does not claim complete business evidence.

Public pagination wraps Context's opaque cursor with server-created asOf/current company/bank bridge. Subsequent requests preserve that snapshot and recheck current membership; bridge/company changes invalidate the cursor. No public companyId/asOf/bankProfileId query is accepted. New list dependency must be deployed before these list reads; unavailable is 503, never an empty corpus. Existing exact read/write contracts stay unchanged.

## Durable write receipts and recovery

Migration `20261025001` creates `company_public_knowledge_operations`. Receipt identity is `(actor, company, operation, Idempotency-Key)` with SHA-256 of normalized client input. It freezes server IDs, operation time and full outbound payload **before any external write**. The first reservation wins; concurrent losers load its payload. Reservations use a short local transaction and existing knowledge advisory lock; no transaction spans HTTP. Per-company timestamps advance past existing saved profiles/reservations; a future legacy profile/reservation blocks new reservation with 503 for reconciliation instead of creating a future-dated public success.

Material replay sends the same immutable Context write after an unknown outcome. Every successful response must match all frozen input fields and a valid canonical hash before completion. Completed replay reads the exact source and rechecks current scope. Profile replay validates every frozen reference and writes the same immutable revision. Completion is monotonic; a late failure cannot replace completed with failed. No auto-retry with regenerated IDs, time or a new key is performed. There is no automatic compensation/delete after a lost response. Receipts are durable and contain user material text; protect/retain them as company data, not application logs. No receipt or secret is returned to clients.

Source HTTP reads use one 15-second request budget and per-call timeout; decoded upstream responses are bounded at 2 MiB. SQL package cancellation is unchanged; reservation lock timeout is 5 seconds. Cancellation/timeout can race an already committed immutable write; replay is the recovery mechanism.

## Verification and native gates

`Tests/PublicKnowledgeTests.cs` and `ContextCompanyMaterialsClientTests.cs` use only fake dependencies. They cover normalized replay/key conflict, lost response, current creator/membership, exact source identity/company/bank/hash/time, unsafe configuration, bounded input, truthful provenance, cursor scope/snapshot and direct controller query/cache behavior. `KnowledgeOperationStoreTests` is a native PostgreSQL gate and explicitly requires `COMPANY_TEST_POSTGRES` for a synthetic instance; it creates/deletes its own test database and covers concurrent receipt reservation/completion plus legacy future times.

No org feed retry or credentials are needed for source review. Full native restore/build/test is pending in the authorized package-enabled executor; known cloud org-feed HTTP 403 is not bypassed by alternate feeds. Run there:

```sh
# Use the executor's already configured authorized org feed; this repo has no NuGet.config.
dotnet restore CompanyService.slnx
dotnet build CompanyService.slnx -c Release --no-restore
# COMPANY_TEST_POSTGRES must identify only the authorized synthetic test instance.
dotnet test CompanyService.slnx -c Release --no-restore
dotnet publish Api/Api.csproj -c Release --no-restore -o ./publish
```

This source slice does not deploy, merge, change gateway/ACL/network ports, or run live models. Deploy compatible Context list first, then CompanyService including migration. Rollback leaves immutable source/profile history and receipts intact; do not remove them automatically.
