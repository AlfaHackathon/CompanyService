using System.Net;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using Api.Common;
using Api.Contracts.Companies;
using Api.Contracts.Knowledge;
using Api.Contracts.Payments;
using Api.Repositories;
using Api.Resources;
using Api.Services;
using Api.Services.PublicKnowledge;
using Microsoft.AspNetCore.Mvc;

namespace Api.CsvImport;

[ApiController, InternalSecret, Route("internal/csv-import")]
public sealed class CsvController(IDapperContext db, IKnowledgeOperationStore operations,
    ICompanyMaterialsClient materials, IConfiguration config) : ControllerBase
{
    private sealed record Frozen(CompanyMaterialWrite Material);
    private static readonly string[] Phases = ["groups", "companies", "persons", "links", "payments"];
    private static object Parameters(CsvBatch b, string hash, string receipt = "", string prepared = "[]", string materialRef = "{}") => new
    {
        source = b.SourceId, version = b.VersionId, phase = b.Phase, manifestHash = b.ManifestHash,
        manifestJson = b.ManifestJson, start = b.StartRow, end = b.StartRow + b.Rows.Length - 1,
        requestHash = hash, receipt, prepared, materialRef
    };
    private bool Enabled => config.GetValue<bool>("CsvImport:Enabled");
    private IActionResult Failure(string code, int status = 409) => StatusCode(status,
        new { status = "blocked", code, skipped = 0, conflicts = status == 409 ? 1 : 0, complete = false });

    [HttpGet("readiness")]
    public IActionResult Ready() => Enabled ? Ok(new { contract = 1, owner = "company", enabled = true }) : Failure("import_disabled", 503);

    [HttpGet("verified")]
    public async Task<IActionResult> Verified([FromQuery] string sourceId, [FromQuery] string versionId,
        [FromQuery] string manifestHash, [FromQuery] string phase)
    {
        var found = await db.FirstOrDefault<string>(new QueryObject(CsvSql.Verified,
            new { source = sourceId, version = versionId, manifestHash, phase }));
        return string.IsNullOrEmpty(found) ? Failure("phase_not_verified", 412) : Content(found, "application/json");
    }

    [HttpPost("batches"), RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Batch([FromBody] CsvBatch b, CancellationToken ct)
    {
        if (!Enabled) return Failure("import_disabled", 503);
        try
        {
            using var manifest = CsvProtocol.Validate(b, Phases);
            var hash = CsvProtocol.Hash(CsvProtocol.Serialize(b));
            var p = Parameters(b, hash);
            var replay = await db.FirstOrDefault<string>(new QueryObject(CsvSql.Replay, p));
            if (!string.IsNullOrEmpty(replay)) return Replay(replay, hash);
            if (!await Prerequisites(b, ct)) return Failure("prerequisite_not_verified", 412);
            var root = manifest.RootElement;
            var receipt = CsvProtocol.Serialize(CsvProtocol.Receipt(b, hash));
            var materialRef = "{}";
            var ticks = DateTimeOffset.UtcNow.UtcTicks;
            if (b.Phase != "payments")
            {
                // Freeze the actual admission time before Context publication. An uncertain retry
                // reuses this exact material/body, never a new client clock or invented reference.
                var key = CsvProtocol.Hash(b.SourceId + "\n" + b.VersionId + "\n" + b.Phase + "\n" + b.StartRow);
                var op = await operations.Reserve(Guid.Empty, Guid.Empty, "csv-import", key, hash, time =>
                {
                    var companies = b.Phase is "companies" or "links"
                        ? b.Rows.Select(row => CsvProtocol.Uuid(CsvProtocol.Cell(root, b, CsvProtocol.Fields(root, b, row), "company_id"))).Distinct().Order().ToArray()
                        : Array.Empty<Guid>();
                    var text = CsvProtocol.Serialize(new { declaredSource = true, b.SourceId, b.VersionId, b.ManifestHash,
                        phase = b.Phase, headers = root.GetProperty("files").GetProperty(b.Phase).GetProperty("headers"), rows = b.Rows });
                    if (text.Length > 200000) throw new InvalidDataException("batch_text_limit_reduce_batch_size");
                    return CsvProtocol.Serialize(new Frozen(new("csv-" + key, "v1", "company-bank-profile", "declared_source",
                        "Declared CSV source batch; no employment or access assertion", text, time, null, null,
                        companies, [], new("event_payload", null, time, "full"))));
                });
                var frozen = JsonSerializer.Deserialize<Frozen>(op.Payload, CsvProtocol.Json) ?? throw new InvalidDataException("frozen_batch");
                var actual = await materials.Save(frozen.Material, ct);
                var normalized = new CompanyMaterialWrite(actual.MaterialId, actual.VersionId, actual.SourceId, actual.Kind,
                    actual.Title, actual.Text, actual.KnownAt, actual.PublishedAt, actual.CorrectsVersionId,
                    actual.CompanyIds, actual.BankProfileIds, actual.Provenance);
                if (CsvProtocol.Serialize(normalized) != CsvProtocol.Serialize(frozen.Material) ||
                    actual.ContentHash != CsvProtocol.Hash(CsvProtocol.Serialize(frozen.Material)))
                    return Failure("context_material_not_verified", 502);
                ticks = op.KnownAt.UtcTicks;
                materialRef = CsvProtocol.Serialize(new SourceReference { MaterialId = actual.MaterialId,
                    VersionId = actual.VersionId, ContentHash = actual.ContentHash });
            }

            using var tx = db.BeginTransaction();
            await tx.Command(new QueryObject(KnowledgeSql.WriteLock));
            replay = await tx.CommandWithResponse<string>(new QueryObject(CsvSql.Replay, p));
            if (replay.Length > 0) { tx.Commit(); return Replay(replay, hash); }
            await tx.Command(new QueryObject(CsvSql.Bind, p));
            if (!await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.Bound, p))) return Failure("source_version_hash_conflict");
            if (await tx.CommandWithResponse<long>(new QueryObject(CsvSql.Next, p)) != b.StartRow) return Failure("checkpoint_gap_or_overlap");

            if (b.Phase == "payments")
            {
                ticks = DateTimeOffset.UtcNow.UtcTicks;
                var previous = await tx.CommandWithResponse<long>(new QueryObject(PaymentRowSql.LastKnownTicks));
                if (previous > ticks) return Failure("payment_clock_backwards", 503);
                ticks = Math.Max(ticks, checked(previous + 1));
            }
            var prepared = Prepare(root, b, ticks, materialRef);
            p = Parameters(b, hash, receipt, prepared, materialRef);
            if (b.Phase == "payments")
            {
                if (!await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.PaymentForeignKeys, p))) return Failure("payment_foreign_key");
                if (await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.PaymentConflict, p))) return Failure("source_row_conflict");
                var versionHash = await tx.CommandWithResponse<string>(new QueryObject(CsvSql.VersionPaymentHash, p));
                if (versionHash.Length > 0 && versionHash != b.ManifestHash) return Failure("payment_version_hash_conflict");
                if (versionHash.Length == 0) await tx.Command(new QueryObject(PaymentRowSql.InsertVersion,
                    new { source = b.SourceId, version = b.VersionId, sourceHash = b.ManifestHash, ticks }));
                await tx.Command(new QueryObject(CsvSql.InsertPayments, p));
            }
            else
            {
                if (b.Phase != "links" && await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.EntityConflict, p))) return Failure("source_entity_conflict");
                if (b.Phase == "links" && !await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.LinkForeignKeys, p))) return Failure("link_foreign_key");
                if (b.Phase == "companies")
                {
                    if (await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.BankConflict, p))) return Failure("existing_bank_profile_initial_import_only");
                    var groupOk = await tx.CommandWithResponse<bool>(new QueryObject("""
SELECT NOT EXISTS(SELECT 1 FROM jsonb_array_elements(CAST(@prepared AS jsonb)) x WHERE x->>'groupId' IS NOT NULL
 AND NOT EXISTS(SELECT 1 FROM csv_import_rows r WHERE r.source_id=@source AND r.version_id=@version AND r.phase='groups' AND r.entity_id=x->>'groupId'))
""", p));
                    if (!groupOk) return Failure("group_foreign_key");
                    await tx.Command(new QueryObject(CsvSql.InsertBanks, p));
                }
                await tx.Command(new QueryObject(CsvSql.InsertRaw, p));
            }
            // Checkpoint is in the exact owner transaction as all batch records.
            await tx.Command(new QueryObject(CsvSql.Checkpoint, p));
            tx.Commit();
            return Content(receipt, "application/json");
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException or FormatException or OverflowException)
        { return Failure("invalid_source_batch", 400); }
        catch (PublicKnowledgeError e) { return Failure("context_or_idempotency_conflict", (int)e.Status); }
        catch (Exception) { return Failure("owner_outcome_uncertain_retry_identical_batch", 503); }
    }

    private IActionResult Replay(string raw, string hash)
    {
        var receipt = JsonSerializer.Deserialize<CsvReceipt>(raw, CsvProtocol.Json);
        return receipt?.RequestHash == hash ? Content(raw, "application/json") : Failure("batch_content_conflict");
    }

    [HttpGet("checkpoint")]
    public async Task<IActionResult> Checkpoint([FromQuery]string sourceId,[FromQuery]string versionId,[FromQuery]string manifestHash,[FromQuery]string phase)
    {
        if(!Enabled || !Phases.Contains(phase)) return Failure("import_not_ready",503);
        var p=new {source=sourceId,version=versionId,manifestHash,phase};
        var bound=await db.FirstOrDefault<string>(new QueryObject("SELECT COALESCE((SELECT manifest_hash FROM csv_import_versions WHERE source_id=@source AND version_id=@version),'')",p));
        if(!string.IsNullOrEmpty(bound) && bound!=manifestHash) return Failure("source_version_hash_conflict");
        var next=await db.FirstOrDefault<long>(new QueryObject(CsvSql.Next,p));
        return Ok(new {nextRow=next,manifestHash,phase});
    }

    private static string Prepare(JsonElement manifest, CsvBatch b, long ticks, string materialRef)
    {
        var list = new List<object>();
        var entities = new HashSet<string>();
        foreach (var row in b.Rows)
        {
            var fields = CsvProtocol.Fields(manifest, b, row);
            string Cell(string key) => CsvProtocol.Cell(manifest, b, fields, key);
            Guid? company = b.Phase is "companies" or "links" ? CsvProtocol.Uuid(Cell("company_id")) : null;
            Guid? person = b.Phase is "persons" or "links" ? CsvProtocol.Uuid(Cell("person_id")) : null;
            Guid? group = b.Phase == "groups" ? CsvProtocol.Uuid(Cell("group_id")) :
                b.Phase == "companies" && Cell("group_id") != "" ? CsvProtocol.Uuid(Cell("group_id")) : null;
            var entity = b.Phase switch { "groups" => group!.Value.ToString("D"), "companies" => company!.Value.ToString("D"),
                "persons" => person!.Value.ToString("D"), "links" => person + "/" + company, _ => row.Number.ToString() };
            if (b.Phase != "links" && !entities.Add(entity)) throw new InvalidDataException("duplicate_entity");
            var raw = CsvProtocol.Serialize(fields);
            if (b.Phase == "payments")
            {
                var input = new PaymentRowInput(b.ManifestHash, b.SourceId, b.VersionId, b.ManifestHash,
                    "payments:" + row.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    CsvProtocol.Uuid(Cell("beneficiary")), CsvProtocol.Uuid(Cell("payer")), Cell("amount"), Cell("count"));
                if (!PaymentRowValidation.Valid(input)) throw new InvalidDataException("invalid_payment");
                var payload = CsvProtocol.Serialize(input);
                list.Add(new { rowId = input.SourceRowId, payer = input.PayerCompanyId, beneficiary = input.BeneficiaryCompanyId,
                    amount = input.PayAmt, count = input.PayCnt, ticks = checked(ticks + list.Count), hash = CsvProtocol.Hash(payload), payload });
                continue;
            }
            string? bank = null, profile = null, profileHash = null, revision = null;
            if (b.Phase == "companies")
            {
                string V(string key) => fields[key];
                var data = new CompanyBankData {
                    OrgType=V("ORG_TYPE"), OkfsName=V("OKFS_NAME"), CompanyAgeYearsGradation=V("COMPANY_AGE_YEARS_GRADATION"),
                    EcoMarkLabel=V("ECO_MARK_LABEL"), MainOkvedCode=V("MAINOKVED_CODE"), MainOkvedName=V("MAINOKVED_NAME"),
                    NegativeMark=V("NEGATIVEMARK"), AmountWorkersFnsGradation=V("AMOUNT_WORKERS_FNS_GRADATION"),
                    AmountWorkersSparkGradation=V("AMOUNT_WORKERS_SPARK_GRADATION"), ClType=V("CL_TYPE"),
                    ClProductRkoMark=V("CL_PRODUCT_RKO_MARK"), ClProductGarMark=V("CL_PRODUCT_GAR_MARK"), ClProductLoaMark=V("CL_PRODUCT_LOA_MARK"),
                    ClProductLeaMark=V("CL_PRODUCT_LEA_MARK"), ClProductDepMark=V("CL_PRODUCT_DEP_MARK"), ClProductZppMark=V("CL_PRODUCT_ZPP_MARK"),
                    FtsFlag=V("FTS_FLAG"), FtsCountryName=V("FTS_COUNTRY_NAME"), FactoringMark=V("FACTORING_MARK"),
                    RevenueCompany=Cell("revenue"), GroupId=group, City=V("CITY"), Region=V("REGION") };
                if (!CompanyBankDataValidation.Valid(data)) throw new InvalidDataException("invalid_bank_data");
                revision = "csv-" + b.ManifestHash + "-" + row.Number;
                var time = new DateTimeOffset(ticks, TimeSpan.Zero);
                var reference = JsonSerializer.Deserialize<SourceReference>(materialRef, CsvProtocol.Json)!;
                var p = new CompanyBankProfile(company!.Value, revision, time, data, CompanyBankProfilePolicy.SourceId,
                    [reference], new("event_payload", null, time, "full"));
                if (!CompanyBankProfilePolicy.Valid(p)) throw new InvalidDataException("invalid_profile");
                bank=CsvProtocol.Serialize(data); profile=CsvProtocol.Serialize(p); profileHash=CsvProtocol.Hash(profile);
            }
            list.Add(new { number=row.Number, entityId=entity, companyId=company, personId=person, groupId=group,
                ticks, raw, hash=CsvProtocol.Hash(raw), revision, bank, profile, profileHash });
        }
        return CsvProtocol.Serialize(list);
    }

    private async Task<bool> Prerequisites(CsvBatch b, CancellationToken ct)
    {
        var context = await Remote(config["ContextService:BaseUrl"], "context-service/internal/csv-import/readiness", ct);
        if (context.GetProperty("contract").GetInt32() != 1 || context.GetProperty("workerEnabled").GetBoolean()) return false;
        var previous = b.Phase switch { "companies" => "groups", "persons" => "companies", "links" => "persons", "payments" => "links", _ => null };
        if (previous is not null)
        {
            var ready = await db.FirstOrDefault<string>(new QueryObject(CsvSql.Verified,
                new { source=b.SourceId, version=b.VersionId, manifestHash=b.ManifestHash, phase=previous }));
            if (string.IsNullOrEmpty(ready)) return false;
        }
        if (b.Phase is "links" or "payments")
        {
            var phase = b.Phase == "links" ? "accounts" : "links";
            var query = $"user-service/internal/csv-import/verified?sourceId={Uri.EscapeDataString(b.SourceId)}&versionId={Uri.EscapeDataString(b.VersionId)}&manifestHash={b.ManifestHash}&phase={phase}";
            var ready = await Remote(config["CsvImport:UserServiceBaseUrl"], query, ct);
            if (!ready.GetProperty("verified").GetBoolean()) return false;
        }
        return true;
    }
    private async Task<JsonElement> Remote(string? address, string path, CancellationToken ct)
    {
        if (!Uri.TryCreate(address?.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new InvalidDataException("prerequisite_configuration");
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri,path));
        request.Headers.Add("X-Internal-Secret", config["Internal:Secret"]);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("prerequisite_unavailable");
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length > 100000) throw new InvalidDataException("prerequisite_response");
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.Clone();
    }

    [HttpPost("verify"), RequestSizeLimit(200000)]
    public async Task<IActionResult> Verify([FromBody] CsvBatch b)
    {
        if (!Enabled || !Phases.Contains(b.Phase) || !CsvProtocol.Sha(b.ManifestHash) || CsvProtocol.Hash(b.ManifestJson) != b.ManifestHash) return Failure("invalid_verification",400);
        try
        {
            using var manifest = JsonDocument.Parse(b.ManifestJson);
            using var tx = db.BeginTransaction();
            await tx.Command(new QueryObject(KnowledgeSql.WriteLock));
            var p=Parameters(b, "");
            if (!await tx.CommandWithResponse<bool>(new QueryObject(CsvSql.Bound,p))) return Failure("source_version_hash_conflict");
            var raw = await tx.CommandWithResponse<string>(new QueryObject(b.Phase=="payments" ? CsvSql.VerifyPayments : CsvSql.VerifyRaw,p));
            using var actual = JsonDocument.Parse(raw);
            var a=actual.RootElement;
            var expected=manifest.RootElement.GetProperty("expected");
            if (a.GetProperty("rows").GetInt64()!=manifest.RootElement.GetProperty("files").GetProperty(b.Phase).GetProperty("rows").GetInt64() ||
                await tx.CommandWithResponse<long>(new QueryObject(CsvSql.Next,p)) != a.GetProperty("rows").GetInt64()+1) return Failure("row_verification_failed");
            if (b.Phase=="groups" && a.GetProperty("groups").GetInt64()!=expected.GetProperty("groups").GetInt64()) return Failure("group_verification_failed");
            if(b.Phase=="companies")
            {
                var canonical=await tx.CommandWithResponse<long>(new QueryObject("""
SELECT COUNT(*) FROM csv_import_rows r JOIN company_bank_profiles p ON p.company_id=r.company_id
 AND p.revision_id='csv-'||@manifestHash||'-'||r.row_number::text
WHERE r.source_id=@source AND r.version_id=@version AND r.phase='companies'
""",p));
                if(canonical!=a.GetProperty("rows").GetInt64()) return Failure("bank_profile_verification_failed");
            }
            if (b.Phase=="payments")
            {
                var amountOk=await tx.CommandWithResponse<bool>(new QueryObject("SELECT CAST(@a AS numeric)=CAST(@b AS numeric)",new {a=a.GetProperty("amount").GetString(),b=expected.GetProperty("payment_amount").GetString()}));
                if (!amountOk || a.GetProperty("count").GetString()!=expected.GetProperty("payment_count").GetRawText() ||
                    a.GetProperty("selfRows").GetInt64()!=expected.GetProperty("payment_self_rows").GetInt64() ||
                    a.GetProperty("duplicatePairs").GetInt64()!=expected.GetProperty("payment_duplicate_pairs").GetInt64() ||
                    a.GetProperty("extraPairRows").GetInt64()!=expected.GetProperty("payment_extra_pair_rows").GetInt64()) return Failure("payment_totals_verification_failed");
            }
            var receipt=CsvProtocol.Serialize(new { verified=true, b.SourceId,b.VersionId,b.ManifestHash,b.Phase,statistics=a.Clone() });
            await tx.Command(new QueryObject(CsvSql.MarkVerified,Parameters(b,"",receipt)));
            tx.Commit();
            return Content(receipt,"application/json");
        }
        catch(Exception) { return Failure("verification_not_confirmed",503); }
    }
}
