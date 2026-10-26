# Canonical company bank context

The confirmed Company source header has **24 fields**: `SYNTHETIC_U` maps through an external stable mapping to the existing Company UUID; the other 23 fields are preserved in `CompanyBankData`. This change provides storage/contracts, not a CSV importer or mapping registry. It creates no operational company, fabricated name/creator, account, credentials, membership, parent relationship or access grant. Existing `CompanyProfile.Name/Description` and auth/public creation contracts are unchanged.

## Complete source fields

| Source | BankData property | Type |
|---|---|---|
| ORG_TYPE | orgType | string/null |
| OKFS_NAME | okfsName | string/null |
| COMPANY_AGE_YEARS_GRADATION | companyAgeYearsGradation | string/null |
| ECO_MARK_LABEL | ecoMarkLabel | string/null |
| MAINOKVED_CODE | mainOkvedCode | string/null |
| MAINOKVED_NAME | mainOkvedName | string/null |
| NEGATIVEMARK | negativeMark | string/null |
| AMOUNT_WORKERS_FNS_GRADATION | amountWorkersFnsGradation | string/null |
| AMOUNT_WORKERS_SPARK_GRADATION | amountWorkersSparkGradation | string/null |
| CL_TYPE | clType | string/null |
| CL_PRODUCT_RKO_MARK | clProductRkoMark | string/null |
| CL_PRODUCT_GAR_MARK | clProductGarMark | string/null |
| CL_PRODUCT_LOA_MARK | clProductLoaMark | string/null |
| CL_PRODUCT_LEA_MARK | clProductLeaMark | string/null |
| CL_PRODUCT_DEP_MARK | clProductDepMark | string/null |
| CL_PRODUCT_ZPP_MARK | clProductZppMark | string/null |
| FTS_FLAG | ftsFlag | string/null |
| FTS_COUNTRY_NAME | ftsCountryName | string/null |
| FACTORING_MARK | factoringMark | string/null |
| REVENUE_COMPANY | revenueCompany | exact decimal string/null |
| GROUP_ID | groupId | mapped UUID/null |
| CITY | city | string/null |
| REGION | region | string/null |

Opaque codes/flags/gradations, including empty strings, are retained without enum/boolean guessing. Numeric revenue uses a signed base-10 decimal string, including leading/trailing zeros and arbitrary precision; no double conversion, rounding, default currency/year/period or derivation from payments. Empty or whitespace-only revenue remains its exact source string as an unknown raw value, distinct from numeric zero and null; it is never trimmed or interpreted as an amount. Other malformed decimals are rejected rather than silently converted: comma/scientific formats need an explicit source contract before import. Numeric/full `mainOkvedCode` remains distinct from operational industry sections A–U. Group UUID is not `ParentCompanyId` and creates no ownership/grant. Region/city are declared context and cannot by themselves prove geographical exclusion.

## Explicit immutable capture and reads

All new routes require the existing `X-Internal-Secret`; there is no public ingress/security setting change.

- `POST /company-service/internal/bank-profiles/{companyId}/capture` body `{ "captureId": "opaque-key", "bankData": { ... } }`. Unknown body properties are rejected. CaptureId is 1–128 printable ASCII characters. The operation freezes actual server time and complete typed data; it does not require an operational company row. Maximum request is 1 MiB and must fit existing Context material limits.
- `GET /company-service/internal/bank-profiles/{companyId}?asOf=<ISO timestamp>` reads the newest version known by that time; required `asOf`, 404 means no canonical profile at that time. There is no latest/current operational fallback and no capture on GET.
- `GET /company-service/internal/bank-profiles/versions?limit=100&after=<cursor>` returns `{profiles:[...],nextCursor:string|null}`. Limit is 1–100; first request omits cursor. Cursor is an increasing append-sequence decimal string, avoiding JavaScript integer precision loss. Every version is included in stable sequence order; consumers follow pages until null, including an empty terminal page when the prior page had exactly `limit` records. This supports the full source population without an unbounded version read.

Response `CompanyBankProfile` is `{companyId,revisionId,knownAt,bankData,sourceId,sourceRefs,provenance}`. `sourceId` is `company-bank-profile`; snapshot material text is exactly serialized `{companyId,bankData}`, kind `company`, companyIds `[companyId]`, bankProfileIds `[]`. Existing Context provenance is `event_payload`, actual retrievedAt=knownAt, full text, null sourceUrl/originVersionId/locator. It describes this internal capture, not an invented CSV file origin. Exactly one Context material reference binds the exact normalized envelope hash. The producer verifies returned identity, scope, time, text, marker and hash before owner persistence.

The existing operation journal freezes the source/profile identity, data and time. Retry an uncertain outcome with the **same captureId and body**; changing the body under that key returns409. Material publication may succeed before an owner write fails; it is not a confirmed owner profile until the owner commit succeeds. Existing exact revision/hash replay precedes CAS. New revisions atomically compare the latest committed owner revision under the existing advisory lock, without a frozen-time cutoff; stale reverse commit order returns409 and requires a new captureId. A replay of an already-written earlier version does not replace a later current head.

Migration `20261026001` adds `company_bank_profiles`: immutable revisions with Company UUID, unique revision/time, JSONB full bank data/payload and an append sequence. It has **no operational/auth foreign key**, so incomplete canonical-only companies remain representable. Existing operational Company/CompanyResponse read nullable latest BankData through a left join; legacy company rows without a bank profile still return null. Parent/child previews in the existing group response return BankData=null: a group listing does not grant another company's bank data. Legacy `BankProfileId` remains a compatibility field and is not the new canonical source identity.

These captures are declared context. Consumers must apply a code guard for `company-bank-profile` to prevent promotion into documentary financial exposure/economic objects. All flags/unknowns remain opaque; main activity/region/city may guide relevance without hard exclusions. ContextService owns the separate bank-profile search projection; it uses this Company UUID as recipient identity.

## Verification and pending source contracts

Native DB-free fixtures: `CompanyBankDataTests`, `CompanyBankProfileCaptureTests`, `CompanyBankProfileStoreTests`. The last exercises actual writer dispatch/lock/hash-before-CAS behavior with fake persistence commands. No external database is needed by these selected tests; existing PostgreSQL-dependent classes are excluded from this task's gate. Package-free actual DTO and Company/Context serialization/hash verification is supplemental, not a full API build.

Payments remain out of implementation until exact headers arrive. Confirmed semantics for the later slice: payerCompanyId/beneficiaryCompanyId are mapped Company UUIDs; amount/count retain exact decimal/integer strings; currency RUB; accumulation since January 1 with year/end unknown; immutable source/version/asOf; self-transfer is derived from equal UUIDs, not a supplier or revenue classification. No unknown payment columns, company exposure, accounting revenue, currency/year or supplier relationships are invented here.
