# Canonical payment rows and observed activity

The supplied source contract is exactly `BENEFICIARY;PAYER;PAY_AMT;PAY_CNT`. Beneficiary/payer are already mapped Company UUIDs. This slice stores every source row, including repeated directed company pairs. It does not read/import a dataset, create company/auth/membership records, infer suppliers/economic objects or accounting revenue, or connect Context/Agent/model execution.

## Explicit row write

`POST /company-service/internal/payment-rows` requires the existing `X-Internal-Secret` and a strict body with nine fields:

```json
{
  "importId": "caller-supplied-import",
  "sourceId": "caller-supplied-logical-source",
  "sourceVersionId": "caller-supplied-version",
  "sourceContentHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "sourceRowId": "caller-supplied-part-and-original-row-identity",
  "beneficiaryCompanyId": "11111111-1111-4111-8111-111111111111",
  "payerCompanyId": "22222222-2222-4222-8222-222222222222",
  "payAmt": "0001000.00000000000000000001",
  "payCnt": "0001"
}
```

The example metadata and values are synthetic. Real source IDs/hashes must be supplied by the caller; this code does not invent a manifest, hash a dataset, parse CSV, or implement an import/session platform. `sourceContentHash` is the lowercase SHA256 of an immutable **logical source version**, which may be one artifact or a caller-supplied manifest covering multiple parts. The same source/version can therefore contain rows from every part. `sourceRowId` is opaque stable original row identity, not a business-pair key; it may encode part+row. Caller must reuse original source identity, row identity and import metadata on retry.

All nine fields are retained in the immutable raw JSON payload; amount/count strings preserve lexical zeros, signs and scale. UUID values remain typed UUIDs. Codes are not guessed from source keys. The response is `{sourceRaw:<all nine fields>,knownAt:<server UTC time>,watermark:<sequence string>,selfTransfer:<derived equality of party UUIDs>}`.

Metadata identities are nonblank opaque strings, at most200 characters (row identity512), without control characters; no trimming occurs. Hash is exactly64 lowercase hex characters. Amount is a signed plain decimal string (including plus/minus and variable scale); count is a nonnegative integer string, with zero allowed. Comma/exponent, numeric JSON, missing/blank values, suffix whitespace and LF/CRLF are rejected. Maximum4096 digits per value and a32KiB HTTP body are explicit request resource bounds, not the observed sample's min/max/scale or a business limit. No `.NET decimal`, `double`, numeric rounding, or fixed sample scale is used.

`company_payment_source_versions` binds sourceId/version to exactly one supplied logical content hash. `company_payment_rows` is unique only on `(source_id,source_version_id,source_row_id)`; there is **no unique payer/beneficiary pair**, amount-based deduplication, or business-key summing at write time. Different original rows with the same parties and values remain separate. Exact row/body replay returns its original knownAt/watermark before reading a new clock. Conflicting body/import metadata under a source row, or a different source hash under the same source version, returns409. Retry an unknown outcome with the identical nine fields.

Binding/replay/server-derived monotonic knowledge time/insert run under the existing owner advisory lock in one transaction. Later new rows receive knowledge ticks strictly after existing rows; a backwards server clock fails instead of backdating. Neither party needs an operational/auth row. The additive migration is `20261026002`; it introduces no company/auth foreign keys or new credentials/grants. Its only foreign key is between the source-version and owned row tables.

## One exact source-version activity read

`GET /company-service/internal/payment-activity/{companyId}?sourceId=...&sourceVersionId=...&asOf=<ISO timestamp>` requires all query fields and the existing internal secret. There is no global/default version selection. One SQL statement reads version hash, source knowledge time, observed maximum knowledge time, selected row count/watermark and all totals from one MVCC snapshot, restricted to the exact source/version and rows known by asOf. Reimports/new versions are never mixed implicitly. No observed selected-company row returns404/unknown, not a fabricated zero activity result.

Response fields:

- `companyId,sourceId,sourceVersionId,sourceContentHash,asOf` identify the exact selected scope.
- `currency="RUB"`; `period={basis:"since_january_1",year:null,end:null}`. These semantics were supplied by the parent from webinar p20. Calendar year/end are not inferred from asOf or import time and are not source-row fields.
- `incoming,outgoing,selfTransfer` each contain string `payAmt,payCnt,observedRowCount`. Incoming/outgoing exclude self rows; self-transfer totals are separate. Each observed row belongs to exactly one bucket for the selected company, including pair-duplicate rows.
- `coverage={complete:null,observedRowCount:<string>,watermark:<string>}`. Completeness is unknown: source hash/manifest identity alone does not prove all parts/rows have been observed. A zero bucket is only a zero observed sum within this partial scope.
- `provenance={sourceKnownAt,observedKnownAt,basis:"observed_source_rows"}`. Row watermark is the maximum selected observed sequence, encoded as a string; source hash is caller-asserted immutable logical source identity, not an independently fetched artifact verification.

Totals use PostgreSQL unbounded `NUMERIC` sums with `::text` output for both amount and count. Canonical row strings remain unchanged. Self transfer is a mechanical UUID comparison, not a financial classification. Activity totals never assert company revenue, supplier relationships, economic-object identity, profit, credit exposure or payment-period completeness.

## Verification

DB-free native fixtures are `PaymentRowValidationTests`, `PaymentRowStoreTests` (actual dispatch/transaction ordering with fake persistence commands), and `PaymentActivityControllerTests` (in-process MVC with fake rows). Native organization packages are a separate gate; package-free actual DTO/lexical tests and root's embedded actual SQL checks are supplemental, not a full API build. This task starts no server, PostgreSQL container, external database or live model.
