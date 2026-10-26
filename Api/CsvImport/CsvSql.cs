namespace Api.CsvImport;

public static class CsvSql
{
    public const string Schema = """
CREATE TABLE csv_import_versions(source_id text NOT NULL, version_id text NOT NULL, manifest_hash text NOT NULL,
 manifest_json text NOT NULL, PRIMARY KEY(source_id,version_id));
CREATE TABLE csv_import_batches(source_id text NOT NULL, version_id text NOT NULL, phase text NOT NULL,
 start_row bigint NOT NULL, end_row bigint NOT NULL, request_hash text NOT NULL, receipt_json text NOT NULL,
 PRIMARY KEY(source_id,version_id,phase,start_row),
 FOREIGN KEY(source_id,version_id) REFERENCES csv_import_versions(source_id,version_id));
CREATE TABLE csv_import_rows(source_id text NOT NULL, version_id text NOT NULL, phase text NOT NULL,
 row_number bigint NOT NULL, entity_id text NOT NULL, company_id uuid, person_id uuid, group_id uuid,
 known_ticks bigint NOT NULL, raw_json jsonb NOT NULL, row_hash text NOT NULL, material_ref jsonb NOT NULL,
 PRIMARY KEY(source_id,version_id,phase,row_number),
 FOREIGN KEY(source_id,version_id) REFERENCES csv_import_versions(source_id,version_id));
CREATE INDEX ix_csv_import_entity ON csv_import_rows(source_id,version_id,phase,entity_id);
CREATE TABLE csv_import_verified(source_id text NOT NULL, version_id text NOT NULL, phase text NOT NULL,
 manifest_hash text NOT NULL, receipt_json text NOT NULL, PRIMARY KEY(source_id,version_id,phase),
 FOREIGN KEY(source_id,version_id) REFERENCES csv_import_versions(source_id,version_id));
""";
    public const string Replay = "SELECT COALESCE((SELECT receipt_json FROM csv_import_batches WHERE source_id=@source AND version_id=@version AND phase=@phase AND start_row=@start),'')";
    public const string Bind = """
INSERT INTO csv_import_versions VALUES(@source,@version,@manifestHash,@manifestJson) ON CONFLICT DO NOTHING;
""";
    public const string Bound = "SELECT manifest_hash=@manifestHash AND manifest_json=@manifestJson FROM csv_import_versions WHERE source_id=@source AND version_id=@version";
    public const string Next = "SELECT COALESCE(MAX(end_row),0)+1 FROM csv_import_batches WHERE source_id=@source AND version_id=@version AND phase=@phase";
    public const string Checkpoint = "INSERT INTO csv_import_batches VALUES(@source,@version,@phase,@start,@end,@requestHash,@receipt)";
    public const string InsertRaw = """
INSERT INTO csv_import_rows
SELECT @source,@version,@phase,(x->>'number')::bigint,x->>'entityId',NULLIF(x->>'companyId','')::uuid,
 NULLIF(x->>'personId','')::uuid,NULLIF(x->>'groupId','')::uuid,(x->>'ticks')::bigint,
 (x->>'raw')::jsonb,x->>'hash',CAST(@materialRef AS jsonb)
FROM jsonb_array_elements(CAST(@prepared AS jsonb)) x;
""";
    public const string InsertBanks = """
INSERT INTO company_bank_profiles(company_id,revision_id,known_ticks,content_hash,bank_data,payload)
SELECT (x->>'companyId')::uuid,x->>'revision',(x->>'ticks')::bigint,x->>'profileHash',
 (x->>'bank')::jsonb,(x->>'profile')::jsonb FROM jsonb_array_elements(CAST(@prepared AS jsonb)) x;
""";
    public const string BankConflict = """
SELECT EXISTS(SELECT 1 FROM company_bank_profiles b JOIN jsonb_array_elements(CAST(@prepared AS jsonb)) x
 ON b.company_id=(x->>'companyId')::uuid);
""";
    public const string EntityConflict = """
SELECT EXISTS(SELECT 1 FROM csv_import_rows r JOIN jsonb_array_elements(CAST(@prepared AS jsonb)) x
 ON r.entity_id=x->>'entityId' WHERE r.source_id=@source AND r.version_id=@version AND r.phase=@phase);
""";
    public const string LinkForeignKeys = """
SELECT NOT EXISTS(SELECT 1 FROM jsonb_array_elements(CAST(@prepared AS jsonb)) x
 WHERE NOT EXISTS(SELECT 1 FROM csv_import_rows r WHERE r.source_id=@source AND r.version_id=@version
 AND r.phase='companies' AND r.entity_id=x->>'companyId')
 OR NOT EXISTS(SELECT 1 FROM csv_import_rows r WHERE r.source_id=@source AND r.version_id=@version
 AND r.phase='persons' AND r.entity_id=x->>'personId'));
""";
    public const string VersionPaymentHash = "SELECT COALESCE((SELECT source_content_hash FROM company_payment_source_versions WHERE source_id=@source AND version_id=@version),'')";
    public const string PaymentConflict = """
SELECT EXISTS(SELECT 1 FROM company_payment_rows p JOIN jsonb_array_elements(CAST(@prepared AS jsonb)) x
 ON p.source_row_id=x->>'rowId' WHERE p.source_id=@source AND p.source_version_id=@version);
""";
    public const string PaymentForeignKeys = """
SELECT NOT EXISTS(SELECT 1 FROM jsonb_array_elements(CAST(@prepared AS jsonb)) x WHERE
 NOT EXISTS(SELECT 1 FROM csv_import_rows r WHERE r.source_id=@source AND r.version_id=@version AND r.phase='companies' AND r.entity_id=x->>'payer')
 OR NOT EXISTS(SELECT 1 FROM csv_import_rows r WHERE r.source_id=@source AND r.version_id=@version AND r.phase='companies' AND r.entity_id=x->>'beneficiary'));
""";
    public const string InsertPayments = """
INSERT INTO company_payment_rows(source_id,source_version_id,source_row_id,import_id,payer_company_id,beneficiary_company_id,pay_amt,pay_cnt,known_ticks,row_hash,payload)
SELECT @source,@version,x->>'rowId',@manifestHash,(x->>'payer')::uuid,(x->>'beneficiary')::uuid,
 x->>'amount',x->>'count',(x->>'ticks')::bigint,x->>'hash',(x->>'payload')::jsonb
FROM jsonb_array_elements(CAST(@prepared AS jsonb)) x;
""";
    public const string VerifyRaw = """
SELECT jsonb_build_object('rows',COUNT(*),'groups',COUNT(DISTINCT group_id),
 'incomplete',COUNT(*) FILTER(WHERE EXISTS(SELECT 1 FROM jsonb_each_text(raw_json) v WHERE v.value='')),
 'skipped',0,'conflicts',0)::text FROM csv_import_rows WHERE source_id=@source AND version_id=@version AND phase=@phase;
""";
    public const string VerifyPayments = """
WITH pairs AS (SELECT COUNT(*) n FROM company_payment_rows WHERE source_id=@source AND source_version_id=@version GROUP BY payer_company_id,beneficiary_company_id)
SELECT jsonb_build_object('rows',COUNT(*),'amount',COALESCE(SUM(pay_amt::numeric),0)::text,
 'count',COALESCE(SUM(pay_cnt::numeric),0)::text,
 'selfRows',COUNT(*) FILTER(WHERE payer_company_id=beneficiary_company_id),
 'duplicatePairs',(SELECT COUNT(*) FROM pairs WHERE n>1),
 'extraPairRows',(SELECT COALESCE(SUM(n-1),0) FROM pairs),'skipped',0,'conflicts',0)::text
FROM company_payment_rows WHERE source_id=@source AND source_version_id=@version;
""";
    public const string Verified = "SELECT COALESCE((SELECT receipt_json FROM csv_import_verified WHERE source_id=@source AND version_id=@version AND phase=@phase AND manifest_hash=@manifestHash),'')";
    public const string MarkVerified = "INSERT INTO csv_import_verified VALUES(@source,@version,@phase,@manifestHash,@receipt) ON CONFLICT DO NOTHING";
}
