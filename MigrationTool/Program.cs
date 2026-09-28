// [2026-09-22][Antigravity] ACAMP V16 to V20 Migration Tool - Menu with 0 = Exit, 1..n = Subscription selection
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ClosedXML.Excel;
using TU.ACAMP.Domain.Tools;
using MigrationTool.Data;
using MigrationTool.Models;
using MigrationTool.Migration;
using MigrationTool.V20;

namespace MigrationTool;

internal class Program
{
    // [2026-09-23][Claude] Microsoft Excel cannot open files whose full path exceeds 218 characters
    private const int ExcelMaxPathLength = 218;

    static async Task<int> Main(string[] args)
    {
        #region hide [2026-09-22]
        ConfigureConsoleEncoding();
        PrintBanner();

        var config = LoadConfiguration(args);
        string? connectionString = GetV16ConnectionString(config);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            PrintError("Missing 'V16Connection' in appsettings.json or configuration.");
            return 1;
        }

        using var dbContext = CreateV16DbContext(connectionString);
        if (!await CheckDatabaseConnectionAsync(dbContext))
        {
            return 1;
        }

        // Check if subscription ID is supplied directly via CLI arguments
        string? cliSubscriptionId = ExtractSubscriptionIdFromArgs(args);
        if (!string.IsNullOrWhiteSpace(cliSubscriptionId))
        {
            var (isValid, subscriptionId, parseError) = ParseSubscriptionGuid(cliSubscriptionId);
            if (!isValid)
            {
                PrintError(parseError ?? "Invalid Subscription ID.");
                return 1;
            }

            // [2026-09-23][Claude] --step create-subscription = สร้าง Subscription ใน V20 ผ่าน Admin API (ไม่ถามยืนยัน)
            string? cliStep = ExtractArgValue(args, "--step");
            if (string.Equals(cliStep, "create-subscription", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteCreateV20SubscriptionAsync(dbContext, config, subscriptionId, interactive: false);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step migrate = ทุกขั้นตามลำดับ (ไม่ถามยืนยัน)
            if (string.Equals(cliStep, "migrate", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateToV20Async(dbContext, config, subscriptionId, interactive: false);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step topics = Migrate V16 Activity Topics -> V20 (Facility ที่ migrate แล้ว)
            if (string.Equals(cliStep, "topics", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateTopicsAsync(dbContext, config, subscriptionId);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step events = Migrate V16 Activity Events -> V20 (รวมยอดรายเดือน)
            if (string.Equals(cliStep, "events", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateEventsAsync(dbContext, config, subscriptionId);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step sources = Migrate V16 custom Emission Sources -> V20
            if (string.Equals(cliStep, "sources", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateSourcesAsync(dbContext, config, subscriptionId);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step groups = Migrate V16 custom Emission Groups -> V20
            if (string.Equals(cliStep, "groups", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateGroupsAsync(dbContext, config, subscriptionId);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step units = Migrate V16 custom Emission Units -> V20
            if (string.Equals(cliStep, "units", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateUnitsAsync(dbContext, config, subscriptionId);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step facilities = Migrate V16 Facility -> V20 Facility
            if (string.Equals(cliStep, "facilities", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateFacilitiesAsync(dbContext, config, subscriptionId, interactive: false);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step companies = Migrate V16 Subsidiary -> V20 Company
            if (string.Equals(cliStep, "companies", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateCompaniesAsync(dbContext, config, subscriptionId, interactive: false);
                return ok ? 0 : 1;
            }
            // [2026-09-23][Claude] --step corporates = Migrate V16 Organization -> V20 Corporate
            if (string.Equals(cliStep, "corporates", StringComparison.OrdinalIgnoreCase))
            {
                bool ok = await ExecuteMigrateCorporatesAsync(dbContext, config, subscriptionId, interactive: false);
                return ok ? 0 : 1;
            }

            await ExecuteMigrationPhase1Async(dbContext, config, subscriptionId);
            return 0;
        }

        // Run interactive menu: 0 = Exit, 1..n = Subscription selection
        await RunMainMenuLoopAsync(dbContext, config);
        return 0;
        #endregion
    }

    // [2026-09-22][Antigravity] Interactive menu loop
    // [2026-09-23][Claude] ลำดับใหม่: 1) เลือกงาน (Export / Migrate / Exit) -> 2) เลือก Subscription (0 = กลับ) -> 3) ทำงาน
    //     Migrate: เริ่มทันทีหลังเลือก Subscription (ไม่ถาม y/N) แล้วเลือก Organization -> Company -> Facility ทีละขั้น
    //     ทำเสร็จถามว่าจะกลับเมนูหลักหรือออก
    private static async Task RunMainMenuLoopAsync(V16SourceDbContext dbContext, IConfiguration config)
    {
        #region hide [2026-09-23]
        while (true)
        {
            string action = PromptMainAction();
            if (action == "0")
            {
                PrintExitMessage();
                return;
            }

            Guid? subscriptionId = await PromptSelectSubscriptionAsync(dbContext);
            if (subscriptionId == null)
            {
                continue; // 0 = กลับไปเลือกงาน
            }

            if (action == "1")
            {
                await ExecuteMigrationPhase1Async(dbContext, config, subscriptionId.Value);
            }
            else
            {
                await ExecuteMigrateToV20Async(dbContext, config, subscriptionId.Value, interactive: true);
            }

            if (!AskBackToMainMenu())
            {
                PrintExitMessage();
                return;
            }
            Console.WriteLine();
        }
        #endregion
    }

    // [2026-09-23][Claude] เมนูหลัก: 1 = Export Excel, 0 = Exit (เมนู 2 = Migrate to V20 ถูกซ่อน — ใช้ CLI --step แทน)
    private static string PromptMainAction()
    {
        #region hide [2026-09-23]
        while (true)
        {
            Console.WriteLine("----------------------------------------------------------------");
            Console.WriteLine(" MAIN MENU:");
            Console.WriteLine("----------------------------------------------------------------");
            Console.WriteLine("   [ 1] Export Excel");
            // [2026-09-23][Claude] ซ่อนเมนู [2] Migrate to V20 ตามสั่ง (ยังรันผ่าน CLI --step ได้)
            // Console.WriteLine("   [ 2] Migrate to V20");
            Console.WriteLine("   [ 0] Exit program");
            Console.WriteLine("----------------------------------------------------------------");
            Console.Write("Select an action: ");
            string? choice = Console.ReadLine()?.Trim();

            if (choice == "1") return choice;
            if (choice == "0" || string.Equals(choice, "exit", StringComparison.OrdinalIgnoreCase) || string.Equals(choice, "q", StringComparison.OrdinalIgnoreCase)) return "0";
            if (!string.IsNullOrEmpty(choice)) PrintError($"Invalid input: '{choice}'.");
        }
        #endregion
    }

    // [2026-09-23][Claude] เลือก Subscription ของ V16: 1..n หรือ GUID, 0 = กลับ (คืน null)
    private static async Task<Guid?> PromptSelectSubscriptionAsync(V16SourceDbContext dbContext)
    {
        #region hide [2026-09-23]
        var subscriptions = await FetchAllSubscriptionsAsync(dbContext);
        if (subscriptions.Count == 0)
        {
            PrintWarning("No subscriptions found in V16 database.");
            return null;
        }

        PrintSubscriptionMenu(subscriptions);
        while (true)
        {
            Console.Write($"Select a Subscription [1-{subscriptions.Count}] or 0 to go back: ");
            string? choice = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(choice)) continue;
            if (choice == "0") return null;

            if (int.TryParse(choice, out int index) && index >= 1 && index <= subscriptions.Count)
            {
                return subscriptions[index - 1].Id;
            }
            if (Guid.TryParse(choice, out Guid manualGuid))
            {
                return manualGuid;
            }
            PrintError($"Invalid input: '{choice}'. Please enter a number between 1 and {subscriptions.Count}, or 0 to go back.");
        }
        #endregion
    }

    // [2026-09-23][Claude] ถามหลังทำงานเสร็จ: 1/Enter = กลับเมนูหลัก, 0 = ออกจากโปรแกรม
    private static bool AskBackToMainMenu()
    {
        #region hide [2026-09-23]
        while (true)
        {
            Console.WriteLine();
            Console.Write("Back to main menu [1/Enter] or exit [0]: ");
            string? answer = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(answer) || answer == "1") return true;
            if (answer == "0" || answer.Equals("exit", StringComparison.OrdinalIgnoreCase) || answer.Equals("q", StringComparison.OrdinalIgnoreCase)) return false;
            PrintError($"Invalid input: '{answer}'. Enter 1 to go back or 0 to exit.");
        }
        #endregion
    }

    // [2026-09-23][Claude] Migrate to V20: รันทุกขั้นตามลำดับ หยุดทันทีเมื่อขั้นใดล้มเหลว — ทุกขั้นรันซ้ำได้ (ขั้นที่ทำแล้วจะข้าม/ใช้ของเดิม)
    //   กลุ่ม Subscription (บังคับ ไม่มีการเลือก): 1) Subscription (+ API Key)  2) Emission Unit  3) Emission Group  4) Emission Source
    //   กลุ่มองค์กร (เลือกได้ทีละระดับ):         5) Organization -> Corporate  6) Subsidiary -> Company  7) Facility -> Facility
    //   ข้อมูลกิจกรรม (ตาม Facility ที่เลือก):     8) Topic  9) Event (รวมยอดรายเดือน)
    // interactive = ให้เลือกรายการในกลุ่มองค์กร การเลือกถือเป็นการยืนยัน (0 = ยกเลิก); ไม่ interactive (CLI) = ใช้ชุดที่เคยเลือกใน state หรือทั้งหมด
    // ทุกขั้นตรวจผล (Verify) ผ่าน API migration-report ทันทีหลังทำขั้นนั้นเสร็จ และรายงานผล/บันทึกไฟล์แยกตามระดับ
    private static async Task<bool> ExecuteMigrateToV20Async(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId, bool interactive)
    {
        #region hide [2026-09-23]
        Console.WriteLine("\n=== Migrate to V20 — Step 1/9: Subscription ===");
        if (!await ExecuteCreateV20SubscriptionAsync(dbContext, config, subscriptionId, interactive))
        {
            PrintWarning("Migration stopped at Step 1 (Subscription).");
            return false;
        }

        // [2026-09-23][Claude] Library ของ Subscription migrate ทั้งหมดพร้อม Subscription (ไม่มีการเลือก)
        Console.WriteLine("\n=== Migrate to V20 — Step 2/9: Emission Units ===");
        if (!await ExecuteMigrateUnitsAsync(dbContext, config, subscriptionId))
        {
            PrintWarning("Migration stopped at Step 2 (Emission Units).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 3/9: Emission Groups ===");
        if (!await ExecuteMigrateGroupsAsync(dbContext, config, subscriptionId))
        {
            PrintWarning("Migration stopped at Step 3 (Emission Groups).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 4/9: Emission Sources ===");
        if (!await ExecuteMigrateSourcesAsync(dbContext, config, subscriptionId))
        {
            PrintWarning("Migration stopped at Step 4 (Emission Sources).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 5/9: Organizations -> Corporates ===");
        if (!await ExecuteMigrateCorporatesAsync(dbContext, config, subscriptionId, interactive))
        {
            PrintWarning("Migration stopped at Step 5 (Corporates).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 6/9: Subsidiaries -> Companies ===");
        if (!await ExecuteMigrateCompaniesAsync(dbContext, config, subscriptionId, interactive))
        {
            PrintWarning("Migration stopped at Step 6 (Companies).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 7/9: Facilities ===");
        if (!await ExecuteMigrateFacilitiesAsync(dbContext, config, subscriptionId, interactive))
        {
            PrintWarning("Migration stopped at Step 7 (Facilities).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 8/9: Topics ===");
        if (!await ExecuteMigrateTopicsAsync(dbContext, config, subscriptionId))
        {
            PrintWarning("Migration stopped at Step 8 (Topics).");
            return false;
        }

        Console.WriteLine("\n=== Migrate to V20 — Step 9/9: Events ===");
        if (!await ExecuteMigrateEventsAsync(dbContext, config, subscriptionId))
        {
            PrintWarning("Migration stopped at Step 9 (Events).");
            return false;
        }

        PrintSuccess("Migrate to V20 completed.");
        return true;
        #endregion
    }

    // [2026-09-23][Claude] Step: สร้าง Subscription ใหม่ใน V20 ผ่าน POST api/v2/admin/subscriptions แล้วเก็บ V20 Id + API Key ลง migration-state
    // รันซ้ำได้: ถ้า state มีครบแล้วจะไม่สร้างใหม่, ถ้า V20 มีอยู่แล้ว (409 ExternalRef ซ้ำ) จะออก API Key ใหม่ให้ Subscription เดิม
    private static async Task<bool> ExecuteCreateV20SubscriptionAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId, bool interactive)
    {
        #region hide [2026-09-23]
        // [2026-09-23][Claude] สร้าง/ตรวจ Subscription แล้ว Verify ผ่าน API migration-report ทันที
        if (!await ExecuteCreateV20SubscriptionCoreAsync(dbContext, config, subscriptionId, interactive)) return false;

        var v16Sub = await dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId);
        if (v16Sub == null) return false;
        string outputBasePath = GetOutputBasePath(config);
        var state = MigrationStateStore.Load(outputBasePath, subscriptionId)!;

        DateTime expStart = v16Sub.ActivatedDate;
        DateTime expExpiry = v16Sub.ExpireDate > expStart ? v16Sub.ExpireDate : expStart.AddYears(1);
        string expPlan = MapV20PlanText(MapV16LevelToV20Plan(v16Sub.SubscribeLevel));

        return await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "subscription", report =>
        {
            var r = new VerifyResult { Expected = 1 };
            string expTitle = (v16Sub.Title ?? string.Empty).Trim();
            if (!string.Equals(report.Title?.Trim(), expTitle, StringComparison.Ordinal)) r.Fails.Add($"Title: V16 '{expTitle}' vs V20 '{report.Title}'");
            if (!string.Equals(report.Plan, expPlan, StringComparison.OrdinalIgnoreCase)) r.Fails.Add($"Plan: expected {expPlan} (V16 {MapV16LevelText(v16Sub.SubscribeLevel)}) vs V20 {report.Plan}");
            if (report.StartDate.Date != expStart.Date) r.Fails.Add($"StartDate: V16 {expStart:yyyy-MM-dd} vs V20 {report.StartDate:yyyy-MM-dd}");
            if (report.ExpiryDate.Date != expExpiry.Date) r.Fails.Add($"ExpiryDate: expected {expExpiry:yyyy-MM-dd} vs V20 {report.ExpiryDate:yyyy-MM-dd}");
            if (report.SubscriptionId != state.V20SubscriptionId) r.Warns.Add($"SubscriptionId in state {state.V20SubscriptionId} != V20 {report.SubscriptionId}");
            if (r.Fails.Count == 0) r.Ok = 1;
            return r;
        });
        #endregion
    }

    // [2026-09-23][Claude] เนื้องานสร้าง Subscription เดิม (ไม่รวม Verify)
    private static async Task<bool> ExecuteCreateV20SubscriptionCoreAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId, bool interactive)
    {
        #region hide [2026-09-23]
        var v16Sub = await dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == subscriptionId);
        if (v16Sub == null)
        {
            PrintError($"Subscription with ID '{subscriptionId}' was not found in V16 database.");
            return false;
        }

        string outputBasePath = GetOutputBasePath(config);
        string externalRef = $"V16:{v16Sub.Id.ToString().ToLowerInvariant()}";
        var state = MigrationStateStore.Load(outputBasePath, v16Sub.Id) ?? new MigrationState
        {
            V16SubscriptionId = v16Sub.Id,
            V16Title = v16Sub.Title,
            ExternalRef = externalRef
        };

        var settings = GetV20ApiSettings(config);
        if (settings == null) return false;

        // [2026-09-23][Claude] state มี Subscription แล้ว -> ตรวจกับ V20 ก่อนว่ายังอยู่จริง
        //     ยังอยู่ = ไม่สร้างซ้ำ (1 V16 Sub = 1 V20 Sub), ถูกลบใน V20 แล้ว = ล้าง state แล้วสร้างใหม่ได้
        if (state.V20SubscriptionId.HasValue)
        {
            try
            {
                using var checkClient = new V20AdminApiClient(settings.Value.BaseUrl, settings.Value.AdminEmail, settings.Value.AdminPassword);
                var found = await checkClient.FindSubscriptionByExternalRefAsync(externalRef);
                if (found.Success && found.Data != null)
                {
                    // [2026-09-23][Claude] มีอยู่แล้ว -> ตรวจ/ต่ออายุ API Key แทนการสร้างใหม่
                    state.V20SubscriptionId = found.Data.SubscriptionId;
                    if (!await EnsureV20ApiKeyAsync(checkClient, settings.Value.BaseUrl, state, outputBasePath, settings.Value.ApiKeyName, settings.Value.ApiKeyExpireDays))
                    {
                        return false;
                    }

                    PrintSuccess($"Already created in V20: SubscriptionId = {found.Data.SubscriptionId} (API Key {state.V20ApiKeyPrefix}, expires {state.V20ApiKeyExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never"}).\n" +
                                 $"State: {MigrationStateStore.GetStatePath(outputBasePath, v16Sub.Id)}\n" +
                                 "To create again: delete this subscription in V20 first, then run this step again.");
                    return true;
                }

                if (found.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    PrintWarning($"Subscription Id {state.V20SubscriptionId} no longer exists in V20 (deleted). State will be reset and a new subscription created.");
                    state.V20SubscriptionId = null;
                    state.V20ApiKey = null;
                    state.V20ApiKeyPrefix = null;
                    state.V20ApiKeyExpiresAt = null;
                    MigrationStateStore.Save(outputBasePath, state);
                }
                else if (!found.Success)
                {
                    PrintApiError("check existing subscription", found.StatusCode, found.Error);
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                PrintError($"Cannot reach V20 API at {settings.Value.BaseUrl}: {ex.Message}");
                return false;
            }
        }

        // V16 ExpireDate ต้องมากกว่า ActivatedDate ไม่งั้น V20 ปฏิเสธ -> ใช้ +1 ปีแทน
        DateTime startDate = v16Sub.ActivatedDate;
        DateTime expiryDate = v16Sub.ExpireDate > startDate ? v16Sub.ExpireDate : startDate.AddYears(1);

        Console.WriteLine("\n[V20] Create Subscription");
        Console.WriteLine($"  API        : {settings.Value.BaseUrl} (admin: {settings.Value.AdminEmail})");
        Console.WriteLine($"  Title      : {v16Sub.Title}");
        Console.WriteLine($"  RegNo      : {v16Sub.RegistrationNo ?? "-"}");
        Console.WriteLine($"  Start/Exp. : {startDate:yyyy-MM-dd} / {expiryDate:yyyy-MM-dd}");
        int v20Plan = MapV16LevelToV20Plan(v16Sub.SubscribeLevel);
        Console.WriteLine($"  V16 Type   : {v16Sub.SubscribeType} ({MapV16SubscribeTypeText(v16Sub.SubscribeType)})");
        Console.WriteLine($"  V16 Level  : {v16Sub.SubscribeLevel} ({MapV16LevelText(v16Sub.SubscribeLevel)})");
        Console.WriteLine($"  V20 Plan   : {v20Plan} ({MapV20PlanText(v20Plan)})");
        Console.WriteLine($"  ExternalRef: {externalRef}");

        try
        {
            using var client = new V20AdminApiClient(settings.Value.BaseUrl, settings.Value.AdminEmail, settings.Value.AdminPassword);
            V20AdminApiClient.SubscriptionWithKeyDto? result;

            if (state.V20SubscriptionId.HasValue)
            {
                // มี Subscription แล้วแต่ยังไม่มี Key ใน state -> ออก Key ใหม่
                var keyRes = await client.CreateApiKeyAsync(state.V20SubscriptionId.Value, settings.Value.ApiKeyName, settings.Value.ApiKeyExpireDays);
                if (!keyRes.Success) { PrintApiError("issue API key", keyRes.StatusCode, keyRes.Error); return false; }
                result = keyRes.Data;
            }
            else
            {
                var createRes = await client.CreateSubscriptionAsync(new V20AdminApiClient.CreateSubscriptionRequest
                {
                    Title = v16Sub.Title,
                    Plan = v20Plan,
                    StartDate = startDate,
                    ExpiryDate = expiryDate,
                    TaxId = v16Sub.RegistrationNo,
                    Remarks = $"Migrated from ACAMP V16 ({v16Sub.Id}) [V16Type:{MapV16SubscribeTypeText(v16Sub.SubscribeType)}] [V16Level:{MapV16LevelText(v16Sub.SubscribeLevel)}]",
                    ExternalRef = externalRef,
                    ApiKeyName = settings.Value.ApiKeyName,
                    ApiKeyExpireDays = settings.Value.ApiKeyExpireDays
                });

                if (createRes.Success)
                {
                    result = createRes.Data;
                }
                else if (createRes.StatusCode == System.Net.HttpStatusCode.Conflict && createRes.Error?.Subscription != null)
                {
                    // เคยสร้างไว้แล้ว (state หาย) -> ใช้ Subscription เดิม + ออก Key ใหม่
                    int existingId = createRes.Error.Subscription.SubscriptionId;
                    PrintWarning($"V20 already has this subscription (Id {existingId}). Issuing a new API key...");
                    state.V20SubscriptionId = existingId;
                    MigrationStateStore.Save(outputBasePath, state);

                    var keyRes = await client.CreateApiKeyAsync(existingId, settings.Value.ApiKeyName, settings.Value.ApiKeyExpireDays);
                    if (!keyRes.Success) { PrintApiError("issue API key", keyRes.StatusCode, keyRes.Error); return false; }
                    result = keyRes.Data;
                }
                else
                {
                    PrintApiError("create subscription", createRes.StatusCode, createRes.Error);
                    return false;
                }
            }

            if (result?.Subscription == null || result.ApiKey == null || string.IsNullOrWhiteSpace(result.ApiKey.RawKey))
            {
                PrintError("V20 API returned an unexpected response (missing subscription or API key).");
                return false;
            }

            state.V20SubscriptionId = result.Subscription.SubscriptionId;
            state.V20ApiKeyPrefix = result.ApiKey.KeyPrefix;
            state.V20ApiKey = result.ApiKey.RawKey;
            state.V20ApiKeyExpiresAt = result.ApiKey.ExpiresAt;
            string statePath = MigrationStateStore.Save(outputBasePath, state);

            PrintSuccess($"[V20] Subscription ready: Id = {state.V20SubscriptionId}, Title = {result.Subscription.Title}\n" +
                         $"API Key {state.V20ApiKeyPrefix} (expires {state.V20ApiKeyExpiresAt?.ToString("yyyy-MM-dd") ?? "never"})\n" +
                         $"State saved: {statePath}  (contains API key - keep it private)");
            return true;
        }
        catch (HttpRequestException ex)
        {
            PrintError($"Cannot reach V20 API at {settings.Value.BaseUrl}: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException)
        {
            PrintError($"V20 API request timed out ({settings.Value.BaseUrl}).");
            return false;
        }
        #endregion
    }

    // [2026-09-23][Claude] เตรียม session สำหรับขั้นที่ยิง API ด้วย Key ของ Subscription:
    // โหลด state (ต้องผ่าน step Create Subscription แล้ว) + อ่าน config + ตรวจ/ต่ออายุ API Key
    private static async Task<(MigrationState State, string OutputBasePath, string BaseUrl)?> PrepareV20SessionAsync(IConfiguration config, Guid v16SubscriptionId)
    {
        #region hide [2026-09-23]
        string outputBasePath = GetOutputBasePath(config);
        var state = MigrationStateStore.Load(outputBasePath, v16SubscriptionId);
        if (state?.V20SubscriptionId == null)
        {
            PrintError("This subscription has not been created in V20 yet. Run step [2] Create Subscription in V20 first.");
            return null;
        }

        var settings = GetV20ApiSettings(config);
        if (settings == null) return null;

        try
        {
            using var adminClient = new V20AdminApiClient(settings.Value.BaseUrl, settings.Value.AdminEmail, settings.Value.AdminPassword);
            if (!await EnsureV20ApiKeyAsync(adminClient, settings.Value.BaseUrl, state, outputBasePath, settings.Value.ApiKeyName, settings.Value.ApiKeyExpireDays))
            {
                return null;
            }
        }
        catch (HttpRequestException ex)
        {
            PrintError($"Cannot reach V20 API at {settings.Value.BaseUrl}: {ex.Message}");
            return null;
        }

        return (state, outputBasePath, settings.Value.BaseUrl);
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Organization -> V20 Corporate (POST api/v2/corporates)
    // Code = V16-ORG-{V16 OrganizationId} ใช้เป็นตัวจับคู่ รันซ้ำได้: มี Code นี้ใน V20 แล้ว = ใช้ของเดิม ไม่สร้างซ้ำ
    // บันทึก V16 OrganizationId -> CorporateKey ลง state ทีละรายการ (หยุดกลางทางแล้วรันต่อได้)
    private static async Task<bool> ExecuteMigrateCorporatesAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId, bool interactive)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var organizations = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.SubscribeId == subscriptionId)
            .OrderBy(o => o.OrderId)
            .ThenBy(o => o.Id)
            .ToListAsync();

        if (organizations.Count == 0)
        {
            PrintWarning("No organizations found in V16 for this subscription.");
            return true;
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);

        var existingRes = await api.GetAllCorporatesAsync();
        if (!existingRes.Success || existingRes.Data == null)
        {
            PrintError($"V20 API failed to list corporates: HTTP {(int)existingRes.StatusCode} {existingRes.Error?.Code} - {existingRes.Error?.Message}");
            return false;
        }
        var existingByCode = existingRes.Data
            .Where(c => !string.IsNullOrWhiteSpace(c.Code))
            .GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"\n[V20] Migrate Organizations -> Corporates (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"V20 Code",-16}  {"Action",-8}  Title");
        for (int i = 0; i < organizations.Count; i++)
        {
            var org = organizations[i];
            string code = $"V16-ORG-{org.Id}";
            string action = !existingByCode.TryGetValue(code, out var ex) ? "create"
                          : SameTitle(ex.Title, TrimTitle200(org.Title, code)) ? "exists" : "update";
            string change = action == "update" ? $"  (V20: '{ex!.Title}')" : string.Empty;
            Console.WriteLine($"  {i + 1,3}  {org.Id,7}  {code,-16}  {action,-8}  {org.Title}{change}");
        }

        // [2026-09-23][Claude] เลือก Organization ที่จะ migrate (จำไว้ใน state ให้ขั้นถัดไปใช้ชุดเดียวกัน)
        List<V16Organization> selectedOrgs;
        if (interactive)
        {
            var picked = PromptSelectIndexes(organizations.Count, "Select organizations [Enter = all, e.g. 1,3 or 1-2, 0 = cancel]: ");
            if (picked == null)
            {
                PrintWarning("Cancelled.");
                return false;
            }
            selectedOrgs = picked.Select(i => organizations[i]).ToList();

            Console.WriteLine($"Selected {selectedOrgs.Count} of {organizations.Count}: {string.Join(", ", selectedOrgs.Select(o => $"[{o.Id}] {o.Title}"))}");
        }
        else
        {
            // CLI: ใช้ชุดที่เคยเลือกไว้ใน state ถ้ามี ไม่งั้นทั้งหมด
            selectedOrgs = state.SelectedOrganizationIds == null
                ? organizations
                : organizations.Where(o => state.SelectedOrganizationIds.Contains(o.Id)).ToList();
        }

        state.SelectedOrganizationIds = selectedOrgs.Count == organizations.Count ? null : selectedOrgs.Select(o => o.Id).ToList();
        MigrationStateStore.Save(outputBasePath, state);

        int created = 0, updated = 0, existed = 0, failed = 0;
        foreach (var org in selectedOrgs)
        {
            string code = $"V16-ORG-{org.Id}";
            string title = (org.Title ?? string.Empty).Trim();
            if (title.Length == 0) title = code;
            if (title.Length > 200) title = title[..200];

            if (existingByCode.TryGetValue(code, out var existing))
            {
                state.CorporateKeys[org.Id] = existing.CorporateKey;
                MigrationStateStore.Save(outputBasePath, state);

                // [2026-09-23][Claude] ชื่อใน V16 เปลี่ยน -> PATCH ให้ V20 ตรงกับต้นทาง
                if (!SameTitle(existing.Title, title))
                {
                    var upd = await api.UpdateCorporateAsync(existing.CorporateKey, new V20ApiClient.UpdateCorporateRequest { Title = title });
                    if (upd.Success && upd.Data != null)
                    {
                        existingByCode[code] = upd.Data;
                        updated++;
                        Console.WriteLine($"  ~ [{org.Id}] '{existing.Title}' -> '{title}' ({existing.CorporateKey})");
                    }
                    else
                    {
                        failed++;
                        PrintError($"  ! [{org.Id}] update title failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message}");
                    }
                    continue;
                }

                existed++;
                Console.WriteLine($"  = [{org.Id}] {title} -> {existing.CorporateKey} (exists)");
                continue;
            }

            var res = await api.CreateCorporateAsync(new V20ApiClient.CreateCorporateRequest
            {
                Code = code,
                Title = title,
                Remark = $"Migrated from ACAMP V16 Organization {org.Id}",
                IsActive = true
            });

            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.CorporateKey))
            {
                state.CorporateKeys[org.Id] = res.Data.CorporateKey;
                MigrationStateStore.Save(outputBasePath, state);
                existingByCode[code] = res.Data;
                created++;
                Console.WriteLine($"  + [{org.Id}] {title} -> {res.Data.CorporateKey}");
            }
            else
            {
                failed++;
                PrintError($"  ! [{org.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
            }
        }

        string summary = $"[V20] Corporates: created {created}, updated {updated}, existing {existed}, failed {failed} (selected {selectedOrgs.Count} of {organizations.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (failed == 0) PrintSuccess(summary); else PrintWarning(summary);

        // [2026-09-23][Claude] Verify ระดับ Corporate ผ่าน API migration-report
        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "corporates", report =>
        {
            var r = new VerifyResult { Expected = selectedOrgs.Count };
            var byCode = report.Corporates.GroupBy(c => c.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var o in selectedOrgs)
            {
                string code = $"V16-ORG-{o.Id}";
                if (!byCode.TryGetValue(code, out var c)) { r.Fails.Add($"{code} missing (V16 [{o.Id}] {o.Title})"); continue; }
                string expected = TrimTitle200(o.Title, code);
                bool ok = true;
                if (!string.Equals(c.Title?.Trim(), expected, StringComparison.Ordinal)) { r.Fails.Add($"{code} title: V16 '{expected}' vs V20 '{c.Title}'"); ok = false; }
                if (state.CorporateKeys.TryGetValue(o.Id, out var k) && k != c.CorporateKey) r.Warns.Add($"{code} key in state {k} != V20 {c.CorporateKey}");
                if (ok) r.Ok++;
            }
            var expectedCodes = selectedOrgs.Select(o => $"V16-ORG-{o.Id}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var c in report.Corporates.Where(c => !expectedCodes.Contains(c.Code ?? string.Empty)))
                r.Infos.Add($"'{(string.IsNullOrEmpty(c.Code) ? "(no code)" : c.Code)}' {c.Title} exists in V20 outside this run's scope");
            return r;
        });
        return failed == 0 && verified;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Subsidiary -> V20 Company (POST api/v2/companies)
    // ทำเฉพาะ Organization ที่เลือกไว้ (state.SelectedOrganizationIds) และ migrate เป็น Corporate แล้ว (state.CorporateKeys)
    // Code = V16-SUB-{V16 SubsidiaryId} (Code ไม่ซ้ำภายใน Corporate) ใช้จับคู่ รันซ้ำได้; EquityShare = V16 EquityShareApproach (%)
    private static async Task<bool> ExecuteMigrateCompaniesAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId, bool interactive)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var organizations = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.SubscribeId == subscriptionId)
            .OrderBy(o => o.OrderId)
            .ThenBy(o => o.Id)
            .ToListAsync();
        if (state.SelectedOrganizationIds != null)
        {
            organizations = organizations.Where(o => state.SelectedOrganizationIds.Contains(o.Id)).ToList();
        }

        var missingCorporate = organizations.Where(o => !state.CorporateKeys.ContainsKey(o.Id)).ToList();
        if (missingCorporate.Count > 0)
        {
            PrintError($"Organization(s) not migrated to V20 corporates yet: {string.Join(", ", missingCorporate.Select(o => $"[{o.Id}] {o.Title}"))}. Run the Corporates step first.");
            return false;
        }

        var orgIds = organizations.Select(o => o.Id).ToList();
        var subsidiaries = await dbContext.Subsidiaries
            .AsNoTracking()
            .Where(s => orgIds.Contains(s.OrganizationId))
            .OrderBy(s => s.OrganizationId)
            .ThenBy(s => s.OrderId)
            .ThenBy(s => s.Id)
            .ToListAsync();

        if (subsidiaries.Count == 0)
        {
            PrintWarning("No subsidiaries found in V16 under the selected organizations.");
            return true;
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);

        // Company ที่มีอยู่ใน V20 แยกตาม Corporate -> Code
        var existingByCorporate = new Dictionary<string, Dictionary<string, V20ApiClient.CompanyDto>>();
        foreach (var corporateKey in orgIds.Select(id => state.CorporateKeys[id]).Distinct())
        {
            var res = await api.GetAllCompaniesAsync(corporateKey);
            if (!res.Success || res.Data == null)
            {
                PrintError($"V20 API failed to list companies of {corporateKey}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
                return false;
            }
            existingByCorporate[corporateKey] = res.Data
                .Where(c => !string.IsNullOrWhiteSpace(c.Code))
                .GroupBy(c => c.Code.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        Console.WriteLine($"\n[V20] Migrate Subsidiaries -> Companies (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"Org",5}  {"V20 Code",-16}  {"Action",-8}  {"Equity%",7}  Title");
        for (int i = 0; i < subsidiaries.Count; i++)
        {
            var sub = subsidiaries[i];
            string code = $"V16-SUB-{sub.Id}";
            float eq = Math.Clamp(sub.EquityShareApproach, 0f, 100f);
            string action = !existingByCorporate[state.CorporateKeys[sub.OrganizationId]].TryGetValue(code, out var ex) ? "create"
                          : SameTitle(ex.Title, TrimTitle200(sub.Title, code)) && Math.Abs(ex.EquityShare - eq) <= 0.01f ? "exists" : "update";
            string change = action == "update" ? $"  (V20: '{ex!.Title}', {ex.EquityShare:0.##}%)" : string.Empty;
            Console.WriteLine($"  {i + 1,3}  {sub.Id,7}  {sub.OrganizationId,5}  {code,-16}  {action,-8}  {sub.EquityShareApproach,7:0.##}  {sub.Title}{change}");
        }

        var outOfRange = subsidiaries.Where(s => s.EquityShareApproach < 0f || s.EquityShareApproach > 100f).ToList();
        if (outOfRange.Count > 0)
        {
            PrintWarning($"EquityShare outside 0-100 will be clamped: {string.Join(", ", outOfRange.Select(s => $"[{s.Id}] {s.EquityShareApproach}"))}");
        }

        // [2026-09-23][Claude] เลือก Subsidiary ที่จะ migrate (จำไว้ใน state ให้ขั้น Facility ใช้ชุดเดียวกัน)
        List<V16Subsidiary> selectedSubs;
        if (interactive)
        {
            var picked = PromptSelectIndexes(subsidiaries.Count, "Select subsidiaries [Enter = all, e.g. 1,3 or 1-2, 0 = cancel]: ");
            if (picked == null)
            {
                PrintWarning("Cancelled.");
                return false;
            }
            selectedSubs = picked.Select(i => subsidiaries[i]).ToList();

            Console.WriteLine($"Selected {selectedSubs.Count} of {subsidiaries.Count}: {string.Join(", ", selectedSubs.Select(x => $"[{x.Id}] {x.Title}"))}");
        }
        else
        {
            selectedSubs = state.SelectedSubsidiaryIds == null
                ? subsidiaries
                : subsidiaries.Where(x => state.SelectedSubsidiaryIds.Contains(x.Id)).ToList();
        }

        state.SelectedSubsidiaryIds = selectedSubs.Count == subsidiaries.Count ? null : selectedSubs.Select(x => x.Id).ToList();
        MigrationStateStore.Save(outputBasePath, state);

        int created = 0, updated = 0, existed = 0, failed = 0;
        foreach (var sub in selectedSubs)
        {
            string corporateKey = state.CorporateKeys[sub.OrganizationId];
            string code = $"V16-SUB-{sub.Id}";
            string title = (sub.Title ?? string.Empty).Trim();
            if (title.Length == 0) title = code;
            if (title.Length > 200) title = title[..200];
            float equity = Math.Clamp(sub.EquityShareApproach, 0f, 100f);

            var existingCodes = existingByCorporate[corporateKey];
            if (existingCodes.TryGetValue(code, out var existing))
            {
                state.CompanyKeys[sub.Id] = existing.CompanyKey;
                MigrationStateStore.Save(outputBasePath, state);

                // [2026-09-23][Claude] ชื่อ / EquityShare ใน V16 เปลี่ยน -> PATCH เฉพาะฟิลด์ที่ต่าง
                bool titleChanged = !SameTitle(existing.Title, title);
                bool equityChanged = Math.Abs(existing.EquityShare - equity) > 0.01f;
                if (titleChanged || equityChanged)
                {
                    var upd = await api.UpdateCompanyAsync(existing.CompanyKey, new V20ApiClient.UpdateCompanyRequest
                    {
                        Title = titleChanged ? title : null,
                        EquityShare = equityChanged ? equity : null
                    });
                    if (upd.Success && upd.Data != null)
                    {
                        existingCodes[code] = upd.Data;
                        updated++;
                        Console.WriteLine($"  ~ [{sub.Id}] {(titleChanged ? $"'{existing.Title}' -> '{title}' " : string.Empty)}{(equityChanged ? $"equity {existing.EquityShare:0.##} -> {equity:0.##} " : string.Empty)}({existing.CompanyKey})");
                    }
                    else
                    {
                        failed++;
                        PrintError($"  ! [{sub.Id}] update failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message}");
                    }
                    continue;
                }

                existed++;
                Console.WriteLine($"  = [{sub.Id}] {title} -> {existing.CompanyKey} (exists)");
                continue;
            }

            var res = await api.CreateCompanyAsync(new V20ApiClient.CreateCompanyRequest
            {
                CorporateKey = corporateKey,
                Code = code,
                Title = title,
                Remark = $"Migrated from ACAMP V16 Subsidiary {sub.Id}",
                EquityShare = equity,
                IsActive = true
            });

            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.CompanyKey))
            {
                state.CompanyKeys[sub.Id] = res.Data.CompanyKey;
                MigrationStateStore.Save(outputBasePath, state);
                existingCodes[code] = res.Data;
                created++;
                Console.WriteLine($"  + [{sub.Id}] {title} -> {res.Data.CompanyKey}");
            }
            else
            {
                failed++;
                PrintError($"  ! [{sub.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
            }
        }

        string summary = $"[V20] Companies: created {created}, updated {updated}, existing {existed}, failed {failed} (selected {selectedSubs.Count} of {subsidiaries.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (failed == 0) PrintSuccess(summary); else PrintWarning(summary);

        // [2026-09-23][Claude] Verify ระดับ Company ผ่าน API migration-report (ชื่อ, Corporate แม่, EquityShare)
        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "companies", report =>
        {
            var r = new VerifyResult { Expected = selectedSubs.Count };
            var all = report.Corporates.SelectMany(c => c.Companies.Select(x => (Corp: c, Comp: x))).ToList();
            var byCode = all.GroupBy(x => x.Comp.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var x in selectedSubs)
            {
                string code = $"V16-SUB-{x.Id}";
                if (!byCode.TryGetValue(code, out var cc)) { r.Fails.Add($"{code} missing (V16 [{x.Id}] {x.Title})"); continue; }
                bool ok = true;
                string expected = TrimTitle200(x.Title, code);
                if (!string.Equals(cc.Comp.Title?.Trim(), expected, StringComparison.Ordinal)) { r.Fails.Add($"{code} title: V16 '{expected}' vs V20 '{cc.Comp.Title}'"); ok = false; }
                string parent = $"V16-ORG-{x.OrganizationId}";
                if (!string.Equals(cc.Corp.Code, parent, StringComparison.OrdinalIgnoreCase)) { r.Fails.Add($"{code} under corporate '{cc.Corp.Code}', expected '{parent}'"); ok = false; }
                float equity = Math.Clamp(x.EquityShareApproach, 0f, 100f);
                if (Math.Abs(cc.Comp.EquityShare - equity) > 0.01f) { r.Fails.Add($"{code} EquityShare: V16 {equity} vs V20 {cc.Comp.EquityShare}"); ok = false; }
                if (state.CompanyKeys.TryGetValue(x.Id, out var k) && k != cc.Comp.CompanyKey) r.Warns.Add($"{code} key in state {k} != V20 {cc.Comp.CompanyKey}");
                if (ok) r.Ok++;
            }
            var expectedCodes = selectedSubs.Select(x => $"V16-SUB-{x.Id}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var x in all.Where(x => !expectedCodes.Contains(x.Comp.Code ?? string.Empty)))
                r.Infos.Add($"'{(string.IsNullOrEmpty(x.Comp.Code) ? "(no code)" : x.Comp.Code)}' {x.Comp.Title} exists in V20 outside this run's scope");
            return r;
        });
        return failed == 0 && verified;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Facility -> V20 Facility (POST api/v2/facilities)
    // ทำเฉพาะ Subsidiary ที่เลือกไว้ (state.SelectedSubsidiaryIds ใต้ SelectedOrganizationIds) และ migrate เป็น Company แล้ว (state.CompanyKeys)
    // Code = V16-FAC-{V16 FacilityId} ใช้จับคู่ รันซ้ำได้
    // V20 บังคับ Title ไม่ซ้ำภายใน Company: ชื่อซ้ำกันเองใน V16 (Subsidiary เดียวกัน) จะเติม " (V16-FAC-{id})" ต่อท้ายรายการที่ 2 เป็นต้นไป
    // ถ้า V20 มี Facility ชื่อเดียวกันแต่ Code อื่นอยู่แล้ว = ล้มเหลวรายการนั้น (ไม่รวมข้อมูลเข้าด้วยกันเอง)
    private static async Task<bool> ExecuteMigrateFacilitiesAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId, bool interactive)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        // Subsidiary ที่อยู่ในขอบเขต = ใต้ Organization ที่เลือก และตรงกับ Subsidiary ที่เลือก
        var orgIds = await dbContext.Organizations
            .AsNoTracking()
            .Where(o => o.SubscribeId == subscriptionId)
            .Select(o => o.Id)
            .ToListAsync();
        if (state.SelectedOrganizationIds != null)
        {
            orgIds = orgIds.Where(id => state.SelectedOrganizationIds.Contains(id)).ToList();
        }

        var subsidiaries = await dbContext.Subsidiaries
            .AsNoTracking()
            .Where(x => orgIds.Contains(x.OrganizationId))
            .ToListAsync();
        if (state.SelectedSubsidiaryIds != null)
        {
            subsidiaries = subsidiaries.Where(x => state.SelectedSubsidiaryIds.Contains(x.Id)).ToList();
        }

        var missingCompany = subsidiaries.Where(x => !state.CompanyKeys.ContainsKey(x.Id)).ToList();
        if (missingCompany.Count > 0)
        {
            PrintError($"Subsidiary(ies) not migrated to V20 companies yet: {string.Join(", ", missingCompany.Select(x => $"[{x.Id}] {x.Title}"))}. Run the Companies step first.");
            return false;
        }

        var subIds = subsidiaries.Select(x => x.Id).ToList();
        var facilities = await dbContext.Facilities
            .AsNoTracking()
            .Where(f => subIds.Contains(f.SubsidiaryId))
            .OrderBy(f => f.SubsidiaryId)
            .ThenBy(f => f.OrderId)
            .ThenBy(f => f.Id)
            .ToListAsync();

        if (facilities.Count == 0)
        {
            PrintWarning("No facilities found in V16 under the selected subsidiaries.");
            return true;
        }

        // ชื่อที่จะใช้ใน V20: ชื่อซ้ำภายใน Subsidiary เดียวกันเติม Code ต่อท้าย
        var targetTitles = BuildFacilityTargetTitles(facilities);

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);

        var existingByCompany = new Dictionary<string, List<V20ApiClient.FacilityDto>>();
        foreach (var companyKey in subIds.Select(id => state.CompanyKeys[id]).Distinct())
        {
            var res = await api.GetAllFacilitiesAsync(companyKey);
            if (!res.Success || res.Data == null)
            {
                PrintError($"V20 API failed to list facilities of {companyKey}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
                return false;
            }
            existingByCompany[companyKey] = res.Data;
        }

        V20ApiClient.FacilityDto? FindByCode(string companyKey, string code) =>
            existingByCompany[companyKey].FirstOrDefault(x => string.Equals(x.Code?.Trim(), code, StringComparison.OrdinalIgnoreCase));
        V20ApiClient.FacilityDto? FindByTitle(string companyKey, string title) =>
            existingByCompany[companyKey].FirstOrDefault(x => string.Equals(x.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"\n[V20] Migrate Facilities (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"Sub",5}  {"V20 Code",-16}  {"Action",-8}  Title");
        for (int i = 0; i < facilities.Count; i++)
        {
            var f = facilities[i];
            string code = $"V16-FAC-{f.Id}";
            string companyKey = state.CompanyKeys[f.SubsidiaryId];
            string title = targetTitles[f.Id];
            var byCode = FindByCode(companyKey, code);
            var byTitle = FindByTitle(companyKey, title);
            string action = byCode == null
                ? (byTitle != null ? "CONFLICT" : "create")
                : SameTitle(byCode.Title, title) ? "exists"
                : (byTitle != null && byTitle.FacilityKey != byCode.FacilityKey ? "CONFLICT" : "update");
            string renamed = string.Equals(title, (f.Title ?? string.Empty).Trim(), StringComparison.Ordinal) ? string.Empty : "  (renamed: duplicate title in V16)";
            Console.WriteLine($"  {i + 1,3}  {f.Id,7}  {f.SubsidiaryId,5}  {code,-16}  {action,-8}  {title}{renamed}");
        }

        // เลือก Facility ที่จะ migrate (จำไว้ใน state ให้ขั้น Topic / Event ใช้ชุดเดียวกัน)
        List<V16Facility> selectedFacs;
        if (interactive)
        {
            var picked = PromptSelectIndexes(facilities.Count, "Select facilities [Enter = all, e.g. 1,3 or 1-2, 0 = cancel]: ");
            if (picked == null)
            {
                PrintWarning("Cancelled.");
                return false;
            }
            selectedFacs = picked.Select(i => facilities[i]).ToList();

            Console.WriteLine($"Selected {selectedFacs.Count} of {facilities.Count}: {string.Join(", ", selectedFacs.Select(x => $"[{x.Id}] {x.Title}"))}");
        }
        else
        {
            selectedFacs = state.SelectedFacilityIds == null
                ? facilities
                : facilities.Where(x => state.SelectedFacilityIds.Contains(x.Id)).ToList();
        }

        state.SelectedFacilityIds = selectedFacs.Count == facilities.Count ? null : selectedFacs.Select(x => x.Id).ToList();
        MigrationStateStore.Save(outputBasePath, state);

        int created = 0, updated = 0, existed = 0, failed = 0;
        foreach (var f in selectedFacs)
        {
            string companyKey = state.CompanyKeys[f.SubsidiaryId];
            string code = $"V16-FAC-{f.Id}";
            string title = targetTitles[f.Id];

            var existing = FindByCode(companyKey, code);
            if (existing != null)
            {
                state.FacilityKeys[f.Id] = existing.FacilityKey;
                MigrationStateStore.Save(outputBasePath, state);

                // [2026-09-23][Claude] ชื่อใน V16 เปลี่ยน -> PATCH (ถ้าชื่อใหม่ชน Facility อื่นใน Company เดียวกัน = ล้มเหลว ไม่แก้ทับ)
                if (!SameTitle(existing.Title, title))
                {
                    var clash = FindByTitle(companyKey, title);
                    if (clash != null && clash.FacilityKey != existing.FacilityKey)
                    {
                        failed++;
                        PrintError($"  ! [{f.Id}] cannot rename to '{title}': another facility in the same company already uses it (code '{clash.Code}', {clash.FacilityKey}).");
                        continue;
                    }

                    var upd = await api.UpdateFacilityAsync(existing.FacilityKey, new V20ApiClient.UpdateFacilityRequest { Title = title });
                    if (upd.Success && upd.Data != null)
                    {
                        var list = existingByCompany[companyKey];
                        list[list.IndexOf(existing)] = upd.Data;
                        updated++;
                        Console.WriteLine($"  ~ [{f.Id}] '{existing.Title}' -> '{title}' ({existing.FacilityKey})");
                    }
                    else
                    {
                        failed++;
                        PrintError($"  ! [{f.Id}] update title failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message}");
                    }
                    continue;
                }

                existed++;
                Console.WriteLine($"  = [{f.Id}] {title} -> {existing.FacilityKey} (exists)");
                continue;
            }

            var conflict = FindByTitle(companyKey, title);
            if (conflict != null)
            {
                failed++;
                PrintError($"  ! [{f.Id}] {title}: V20 already has a facility with this title (code '{conflict.Code}', {conflict.FacilityKey}) in the same company. Rename one of them, then run again.");
                continue;
            }

            var res = await api.CreateFacilityAsync(new V20ApiClient.CreateFacilityRequest
            {
                CompanyKey = companyKey,
                Code = code,
                Title = title,
                Remark = $"Migrated from ACAMP V16 Facility {f.Id}",
                IsActive = true
            });

            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.FacilityKey))
            {
                state.FacilityKeys[f.Id] = res.Data.FacilityKey;
                MigrationStateStore.Save(outputBasePath, state);
                existingByCompany[companyKey].Add(res.Data);
                created++;
                Console.WriteLine($"  + [{f.Id}] {title} -> {res.Data.FacilityKey}");
            }
            else
            {
                failed++;
                PrintError($"  ! [{f.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
            }
        }

        string summary = $"[V20] Facilities: created {created}, updated {updated}, existing {existed}, failed {failed} (selected {selectedFacs.Count} of {facilities.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (failed == 0) PrintSuccess(summary); else PrintWarning(summary);

        // [2026-09-23][Claude] Verify ระดับ Facility ผ่าน API migration-report (ชื่อตามกติกาเดียวกับตอนสร้าง, Company แม่)
        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "facilities", report =>
        {
            var r = new VerifyResult { Expected = selectedFacs.Count };
            var all = report.Corporates.SelectMany(c => c.Companies).SelectMany(x => x.Facilities.Select(f => (Comp: x, Fac: f))).ToList();
            var byCode = all.GroupBy(x => x.Fac.Code ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var f in selectedFacs)
            {
                string code = $"V16-FAC-{f.Id}";
                if (!byCode.TryGetValue(code, out var ff)) { r.Fails.Add($"{code} missing (V16 [{f.Id}] {f.Title})"); continue; }
                bool ok = true;
                string expected = targetTitles[f.Id];
                if (!string.Equals(ff.Fac.Title?.Trim(), expected, StringComparison.Ordinal)) { r.Fails.Add($"{code} title: expected '{expected}' vs V20 '{ff.Fac.Title}'"); ok = false; }
                string parent = $"V16-SUB-{f.SubsidiaryId}";
                if (!string.Equals(ff.Comp.Code, parent, StringComparison.OrdinalIgnoreCase)) { r.Fails.Add($"{code} under company '{ff.Comp.Code}', expected '{parent}'"); ok = false; }
                if (state.FacilityKeys.TryGetValue(f.Id, out var k) && k != ff.Fac.FacilityKey) r.Warns.Add($"{code} key in state {k} != V20 {ff.Fac.FacilityKey}");
                if (ok) r.Ok++;
            }
            var expectedCodes = selectedFacs.Select(f => $"V16-FAC-{f.Id}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var x in all.Where(x => !expectedCodes.Contains(x.Fac.Code ?? string.Empty)))
                r.Infos.Add($"'{(string.IsNullOrEmpty(x.Fac.Code) ? "(no code)" : x.Fac.Code)}' {x.Fac.Title} exists in V20 outside this run's scope");
            return r;
        });
        return failed == 0 && verified;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Emission Unit (Custom ของ Subscription: SubscribeId == sub) -> V20 (api/v2/emission-units)
    // จับคู่ตามลำดับ: 1) Custom ใน V20 ที่ Remark มี tag [V16-UNT-{id}] (ชื่อต่าง = update)  2) หน่วยใน V20 (Default/Custom) ที่ชื่อตรงกัน (ไม่สนตัวพิมพ์) = ใช้ตัวนั้น
    //                  3) ไม่พบ = สร้าง Custom ใหม่ Remark = "[V16-UNT-{id}] {V16 Memo}"
    // บันทึก V16 UnitId -> UnitKey ใน state (ขั้น Emission Source ใช้ต่อ) แล้ว Verify ผ่าน migration-report (รายการ Units)
    // migrate ทั้งหมดพร้อม Subscription (ไม่มีการเลือกรายการ)
    private static async Task<bool> ExecuteMigrateUnitsAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var v16Units = await dbContext.EmissionUnits
            .AsNoTracking()
            .Where(u => u.SubscribeId == subscriptionId)
            .OrderBy(u => u.OrderId)
            .ThenBy(u => u.Id)
            .ToListAsync();

        if (v16Units.Count == 0)
        {
            PrintWarning("No custom emission units in V16 for this subscription (system default units already exist in V20).");
            return true;
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);
        var listRes = await api.GetUnitsAsync();
        if (!listRes.Success || listRes.Data == null)
        {
            PrintError($"V20 API failed to list emission units: HTTP {(int)listRes.StatusCode} {listRes.Error?.Code} - {listRes.Error?.Message}");
            return false;
        }
        var v20Units = listRes.Data;

        static string Tag(int id) => $"[V16-UNT-{id}]";
        string TitleOf(V16EmissionUnit u) => TrimTitle200(u.Title, $"V16-UNT-{u.Id}");
        V20ApiClient.UnitDto? FindByTag(int id) =>
            v20Units.FirstOrDefault(x => x.SourceType == "Custom" && (x.Remark ?? string.Empty).Contains(Tag(id), StringComparison.OrdinalIgnoreCase));
        V20ApiClient.UnitDto? FindByTitle(string title) =>
            v20Units.FirstOrDefault(x => string.Equals((x.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"\n[V20] Migrate Emission Units (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"Action",-16}  Title");
        for (int i = 0; i < v16Units.Count; i++)
        {
            var u = v16Units[i];
            string title = TitleOf(u);
            var tagged = FindByTag(u.Id);
            var sameTitle = FindByTitle(title);
            string action = tagged != null
                ? (string.Equals(tagged.Title?.Trim(), title, StringComparison.Ordinal) ? "exists" : "update")
                : sameTitle != null ? $"use {sameTitle.SourceType.ToLowerInvariant()}" : "create";
            string change = action == "update" ? $"  (V20: '{tagged!.Title}')" : string.Empty;
            Console.WriteLine($"  {i + 1,3}  {u.Id,7}  {action,-16}  {title}{change}");
        }

        // [2026-09-23][Claude] Library ของ Subscription: migrate ทั้งหมดเสมอ (ไม่มีการเลือก)
        var selected = v16Units;

        int created = 0, updated = 0, existed = 0, failed = 0;
        foreach (var u in selected)
        {
            string title = TitleOf(u);
            var tagged = FindByTag(u.Id);

            if (tagged != null)
            {
                state.UnitKeys[u.Id] = tagged.UnitKey;
                MigrationStateStore.Save(outputBasePath, state);
                if (string.Equals(tagged.Title?.Trim(), title, StringComparison.Ordinal))
                {
                    existed++;
                    Console.WriteLine($"  = [{u.Id}] {title} -> {tagged.UnitKey} (exists)");
                    continue;
                }

                var upd = await api.UpdateUnitAsync(tagged.UnitKey, new V20ApiClient.UpdateUnitRequest { Title = title });
                if (upd.Success && upd.Data != null)
                {
                    v20Units[v20Units.IndexOf(tagged)] = upd.Data;
                    updated++;
                    Console.WriteLine($"  ~ [{u.Id}] '{tagged.Title}' -> '{title}' ({tagged.UnitKey})");
                }
                else
                {
                    failed++;
                    PrintError($"  ! [{u.Id}] update title failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message}");
                }
                continue;
            }

            var sameTitle = FindByTitle(title);
            if (sameTitle != null)
            {
                state.UnitKeys[u.Id] = sameTitle.UnitKey;
                MigrationStateStore.Save(outputBasePath, state);
                existed++;
                Console.WriteLine($"  = [{u.Id}] {title} -> {sameTitle.UnitKey} (use existing {sameTitle.SourceType.ToLowerInvariant()} unit)");
                continue;
            }

            string remark = $"{Tag(u.Id)} {(u.Memo ?? string.Empty).Trim()}".Trim();
            if (remark.Length > 500) remark = remark[..500];
            var res = await api.CreateUnitAsync(new V20ApiClient.CreateUnitRequest { Title = title, Remark = remark });
            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.UnitKey))
            {
                state.UnitKeys[u.Id] = res.Data.UnitKey;
                MigrationStateStore.Save(outputBasePath, state);
                v20Units.Add(res.Data);
                created++;
                Console.WriteLine($"  + [{u.Id}] {title} -> {res.Data.UnitKey}");
            }
            else
            {
                failed++;
                PrintError($"  ! [{u.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
            }
        }

        string summary = $"[V20] Emission Units: created {created}, updated {updated}, existing {existed}, failed {failed} (total {v16Units.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (failed == 0) PrintSuccess(summary); else PrintWarning(summary);

        // [2026-09-23][Claude] Verify: หน่วยที่ map ใน state ต้องมีอยู่ใน V20 (รายการ Units ของ migration-report) และชื่อตรง V16 (ไม่สนตัวพิมพ์)
        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "units", report =>
        {
            var r = new VerifyResult { Expected = selected.Count };
            var byKey = report.Units.GroupBy(x => x.UnitKey).ToDictionary(g => g.Key, g => g.First());
            foreach (var u in selected)
            {
                string title = TitleOf(u);
                if (!state.UnitKeys.TryGetValue(u.Id, out var key)) { r.Fails.Add($"V16 unit [{u.Id}] {title}: no V20 mapping in state"); continue; }
                if (!byKey.TryGetValue(key, out var v)) { r.Fails.Add($"V16 unit [{u.Id}] {title}: {key} not found in V20"); continue; }
                if (!string.Equals(v.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase)) { r.Fails.Add($"V16 unit [{u.Id}] title: V16 '{title}' vs V20 '{v.Title}'"); continue; }
                if (!v.IsActive) r.Warns.Add($"V16 unit [{u.Id}] {title}: V20 unit {key} is inactive");
                if (v.SourceType == "Default") r.Infos.Add($"V16 unit [{u.Id}] {title} mapped to V20 default unit {key}");
                r.Ok++;
            }
            return r;
        });
        return failed == 0 && verified;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Emission Group (Custom: SubscribeId == sub) -> V20 (api/v2/emission-groups) — migrate ทั้งหมดพร้อม Subscription
    // Scope: V16 ScopeId 1-4 = V20 TgoScopeNo 1-4 (4 = Separate Report); Code ของ V20 สร้างอัตโนมัติฝั่ง API (เช่น 03C01)
    // จับคู่: 1) Custom ที่ Remark มี tag [V16-GRP-{id}] (ชื่อต่าง = update)  2) กลุ่ม Default/Custom ที่ Title+Scope ตรงกัน = ใช้ตัวนั้น  3) ไม่พบ = สร้าง
    // Verify ผ่าน migration-report (รายการ Groups): มีอยู่จริง, ชื่อตรง (ไม่สนตัวพิมพ์), Scope ตรง
    private static async Task<bool> ExecuteMigrateGroupsAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var v16Groups = await dbContext.EmissionGroups
            .AsNoTracking()
            .Where(g => g.SubscribeId == subscriptionId)
            .OrderBy(g => g.ScopeId).ThenBy(g => g.OrderId).ThenBy(g => g.Id)
            .ToListAsync();

        if (v16Groups.Count == 0)
        {
            PrintWarning("No custom emission groups in V16 for this subscription (system default groups already exist in V20).");
            return true;
        }

        var badScope = v16Groups.Where(g => g.ScopeId < 1 || g.ScopeId > 4).ToList();
        if (badScope.Count > 0)
        {
            PrintError($"V16 group(s) with invalid ScopeId (expected 1-4): {string.Join(", ", badScope.Select(g => $"[{g.Id}] {g.Title} (ScopeId {g.ScopeId})"))}");
            return false;
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);
        var listRes = await api.GetGroupsAsync();
        if (!listRes.Success || listRes.Data == null)
        {
            PrintError($"V20 API failed to list emission groups: HTTP {(int)listRes.StatusCode} {listRes.Error?.Code} - {listRes.Error?.Message}");
            return false;
        }
        var v20Groups = listRes.Data;

        static string Tag(int id) => $"[V16-GRP-{id}]";
        string TitleOf(V16EmissionGroup g) => TrimTitle200(g.Title, $"V16-GRP-{g.Id}");
        V20ApiClient.GroupDto? FindByTag(int id) =>
            v20Groups.FirstOrDefault(x => x.SourceType == "Custom" && (x.Remark ?? string.Empty).Contains(Tag(id), StringComparison.OrdinalIgnoreCase));
        V20ApiClient.GroupDto? FindByTitleScope(string title, int scope) =>
            v20Groups.FirstOrDefault(x => x.Scope == scope && string.Equals((x.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"\n[V20] Migrate Emission Groups (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"Scope",-15}  {"Action",-16}  Title");
        for (int i = 0; i < v16Groups.Count; i++)
        {
            var g = v16Groups[i];
            string title = TitleOf(g);
            var tagged = FindByTag(g.Id);
            var same = FindByTitleScope(title, g.ScopeId);
            string action = tagged != null
                ? (string.Equals(tagged.Title?.Trim(), title, StringComparison.Ordinal) ? "exists" : "update")
                : same != null ? $"use {(same.SourceType ?? string.Empty).ToLowerInvariant()}" : "create";
            string change = action == "update" ? $"  (V20: '{tagged!.Title}')" : string.Empty;
            Console.WriteLine($"  {i + 1,3}  {g.Id,7}  {MapScopeIdToScopeText(g.ScopeId),-15}  {action,-16}  {title}{change}");
        }

        int created = 0, updated = 0, existed = 0, failed = 0;
        foreach (var g in v16Groups)
        {
            string title = TitleOf(g);
            var tagged = FindByTag(g.Id);

            if (tagged != null)
            {
                state.GroupKeys[g.Id] = tagged.GroupKey;
                MigrationStateStore.Save(outputBasePath, state);
                if (string.Equals(tagged.Title?.Trim(), title, StringComparison.Ordinal))
                {
                    existed++;
                    Console.WriteLine($"  = [{g.Id}] {title} -> {tagged.GroupKey} (exists)");
                    continue;
                }

                var upd = await api.UpdateGroupAsync(tagged.GroupKey, new V20ApiClient.UpdateGroupRequest { Title = title });
                if (upd.Success && upd.Data != null)
                {
                    v20Groups[v20Groups.IndexOf(tagged)] = upd.Data;
                    updated++;
                    Console.WriteLine($"  ~ [{g.Id}] '{tagged.Title}' -> '{title}' ({tagged.GroupKey})");
                }
                else
                {
                    failed++;
                    PrintError($"  ! [{g.Id}] update title failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message}");
                }
                continue;
            }

            var same = FindByTitleScope(title, g.ScopeId);
            if (same != null)
            {
                state.GroupKeys[g.Id] = same.GroupKey;
                MigrationStateStore.Save(outputBasePath, state);
                existed++;
                Console.WriteLine($"  = [{g.Id}] {title} -> {same.GroupKey} (use existing {(same.SourceType ?? string.Empty).ToLowerInvariant()} group {same.Code})");
                continue;
            }

            string remark = $"{Tag(g.Id)} {(g.Memo ?? string.Empty).Trim()}".Trim();
            if (remark.Length > 250) remark = remark[..250];
            var res = await api.CreateGroupAsync(new V20ApiClient.CreateGroupRequest { Title = title, Scope = g.ScopeId, Remark = remark });
            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.GroupKey))
            {
                state.GroupKeys[g.Id] = res.Data.GroupKey;
                MigrationStateStore.Save(outputBasePath, state);
                v20Groups.Add(res.Data);
                created++;
                Console.WriteLine($"  + [{g.Id}] {title} -> {res.Data.GroupKey} ({res.Data.Code})");
            }
            else
            {
                failed++;
                PrintError($"  ! [{g.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message}");
            }
        }

        string summary = $"[V20] Emission Groups: created {created}, updated {updated}, existing {existed}, failed {failed} (total {v16Groups.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (failed == 0) PrintSuccess(summary); else PrintWarning(summary);

        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "groups", report =>
        {
            var r = new VerifyResult { Expected = v16Groups.Count };
            var byKey = report.Groups.GroupBy(x => x.GroupKey).ToDictionary(x => x.Key, x => x.First());
            foreach (var g in v16Groups)
            {
                string title = TitleOf(g);
                if (!state.GroupKeys.TryGetValue(g.Id, out var key)) { r.Fails.Add($"V16 group [{g.Id}] {title}: no V20 mapping in state"); continue; }
                if (!byKey.TryGetValue(key, out var v)) { r.Fails.Add($"V16 group [{g.Id}] {title}: {key} not found in V20"); continue; }
                bool ok = true;
                if (!string.Equals(v.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase)) { r.Fails.Add($"V16 group [{g.Id}] title: V16 '{title}' vs V20 '{v.Title}'"); ok = false; }
                if (v.Scope != g.ScopeId) { r.Fails.Add($"V16 group [{g.Id}] scope: V16 {g.ScopeId} vs V20 {v.Scope}"); ok = false; }
                if (!v.IsActive) r.Warns.Add($"V16 group [{g.Id}] {title}: V20 group {key} is inactive");
                if (v.SourceType == "Default") r.Infos.Add($"V16 group [{g.Id}] {title} mapped to V20 default group {v.Code}");
                if (ok) r.Ok++;
            }
            return r;
        });
        return failed == 0 && verified;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Emission Source (Custom: SubscribeId == sub) -> V20 (api/v2/emission-sources) — migrate ทั้งหมดพร้อม Subscription
    // Group: state.GroupKeys (Custom ที่ migrate แล้ว) ถ้าไม่มี (กลุ่ม Default ของ V16):
    //        1) appsettings MigrationSettings:V16GroupMap { "V16 GroupId": "V20 Group Code" }  2) กลุ่ม V20 Scope เดียวกันที่ Title/TitleTh ตรงชื่อ V16 (หรือชื่อ V16 ที่ตัดคำนำหน้า "กลุ่ม")
    // Unit:  state.UnitKeys ถ้าไม่มี: 1) appsettings MigrationSettings:V16UnitMap { "V16 UnitId": "V20 Unit Title" }  2) หน่วย V20 ที่ชื่อตรงกัน
    // ค่าก๊าซส่งตาม V16, ค่า GWP ส่งค่าที่ V16 ใช้คำนวณจริง (<= 0 ใช้ค่า fallback เดียวกับ V16SourceEmission.CalculateTotalEf) เพื่อให้ EF ของ V20 = V16
    // ชื่อซ้ำภายใน V16 (V20 ห้าม Title ซ้ำ) -> รายการที่ 2 เป็นต้นไปเติม " (V16-ESR-{id})"
    // จับคู่: 1) Custom ที่ Remark มี tag [V16-ESR-{id}] (ค่าต่าง = update ทุกฟิลด์)  2) Source Default/Custom ที่ชื่อตรงกัน = ใช้ตัวนั้น  3) ไม่พบ = สร้าง
    // Verify ผ่าน migration-report (รายการ Sources): มีอยู่จริง, ชื่อตรง, Group/Unit ตรง และ EF ตรง V16 (ตัวที่ใช้ของเดิม: EF ต่าง = WARN)
    // [2026-09-23][Claude] Source ที่ย้ายไม่ได้ (จับคู่ Group/Unit ไม่ได้, ค่าไม่ใช่ตัวเลข, API ปฏิเสธ) = ข้าม + WARN ไม่หยุด migration; Verify ไม่นับรายการที่ข้าม
    private static async Task<bool> ExecuteMigrateSourcesAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var v16Sources = await dbContext.EmissionSources
            .AsNoTracking()
            .Include(s => s.Group)
            .Include(s => s.Unit)
            .Where(s => s.SubscribeId == subscriptionId)
            .OrderBy(s => s.Group != null ? s.Group.ScopeId : 0)
            .ThenBy(s => s.GroupId)
            .ThenBy(s => s.Id)
            .ToListAsync();

        if (v16Sources.Count == 0)
        {
            PrintWarning("No custom emission sources in V16 for this subscription (system default sources already exist in V20).");
            return true;
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);
        var groupsRes = await api.GetGroupsAsync();
        if (!groupsRes.Success || groupsRes.Data == null)
        {
            PrintError($"V20 API failed to list emission groups: HTTP {(int)groupsRes.StatusCode} {groupsRes.Error?.Code} - {groupsRes.Error?.Message}");
            return false;
        }
        var unitsRes = await api.GetUnitsAsync();
        if (!unitsRes.Success || unitsRes.Data == null)
        {
            PrintError($"V20 API failed to list emission units: HTTP {(int)unitsRes.StatusCode} {unitsRes.Error?.Code} - {unitsRes.Error?.Message}");
            return false;
        }
        var listRes = await api.GetSourcesAsync();
        if (!listRes.Success || listRes.Data == null)
        {
            PrintError($"V20 API failed to list emission sources: HTTP {(int)listRes.StatusCode} {listRes.Error?.Code} - {listRes.Error?.Message}");
            return false;
        }
        var v20Groups = groupsRes.Data;
        var v20Units = unitsRes.Data;
        var v20Sources = listRes.Data;
        var targetTitles = BuildSourceTargetTitles(v16Sources);

        static string Tag(int id) => $"[V16-ESR-{id}]";
        V20ApiClient.SourceDto? FindByTag(int id) =>
            v20Sources.FirstOrDefault(x => x.SourceType == "Custom" && (x.Remark ?? string.Empty).Contains(Tag(id), StringComparison.OrdinalIgnoreCase));
        V20ApiClient.SourceDto? FindByTitle(string title) =>
            v20Sources.FirstOrDefault(x => string.Equals((x.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase));

        // [2026-09-23][Claude] จับคู่กลุ่ม/หน่วย Default ของ V16 -> V20 (แจ้งผลการจับคู่ครั้งเดียวต่อ V16 Id เพื่อให้ตรวจได้)
        var groupMapConfig = config.GetSection("MigrationSettings:V16GroupMap");
        var unitMapConfig = config.GetSection("MigrationSettings:V16UnitMap");
        var reportedDefaultMatch = new HashSet<string>();
        static bool SameText(string? a, string? b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        string? ResolveGroupKey(V16EmissionSource s)
        {
            if (state.GroupKeys.TryGetValue(s.GroupId, out var key)) return key;
            if (s.Group == null) return null;

            V20ApiClient.GroupDto? match;
            string how;
            string? mappedCode = groupMapConfig[s.GroupId.ToString()];
            if (!string.IsNullOrWhiteSpace(mappedCode))
            {
                match = v20Groups.FirstOrDefault(g => string.Equals(g.Code, mappedCode.Trim(), StringComparison.OrdinalIgnoreCase));
                how = $"V16GroupMap -> code {mappedCode.Trim()}";
            }
            else
            {
                string title = (s.Group.Title ?? string.Empty).Trim();
                string stripped = title.StartsWith("กลุ่ม", StringComparison.Ordinal) ? title["กลุ่ม".Length..].Trim() : title;
                var candidates = v20Groups
                    .Where(g => g.Scope == s.Group.ScopeId &&
                                (SameText(g.Title, title) || SameText(g.TitleTh, title) || SameText(g.Title, stripped) || SameText(g.TitleTh, stripped)))
                    .ToList();
                match = candidates.Count == 1 ? candidates[0] : null;
                how = candidates.Count > 1 ? $"ambiguous ({candidates.Count} V20 groups)" : "title match";
            }

            string reportKey = $"G{s.GroupId}";
            if (reportedDefaultMatch.Add(reportKey))
            {
                if (match != null) Console.WriteLine($"  V16 default group [{s.GroupId}] '{s.Group.Title}' -> V20 {match.Code} '{match.Title}' / '{match.TitleTh}' ({how})");
            }
            return match?.GroupKey;
        }
        string? ResolveUnitKey(V16EmissionSource s)
        {
            if (state.UnitKeys.TryGetValue(s.UnitId, out var key)) return key;
            if (s.Unit == null) return null;
            string? mappedTitle = unitMapConfig[s.UnitId.ToString()];
            string title = !string.IsNullOrWhiteSpace(mappedTitle) ? mappedTitle.Trim() : (s.Unit.Title ?? string.Empty).Trim();
            return v20Units.FirstOrDefault(u => string.Equals((u.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase))?.UnitKey;
        }

        // ค่าที่จะส่งไป V20 (null = ค่าใน V16 ไม่ใช่ตัวเลขที่ใช้ได้)
        V20ApiClient.SourceRequest? BuildRequest(V16EmissionSource s, string title, string groupKey, string unitKey)
        {
            float[] raw = { s.CO2, s.FossilCH4, s.CH4, s.N2O, s.SF6, s.NF3, s.HFCs, s.PFCs, s.gwpCO2, s.gwpFossilCH4, s.gwpCH4, s.gwpN2O, s.gwpSF6, s.gwpNF3, s.gwpHFCs, s.gwpPFCs };
            if (raw.Any(v => float.IsNaN(v) || float.IsInfinity(v))) return null;
            static decimal Gwp(float v, float fallback) => (decimal)(v > 0 ? v : fallback);
            return new V20ApiClient.SourceRequest
            {
                Title = title,
                GroupKey = groupKey,
                UnitKey = unitKey,
                Remark = Tag(s.Id),
                Ref = (s.Ref ?? string.Empty).Trim(),
                CO2 = (decimal)s.CO2,
                FossilCH4 = (decimal)s.FossilCH4,
                CH4 = (decimal)s.CH4,
                N2O = (decimal)s.N2O,
                SF6 = (decimal)s.SF6,
                NF3 = (decimal)s.NF3,
                HFCs = (decimal)s.HFCs,
                PFCs = (decimal)s.PFCs,
                GwpCO2 = Gwp(s.gwpCO2, 1f),
                GwpFossilCH4 = Gwp(s.gwpFossilCH4, 28f),
                GwpCH4 = Gwp(s.gwpCH4, 28f),
                GwpN2O = Gwp(s.gwpN2O, 265f),
                GwpSF6 = Gwp(s.gwpSF6, 23500f),
                GwpNF3 = Gwp(s.gwpNF3, 16100f),
                GwpHFCs = Gwp(s.gwpHFCs, 1f),
                GwpPFCs = Gwp(s.gwpPFCs, 1f),
                GwpVersion = "AR5"
            };
        }

        static bool SameValues(V20ApiClient.SourceDto v, V20ApiClient.SourceRequest r) =>
            string.Equals(v.Title?.Trim(), r.Title, StringComparison.Ordinal) &&
            v.GroupKey == r.GroupKey && v.UnitKey == r.UnitKey &&
            string.Equals((v.Ref ?? string.Empty).Trim(), r.Ref, StringComparison.Ordinal) &&
            v.CO2 == r.CO2 && v.FossilCH4 == r.FossilCH4 && v.CH4 == r.CH4 && v.N2O == r.N2O &&
            v.SF6 == r.SF6 && v.NF3 == r.NF3 && v.HFCs == r.HFCs && v.PFCs == r.PFCs &&
            v.GwpCO2 == r.GwpCO2 && v.GwpFossilCH4 == r.GwpFossilCH4 && v.GwpCH4 == r.GwpCH4 && v.GwpN2O == r.GwpN2O &&
            v.GwpSF6 == r.GwpSF6 && v.GwpNF3 == r.GwpNF3 && v.GwpHFCs == r.GwpHFCs && v.GwpPFCs == r.GwpPFCs;

        Console.WriteLine($"\n[V20] Migrate Emission Sources (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"Scope",-15}  {"Action",-16}  Title");
        for (int i = 0; i < v16Sources.Count; i++)
        {
            var s = v16Sources[i];
            string title = targetTitles[s.Id];
            var tagged = FindByTag(s.Id);
            var same = FindByTitle(title);
            string action = tagged != null ? "exists/update"
                : same != null ? $"use {(same.SourceType ?? string.Empty).ToLowerInvariant()}" : "create";
            string scopeText = s.Group != null ? MapScopeIdToScopeText(s.Group.ScopeId) : "-";
            Console.WriteLine($"  {i + 1,3}  {s.Id,7}  {scopeText,-15}  {action,-16}  {title}");
        }

        var usedExisting = new HashSet<int>();
        var expectedKeys = new Dictionary<int, (string GroupKey, string UnitKey)>();
        // [2026-09-23][Claude] Source ที่ย้ายไม่ได้ = ข้าม + แจ้งเตือน (ไม่หยุด migration) — ตรวจ Verify เฉพาะรายการที่ย้ายได้
        var skipped = new Dictionary<int, string>();
        int created = 0, updated = 0, existed = 0;
        foreach (var s in v16Sources)
        {
            string title = targetTitles[s.Id];
            var tagged = FindByTag(s.Id);

            if (tagged == null)
            {
                var same = FindByTitle(title);
                if (same != null)
                {
                    state.SourceKeys[s.Id] = same.SourceKey;
                    MigrationStateStore.Save(outputBasePath, state);
                    usedExisting.Add(s.Id);
                    existed++;
                    Console.WriteLine($"  = [{s.Id}] {title} -> {same.SourceKey} (use existing {(same.SourceType ?? string.Empty).ToLowerInvariant()} source)");
                    continue;
                }
            }

            string? groupKey = ResolveGroupKey(s);
            string? unitKey = ResolveUnitKey(s);
            if (groupKey == null || unitKey == null)
            {
                skipped[s.Id] = groupKey == null ? $"group [{s.GroupId}] '{s.Group?.Title}' not matched" : $"unit [{s.UnitId}] '{s.Unit?.Title}' not matched";
                if (groupKey == null) PrintWarning($"  ! [{s.Id}] {title}: V16 group [{s.GroupId}] '{s.Group?.Title}' (Scope {s.Group?.ScopeId}) not matched to a V20 group — " +
                                                 $"custom group: run the Emission Groups step; default group: add \"{s.GroupId}\": \"<V20 group code>\" to MigrationSettings:V16GroupMap in appsettings.json.");
                if (unitKey == null) PrintWarning($"  ! [{s.Id}] {title}: V16 unit [{s.UnitId}] '{s.Unit?.Title}' not matched to a V20 unit — " +
                                                $"custom unit: run the Emission Units step; default unit: add \"{s.UnitId}\": \"<V20 unit title>\" to MigrationSettings:V16UnitMap in appsettings.json.");
                continue;
            }

            var request = BuildRequest(s, title, groupKey, unitKey);
            if (request == null)
            {
                skipped[s.Id] = "gas/GWP value is not a valid number";
                PrintWarning($"  ! [{s.Id}] {title}: V16 gas/GWP value is not a valid number (NaN/Infinity) — skipped.");
                continue;
            }
            expectedKeys[s.Id] = (groupKey, unitKey);

            if (tagged != null)
            {
                state.SourceKeys[s.Id] = tagged.SourceKey;
                MigrationStateStore.Save(outputBasePath, state);
                if (SameValues(tagged, request))
                {
                    existed++;
                    Console.WriteLine($"  = [{s.Id}] {title} -> {tagged.SourceKey} (exists)");
                    continue;
                }

                var upd = await api.UpdateSourceAsync(tagged.SourceKey, request);
                if (upd.Success && upd.Data != null)
                {
                    v20Sources[v20Sources.IndexOf(tagged)] = upd.Data;
                    updated++;
                    Console.WriteLine($"  ~ [{s.Id}] {title} ({tagged.SourceKey}) updated from V16");
                }
                else
                {
                    skipped[s.Id] = $"update failed HTTP {(int)upd.StatusCode} {upd.Error?.Code}";
                    PrintWarning($"  ! [{s.Id}] update failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message} — skipped (V20 keeps old values).");
                }
                continue;
            }

            var res = await api.CreateSourceAsync(request);
            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.SourceKey))
            {
                state.SourceKeys[s.Id] = res.Data.SourceKey;
                MigrationStateStore.Save(outputBasePath, state);
                v20Sources.Add(res.Data);
                created++;
                Console.WriteLine($"  + [{s.Id}] {title} -> {res.Data.SourceKey} (EF {res.Data.Ef})");
            }
            else
            {
                skipped[s.Id] = $"create failed HTTP {(int)res.StatusCode} {res.Error?.Code}";
                PrintWarning($"  ! [{s.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message} — skipped.");
            }
        }

        string summary = $"[V20] Emission Sources: created {created}, updated {updated}, existing {existed}, skipped {skipped.Count} (total {v16Sources.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (skipped.Count == 0) PrintSuccess(summary); else PrintWarning(summary);

        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "sources", report =>
        {
            var r = new VerifyResult { Expected = v16Sources.Count - skipped.Count };
            var byKey = report.Sources.GroupBy(x => x.SourceKey).ToDictionary(x => x.Key, x => x.First());
            foreach (var s in v16Sources)
            {
                string title = targetTitles[s.Id];
                if (skipped.TryGetValue(s.Id, out var reason)) { r.Warns.Add($"V16 source [{s.Id}] {title}: skipped ({reason})"); continue; }
                if (!state.SourceKeys.TryGetValue(s.Id, out var key)) { r.Fails.Add($"V16 source [{s.Id}] {title}: no V20 mapping in state"); continue; }
                if (!byKey.TryGetValue(key, out var v)) { r.Fails.Add($"V16 source [{s.Id}] {title}: {key} not found in V20"); continue; }
                if (!string.Equals(v.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase)) { r.Fails.Add($"V16 source [{s.Id}] title: V16 '{title}' vs V20 '{v.Title}'"); continue; }

                decimal v16Ef = s.CalculateTotalEf();
                bool efMatch = Math.Abs(v.Ef - v16Ef) <= Math.Max(0.000001m, Math.Abs(v16Ef) * 0.00001m);
                if (usedExisting.Contains(s.Id))
                {
                    r.Infos.Add($"V16 source [{s.Id}] {title} mapped to existing V20 {v.SourceType.ToLowerInvariant()} source {key}");
                    if (!efMatch) r.Warns.Add($"V16 source [{s.Id}] {title}: EF V16 {v16Ef} vs existing V20 {key} EF {v.Ef}");
                    r.Ok++;
                    continue;
                }

                bool ok = true;
                if (expectedKeys.TryGetValue(s.Id, out var exp))
                {
                    if (v.GroupKey != exp.GroupKey) { r.Fails.Add($"V16 source [{s.Id}] {title}: group V20 {v.GroupKey} vs expected {exp.GroupKey}"); ok = false; }
                    if (v.UnitKey != exp.UnitKey) { r.Fails.Add($"V16 source [{s.Id}] {title}: unit V20 {v.UnitKey} vs expected {exp.UnitKey}"); ok = false; }
                }
                if (s.Group != null && v.Scope != s.Group.ScopeId) { r.Fails.Add($"V16 source [{s.Id}] {title}: scope V16 {s.Group.ScopeId} vs V20 {v.Scope}"); ok = false; }
                if (!efMatch) { r.Fails.Add($"V16 source [{s.Id}] {title}: EF V16 {v16Ef} vs V20 {v.Ef}"); ok = false; }
                if (!v.IsActive) r.Warns.Add($"V16 source [{s.Id}] {title}: V20 source {key} is inactive");
                if (ok) r.Ok++;
            }
            return r;
        });
        return verified;
        #endregion
    }

    // [2026-09-23][Claude] ชื่อ Emission Source ที่จะใช้ใน V20 (ใช้ทั้งขั้น migrate และ verify) — V20 ห้าม Title ซ้ำทั้ง Subscription
    // ชื่อซ้ำภายใน V16 -> รายการที่ 2 เป็นต้นไปเติม " (V16-ESR-{id})"; ว่าง = "V16-ESR-{id}"; ตัดที่ 200 ตัวอักษร
    private static Dictionary<int, string> BuildSourceTargetTitles(IEnumerable<V16EmissionSource> sources)
    {
        #region hide [2026-09-23]
        var targetTitles = new Dictionary<int, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sources.OrderBy(x => x.Id))
        {
            string code = $"V16-ESR-{s.Id}";
            string title = (s.Title ?? string.Empty).Trim();
            if (title.Length == 0) title = code;
            if (!used.Add(title))
            {
                string suffix = $" ({code})";
                title = (title.Length + suffix.Length > 200 ? title[..(200 - suffix.Length)] : title) + suffix;
                used.Add(title);
            }
            if (title.Length > 200) title = title[..200];
            targetTitles[s.Id] = title;
        }
        return targetTitles;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Activity Topic -> V20 Topic (api/v2/topics) — เฉพาะ Facility ที่ migrate แล้ว (state.FacilityKeys) และอยู่ในชุดที่เลือก (state.SelectedFacilityIds)
    // Source: state.SourceKeys (Custom ที่ migrate แล้ว) ถ้าไม่มี (Source Default ของ V16): 1) MigrationSettings:V16SourceMap { V16 SourceId: V20 Source Title }  2) Source V20 ที่ชื่อตรงกัน (ต้องเจอ 1 ตัว)
    // ชื่อซ้ำใน Facility เดียวกันของ V16 (V20 ห้ามซ้ำ) -> รายการที่ 2 เป็นต้นไปเติม " (V16-TPC-{id})"; ตัดที่ 200 ตัวอักษร
    // จับคู่: 1) Topic ที่ Remark มี tag [V16-TPC-{id}] (ชื่อต่าง = update)  2) Topic ใน Facility เดียวกันที่ชื่อตรงกัน = ใช้ตัวนั้น  3) ไม่พบ = สร้าง (Remark = "[V16-TPC-{id}] {V16 Memo}")
    // Topic ที่ย้ายไม่ได้ = ข้าม + WARN (ไม่หยุด); Verify ผ่าน migration-report (includeTopics): Topic อยู่ใต้ Facility ที่ถูกต้องและชื่อตรง
    private static async Task<bool> ExecuteMigrateTopicsAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId)
    {
        #region hide [2026-09-23]
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var facilityIds = state.FacilityKeys.Keys
            .Where(id => state.SelectedFacilityIds == null || state.SelectedFacilityIds.Contains(id))
            .ToList();
        if (facilityIds.Count == 0)
        {
            PrintError("No facility has been migrated to V20 yet. Run the Facilities step first.");
            return false;
        }

        var v16Topics = await dbContext.ActivityTopics
            .AsNoTracking()
            .Include(t => t.Source)
            .Where(t => facilityIds.Contains(t.FacilityId))
            .OrderBy(t => t.FacilityId).ThenBy(t => t.OrderId).ThenBy(t => t.Id)
            .ToListAsync();

        if (v16Topics.Count == 0)
        {
            PrintWarning("No topics in V16 for the migrated facilities.");
            return true;
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);
        var sourcesRes = await api.GetSourcesAsync();
        if (!sourcesRes.Success || sourcesRes.Data == null)
        {
            PrintError($"V20 API failed to list emission sources: HTTP {(int)sourcesRes.StatusCode} {sourcesRes.Error?.Code} - {sourcesRes.Error?.Message}");
            return false;
        }
        var topicsRes = await api.GetAllTopicsAsync();
        if (!topicsRes.Success || topicsRes.Data == null)
        {
            PrintError($"V20 API failed to list topics: HTTP {(int)topicsRes.StatusCode} {topicsRes.Error?.Code} - {topicsRes.Error?.Message}");
            return false;
        }
        var v20Sources = sourcesRes.Data;
        var v20Topics = topicsRes.Data;
        var targetTitles = BuildTopicTargetTitles(v16Topics);
        var sourceMapConfig = config.GetSection("MigrationSettings:V16SourceMap");

        static string Tag(int id) => $"[V16-TPC-{id}]";
        V20ApiClient.TopicDto? FindByTag(int id) =>
            v20Topics.FirstOrDefault(x => (x.Remark ?? string.Empty).Contains(Tag(id), StringComparison.OrdinalIgnoreCase));
        V20ApiClient.TopicDto? FindByTitle(string facilityKey, string title) =>
            v20Topics.FirstOrDefault(x => x.TargetKey == facilityKey && string.Equals((x.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase));

        // คืน (SourceKey, เหตุผลเมื่อหาไม่ได้)
        (string? Key, string? Reason) ResolveSourceKey(V16ActivityTopic t)
        {
            if (state.SourceKeys.TryGetValue(t.SourceId, out var key)) return (key, null);
            if (t.Source == null) return (null, $"V16 source [{t.SourceId}] not found in V16");
            string? mapped = sourceMapConfig[t.SourceId.ToString()];
            string title = !string.IsNullOrWhiteSpace(mapped) ? mapped.Trim() : (t.Source.Title ?? string.Empty).Trim();
            var candidates = v20Sources.Where(s => string.Equals((s.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 1) return (candidates[0].SourceKey, null);
            return (null, candidates.Count == 0
                ? $"V16 source [{t.SourceId}] '{t.Source.Title}' not matched to a V20 source — add \"{t.SourceId}\": \"<V20 source title>\" to MigrationSettings:V16SourceMap"
                : $"V16 source [{t.SourceId}] '{title}' matches {candidates.Count} V20 sources — add \"{t.SourceId}\": \"<unique V20 source title>\" to MigrationSettings:V16SourceMap");
        }

        Console.WriteLine($"\n[V20] Migrate Topics (V20 SubscriptionId = {state.V20SubscriptionId}, {facilityIds.Count} facility(ies))");
        Console.WriteLine($"  {"#",3}  {"V16 Id",7}  {"Fac",5}  {"Action",-10}  Title");
        for (int i = 0; i < v16Topics.Count; i++)
        {
            var t = v16Topics[i];
            string title = targetTitles[t.Id];
            string facKey = state.FacilityKeys[t.FacilityId];
            string action = FindByTag(t.Id) != null ? "exists" : FindByTitle(facKey, title) != null ? "use" : "create";
            Console.WriteLine($"  {i + 1,3}  {t.Id,7}  {t.FacilityId,5}  {action,-10}  {title}");
        }

        var skipped = new Dictionary<int, string>();
        int created = 0, updated = 0, existed = 0;
        foreach (var t in v16Topics)
        {
            string title = targetTitles[t.Id];
            string facKey = state.FacilityKeys[t.FacilityId];
            var tagged = FindByTag(t.Id);

            if (tagged != null)
            {
                state.TopicKeys[t.Id] = tagged.TopicKey;
                MigrationStateStore.Save(outputBasePath, state);
                if (string.Equals(tagged.Title?.Trim(), title, StringComparison.Ordinal))
                {
                    existed++;
                    Console.WriteLine($"  = [{t.Id}] {title} -> {tagged.TopicKey} (exists)");
                    continue;
                }

                var upd = await api.UpdateTopicAsync(tagged.TopicKey, new V20ApiClient.UpdateTopicRequest { Title = title });
                if (upd.Success && upd.Data != null)
                {
                    v20Topics[v20Topics.IndexOf(tagged)] = upd.Data;
                    updated++;
                    Console.WriteLine($"  ~ [{t.Id}] '{tagged.Title}' -> '{title}' ({tagged.TopicKey})");
                }
                else
                {
                    skipped[t.Id] = $"update title failed HTTP {(int)upd.StatusCode} {upd.Error?.Code}";
                    PrintWarning($"  ! [{t.Id}] update title failed: HTTP {(int)upd.StatusCode} {upd.Error?.Code} - {upd.Error?.Message} — skipped.");
                }
                continue;
            }

            var same = FindByTitle(facKey, title);
            if (same != null)
            {
                state.TopicKeys[t.Id] = same.TopicKey;
                MigrationStateStore.Save(outputBasePath, state);
                existed++;
                Console.WriteLine($"  = [{t.Id}] {title} -> {same.TopicKey} (use existing topic in facility)");
                continue;
            }

            var (sourceKey, reason) = ResolveSourceKey(t);
            if (sourceKey == null)
            {
                skipped[t.Id] = reason ?? "source not matched";
                PrintWarning($"  ! [{t.Id}] {title}: {reason} — skipped.");
                continue;
            }

            string remark = $"{Tag(t.Id)} {(t.Memo ?? string.Empty).Trim()}".Trim();
            if (remark.Length > 500) remark = remark[..500];
            var res = await api.CreateTopicAsync(new V20ApiClient.CreateTopicRequest
            {
                Title = title,
                Remark = remark,
                TargetKey = facKey,
                EmissionSourceKey = sourceKey,
                UnitConversionRatio = 1m,
                EmissionSharePercent = t.EmissionSharePercent,
                IsActive = true
            });
            if (res.Success && res.Data != null && !string.IsNullOrWhiteSpace(res.Data.TopicKey))
            {
                state.TopicKeys[t.Id] = res.Data.TopicKey;
                MigrationStateStore.Save(outputBasePath, state);
                v20Topics.Add(res.Data);
                created++;
                string group = res.Data.Childs.FirstOrDefault()?.ActivityGroup ?? "-";
                Console.WriteLine($"  + [{t.Id}] {title} -> {res.Data.TopicKey} (activity group: {group})");
            }
            else
            {
                skipped[t.Id] = $"create failed HTTP {(int)res.StatusCode} {res.Error?.Code}";
                PrintWarning($"  ! [{t.Id}] {title}: HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message} — skipped.");
            }
        }

        string summary = $"[V20] Topics: created {created}, updated {updated}, existing {existed}, skipped {skipped.Count} (total {v16Topics.Count}).\n" +
                         $"State: {MigrationStateStore.GetStatePath(outputBasePath, subscriptionId)}";
        if (skipped.Count == 0) PrintSuccess(summary); else PrintWarning(summary);

        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "topics", report =>
        {
            var r = new VerifyResult { Expected = v16Topics.Count - skipped.Count };
            var facilityByKey = report.Corporates.SelectMany(c => c.Companies).SelectMany(c => c.Facilities)
                .GroupBy(f => f.FacilityKey).ToDictionary(g => g.Key, g => g.First());
            foreach (var t in v16Topics)
            {
                string title = targetTitles[t.Id];
                if (skipped.TryGetValue(t.Id, out var reason)) { r.Warns.Add($"V16 topic [{t.Id}] {title}: skipped ({reason})"); continue; }
                if (!state.TopicKeys.TryGetValue(t.Id, out var key)) { r.Fails.Add($"V16 topic [{t.Id}] {title}: no V20 mapping in state"); continue; }
                string facKey = state.FacilityKeys[t.FacilityId];
                if (!facilityByKey.TryGetValue(facKey, out var fac)) { r.Fails.Add($"V16 topic [{t.Id}] {title}: facility {facKey} not found in V20"); continue; }
                var v = fac.Topics?.FirstOrDefault(x => x.TopicKey == key);
                if (v == null) { r.Fails.Add($"V16 topic [{t.Id}] {title}: {key} not found under facility {facKey}"); continue; }
                if (!string.Equals(v.Title?.Trim(), title, StringComparison.OrdinalIgnoreCase)) { r.Fails.Add($"V16 topic [{t.Id}] title: V16 '{title}' vs V20 '{v.Title}'"); continue; }
                if (!v.IsActive) r.Warns.Add($"V16 topic [{t.Id}] {title}: V20 topic {key} is inactive");
                r.Ok++;
            }
            return r;
        }, includeTopics: true);
        return verified;
        #endregion
    }

    // [2026-09-23][Claude] ชื่อ Topic ที่จะใช้ใน V20 — V20 ห้ามชื่อซ้ำใน Target เดียวกัน
    // ชื่อซ้ำใน Facility เดียวกันของ V16 -> รายการที่ 2 เป็นต้นไปเติม " (V16-TPC-{id})"; ว่าง = "V16-TPC-{id}"; ตัดที่ 200 ตัวอักษร
    private static Dictionary<int, string> BuildTopicTargetTitles(IEnumerable<V16ActivityTopic> topics)
    {
        #region hide [2026-09-23]
        var targetTitles = new Dictionary<int, string>();
        foreach (var group in topics.OrderBy(t => t.FacilityId).ThenBy(t => t.OrderId).ThenBy(t => t.Id).GroupBy(t => t.FacilityId))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in group)
            {
                string code = $"V16-TPC-{t.Id}";
                string title = (t.Title ?? string.Empty).Trim();
                if (title.Length == 0) title = code;
                if (title.Length > 200) title = title[..200];
                if (!used.Add(title))
                {
                    string suffix = $" ({code})";
                    title = (title.Length + suffix.Length > 200 ? title[..(200 - suffix.Length)] : title) + suffix;
                    used.Add(title);
                }
                targetTitles[t.Id] = title;
            }
        }
        return targetTitles;
        #endregion
    }

    // [2026-09-23][Claude] Step: V16 Activity Event -> V20 Event (api/v2/events/ingest) — เฉพาะ Topic ที่ migrate แล้ว (state.TopicKeys) ใน Facility ที่เลือก
    // V20 รับ 1 รายการต่อ Topic ต่อเดือน -> รวมยอด V16 รายเดือน (Quantity = ผลรวม, Remark = Title/Memo ที่ไม่ซ้ำ ต่อกันด้วย "; " ตัดที่ 250)
    // OccurredAt = วันที่ 1 ของเดือน 00:00 ตามโซนเวลาของระบบ (AppTimeZone); ExternalRefId = "v16:{V16SubId 8 ตัวแรก}:{V16TopicId}:{yyyy-MM}" -> รันซ้ำได้ (replay)
    // ส่งครั้งละ 500; รายการที่ถูกปฏิเสธ = ข้าม + WARN (duplicate_external_ref = ยอด V16 เปลี่ยนหลัง migrate ครั้งก่อน, V20 คงค่าเดิม)
    // Verify ผ่าน migration-report (includeTopics): จำนวน Event และผลรวม Quantity ต่อ Topic ตรงกับ V16 (Topic ที่มีรายการถูกข้าม = WARN)
    private static async Task<bool> ExecuteMigrateEventsAsync(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId)
    {
        #region hide [2026-09-23]
        const int BatchSize = 500;
        var session = await PrepareV20SessionAsync(config, subscriptionId);
        if (session == null) return false;
        var (state, outputBasePath, baseUrl) = session.Value;

        var facilityIds = state.FacilityKeys.Keys
            .Where(id => state.SelectedFacilityIds == null || state.SelectedFacilityIds.Contains(id))
            .ToList();
        var topics = await dbContext.ActivityTopics
            .AsNoTracking()
            .Where(t => facilityIds.Contains(t.FacilityId))
            .Select(t => new { t.Id, t.Title, t.FacilityId })
            .ToListAsync();
        var mappedTopics = topics.Where(t => state.TopicKeys.ContainsKey(t.Id)).ToList();
        int unmappedTopics = topics.Count - mappedTopics.Count;
        if (mappedTopics.Count == 0)
        {
            PrintError("No topic has been migrated to V20 yet. Run the Topics step first.");
            return false;
        }
        if (unmappedTopics > 0)
        {
            PrintWarning($"{unmappedTopics} V16 topic(s) were not migrated (skipped in the Topics step) — their events are skipped.");
        }

        var topicIds = mappedTopics.Select(t => t.Id).ToList();
        var v16Events = await dbContext.ActivityEvents
            .AsNoTracking()
            .Where(e => topicIds.Contains(e.TopicId))
            .Select(e => new { e.TopicId, e.TimeStamp, e.Quantity, e.Title, e.Memo })
            .ToListAsync();

        string subPrefix = subscriptionId.ToString("N")[..8];
        var monthly = v16Events
            .GroupBy(e => new { e.TopicId, Month = e.TimeStamp.ToString("yyyy-MM") })
            .OrderBy(g => g.Key.TopicId).ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var remarks = g.Select(ev => !string.IsNullOrWhiteSpace(ev.Title) ? ev.Title.Trim() : (ev.Memo ?? string.Empty).Trim())
                    .Where(x => x.Length > 0).Distinct().ToList();
                string remark = string.Join("; ", remarks);
                if (remark.Length > 250) remark = remark[..250];
                var first = new DateTime(g.First().TimeStamp.Year, g.First().TimeStamp.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                return new
                {
                    g.Key.TopicId,
                    g.Key.Month,
                    RawCount = g.Count(),
                    Quantity = g.Sum(e => e.Quantity),
                    Remark = remark,
                    OccurredAt = new DateTimeOffset(first, AppTimeZone.Zone.GetUtcOffset(first)),
                    ExternalRefId = $"v16:{subPrefix}:{g.Key.TopicId}:{g.Key.Month}"
                };
            })
            .ToList();

        Console.WriteLine($"\n[V20] Migrate Events (V20 SubscriptionId = {state.V20SubscriptionId})");
        Console.WriteLine($"  V16 events {v16Events.Count} -> {monthly.Count} monthly record(s) across {monthly.Select(m => m.TopicId).Distinct().Count()} topic(s)");

        // topicId -> เดือนที่ข้าม (เหตุผล)
        var skippedMonths = new Dictionary<int, List<string>>();
        void SkipMonth(int topicId, string month, string reason)
        {
            if (!skippedMonths.TryGetValue(topicId, out var list)) skippedMonths[topicId] = list = new List<string>();
            list.Add($"{month}: {reason}");
        }

        var toSend = new List<(int TopicId, string Month, V20ApiClient.IngestEventItem Item)>();
        foreach (var m in monthly)
        {
            if (double.IsNaN(m.Quantity) || double.IsInfinity(m.Quantity) || m.Quantity < 0)
            {
                SkipMonth(m.TopicId, m.Month, $"quantity {m.Quantity} is not valid");
                PrintWarning($"  ! topic [{m.TopicId}] {m.Month}: quantity {m.Quantity} is not valid (V20 rejects negative) — skipped.");
                continue;
            }
            toSend.Add((m.TopicId, m.Month, new V20ApiClient.IngestEventItem
            {
                TopicKey = state.TopicKeys[m.TopicId],
                Quantity = (decimal)m.Quantity,
                OccurredAt = m.OccurredAt,
                ExternalRefId = m.ExternalRefId,
                Title = string.Empty,
                Remark = m.Remark
            }));
        }

        using var api = new V20ApiClient(baseUrl, state.V20ApiKey!);
        int created = 0, replayed = 0, rejected = 0;
        for (int offset = 0; offset < toSend.Count; offset += BatchSize)
        {
            var batch = toSend.Skip(offset).Take(BatchSize).ToList();
            var res = await api.IngestEventsAsync(batch.Select(b => b.Item).ToList());
            if (res.Data == null || res.Data.Results.Count == 0)
            {
                foreach (var b in batch) SkipMonth(b.TopicId, b.Month, $"batch failed HTTP {(int)res.StatusCode} {res.Error?.Code}");
                rejected += batch.Count;
                PrintWarning($"  ! batch {offset / BatchSize + 1} ({batch.Count} records): HTTP {(int)res.StatusCode} {res.Error?.Code} - {res.Error?.Message} — skipped.");
                continue;
            }

            foreach (var item in res.Data.Results)
            {
                if (item.Index < 0 || item.Index >= batch.Count) continue;
                var b = batch[item.Index];
                if (item.Status == "created")
                {
                    if (item.Replayed) replayed++; else created++;
                    continue;
                }
                rejected++;
                string why = item.Code == "duplicate_external_ref"
                    ? "already migrated with a different value (V16 changed after previous run) — V20 keeps the old value"
                    : $"{item.Code} - {item.Message}";
                SkipMonth(b.TopicId, b.Month, item.Code ?? "rejected");
                PrintWarning($"  ! topic [{b.TopicId}] {b.Month} ({b.Item.ExternalRefId}): {why} — skipped.");
            }
            Console.WriteLine($"  batch {offset / BatchSize + 1}: {batch.Count} record(s) sent, accepted {res.Data.Accepted}, rejected {res.Data.Rejected}");
        }

        string summary = $"[V20] Events: created {created}, already migrated {replayed}, skipped {skippedMonths.Values.Sum(x => x.Count)} (monthly records {monthly.Count}).";
        if (skippedMonths.Count == 0) PrintSuccess(summary); else PrintWarning(summary);

        bool verified = await VerifyLevelAsync(config, state, outputBasePath, subscriptionId, "events", report =>
        {
            var byTopic = monthly.GroupBy(m => m.TopicId).ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(x => x.Quantity)));
            var v20Topics = report.Corporates.SelectMany(c => c.Companies).SelectMany(c => c.Facilities)
                .SelectMany(f => f.Topics ?? new List<V20AdminApiClient.TopicReportDto>())
                .GroupBy(t => t.TopicKey).ToDictionary(g => g.Key, g => g.First());
            var r = new VerifyResult { Expected = mappedTopics.Count(t => !skippedMonths.ContainsKey(t.Id)) };
            foreach (var t in mappedTopics)
            {
                string key = state.TopicKeys[t.Id];
                byTopic.TryGetValue(t.Id, out var exp);
                if (skippedMonths.TryGetValue(t.Id, out var months))
                {
                    r.Warns.Add($"V16 topic [{t.Id}] {t.Title}: {months.Count} month(s) skipped ({string.Join(", ", months)})");
                    continue;
                }
                if (!v20Topics.TryGetValue(key, out var v)) { r.Fails.Add($"V16 topic [{t.Id}] {t.Title}: {key} not found in V20"); continue; }
                decimal expTotal = (decimal)exp.Total;
                bool countOk = v.EventCount == exp.Count;
                bool totalOk = Math.Abs(v.TotalQuantity - expTotal) <= Math.Max(0.0001m, Math.Abs(expTotal) * 0.000001m);
                if (!countOk || !totalOk)
                {
                    r.Fails.Add($"V16 topic [{t.Id}] {t.Title}: V16 {exp.Count} month(s) / total {expTotal} vs V20 {v.EventCount} event(s) / total {v.TotalQuantity} ({key})");
                    continue;
                }
                r.Ok++;
            }
            return r;
        }, includeTopics: true);
        return verified;
        #endregion
    }

    // [2026-09-23][Claude] ชื่อ Facility ที่จะใช้ใน V20 (ใช้ทั้งขั้น migrate และ verify ให้ตรงกัน)
    // ชื่อซ้ำภายใน Subsidiary เดียวกันของ V16 -> รายการที่ 2 เป็นต้นไปเติม " (V16-FAC-{id})"; ตัดที่ 200 ตัวอักษร
    private static Dictionary<int, string> BuildFacilityTargetTitles(IEnumerable<V16Facility> facilities)
    {
        #region hide [2026-09-23]
        var targetTitles = new Dictionary<int, string>();
        foreach (var group in facilities.OrderBy(f => f.SubsidiaryId).ThenBy(f => f.OrderId).ThenBy(f => f.Id).GroupBy(f => f.SubsidiaryId))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in group)
            {
                string code = $"V16-FAC-{f.Id}";
                string title = (f.Title ?? string.Empty).Trim();
                if (title.Length == 0) title = code;
                if (!used.Add(title))
                {
                    title = $"{title} ({code})";
                    used.Add(title);
                }
                if (title.Length > 200) title = title[..200];
                targetTitles[f.Id] = title;
            }
        }
        return targetTitles;
        #endregion
    }

    // [2026-09-23][Claude] เทียบชื่อ V16 กับ V20 (ตัดช่องว่างหัวท้าย, ตรงตัวอักษร)
    private static bool SameTitle(string? a, string? b) => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.Ordinal);

    private static string TrimTitle200(string? title, string fallback)
    {
        var t = (title ?? string.Empty).Trim();
        if (t.Length == 0) t = fallback;
        return t.Length > 200 ? t[..200] : t;
    }

    // [2026-09-23][Claude] ผลการตรวจ 1 ระดับ
    private sealed class VerifyResult
    {
        public int Expected { get; set; }
        public int Ok { get; set; }
        public List<string> Fails { get; } = new();
        public List<string> Warns { get; } = new();
        public List<string> Infos { get; } = new();
    }

    // [2026-09-23][Claude] Verify 1 ระดับหลัง migrate: ดึง GET api/v2/admin/subscriptions/{id}/migration-report แล้วให้ check ของระดับนั้นเทียบกับ V16
    // แสดงผลบนจอ + บันทึกไฟล์ {OutputBasePath}/migration-state/{v16SubId}-verify-{level}-{yyyyMMdd-HHmmss}.txt
    // FAIL = ข้อมูลไม่ตรง/ขาด (คืน false), WARN = Key ไม่ตรง state, INFO = รายการใน V20 นอกขอบเขตรอบนี้
    private static async Task<bool> VerifyLevelAsync(IConfiguration config, MigrationState state, string outputBasePath, Guid subscriptionId, string level,
        Func<V20AdminApiClient.MigrationReportDto, VerifyResult> check, bool includeTopics = false)
    {
        #region hide [2026-09-23]
        if (state.V20SubscriptionId == null)
        {
            PrintError($"[VERIFY {level}] No V20 SubscriptionId in state.");
            return false;
        }

        var settings = GetV20ApiSettings(config);
        if (settings == null) return false;

        V20AdminApiClient.MigrationReportDto report;
        try
        {
            using var adminClient = new V20AdminApiClient(settings.Value.BaseUrl, settings.Value.AdminEmail, settings.Value.AdminPassword);
            var res = await adminClient.GetMigrationReportAsync(state.V20SubscriptionId.Value, includeTopics: includeTopics);
            if (!res.Success || res.Data == null)
            {
                PrintApiError($"verify {level} (migration-report)", res.StatusCode, res.Error);
                return false;
            }
            report = res.Data;
        }
        catch (HttpRequestException ex)
        {
            PrintError($"[VERIFY {level}] Cannot reach V20 API at {settings.Value.BaseUrl}: {ex.Message}");
            return false;
        }

        var r = check(report);

        var sb = new StringBuilder();
        sb.AppendLine($"[VERIFY {level}] V20 SubscriptionId {report.SubscriptionId} '{report.Title}'  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Checked {r.Expected}, OK {r.Ok}, FAIL {r.Fails.Count}, WARN {r.Warns.Count}, INFO {r.Infos.Count}");
        sb.AppendLine($"  V20 totals: corporates {report.Totals.Corporates}, companies {report.Totals.Companies}, facilities {report.Totals.Facilities}, topics {report.Totals.Topics}, events {report.Totals.Events}");
        foreach (var m in r.Fails) sb.AppendLine($"  FAIL  {m}");
        foreach (var m in r.Warns) sb.AppendLine($"  WARN  {m}");
        foreach (var m in r.Infos) sb.AppendLine($"  INFO  {m}");
        bool pass = r.Fails.Count == 0 && r.Ok == r.Expected;
        sb.AppendLine(pass ? "  RESULT: PASS" : "  RESULT: FAIL");

        string reportPath = Path.Combine(outputBasePath, "migration-state", $"{subscriptionId.ToString().ToLowerInvariant()}-verify-{level}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Console.WriteLine();
        Console.Write(sb.ToString());
        if (pass) PrintSuccess($"[VERIFY {level}] PASS  ({reportPath})");
        else PrintWarning($"[VERIFY {level}] FAIL  ({reportPath})");
        return pass;
        #endregion
    }

    // [2026-09-23][Claude] ถามให้เลือกลำดับรายการ (1-based): Enter = ทั้งหมด, "1,3" / "1-2" / ผสมได้, 0 = ยกเลิก (คืน null)
    // คืน index แบบ 0-based เรียงตามลำดับเดิม ไม่ซ้ำ; พิมพ์ผิดจะถามซ้ำ
    private static List<int>? PromptSelectIndexes(int count, string prompt)
    {
        #region hide [2026-09-23]
        while (true)
        {
            Console.Write(prompt);
            string input = Console.ReadLine()?.Trim() ?? string.Empty;
            if (input.Length == 0) return Enumerable.Range(0, count).ToList();
            if (input == "0") return null;

            var result = new SortedSet<int>();
            string? error = null;
            foreach (var token in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var range = token.Split('-', StringSplitOptions.TrimEntries);
                if (range.Length == 1 && int.TryParse(range[0], out int single) && single >= 1 && single <= count)
                {
                    result.Add(single - 1);
                }
                else if (range.Length == 2 && int.TryParse(range[0], out int from) && int.TryParse(range[1], out int to) &&
                         from >= 1 && to <= count && from <= to)
                {
                    for (int i = from; i <= to; i++) result.Add(i - 1);
                }
                else
                {
                    error = token;
                    break;
                }
            }

            if (error == null && result.Count > 0) return result.ToList();
            PrintError($"Invalid selection '{error ?? input}'. Use numbers 1-{count}, e.g. 1,3 or 1-2 (Enter = all, 0 = cancel).");
        }
        #endregion
    }

    // [2026-09-23][Claude] ต่ออายุ API Key อัตโนมัติ: ไม่มี Key / ใกล้หมดอายุ (< 1 วัน) / V20 ปฏิเสธ (401) -> ออก Key ใหม่ด้วยสิทธิ์ Admin แล้วบันทึก state
    // Key เดิมไม่ถูกยกเลิก (V20 ยังไม่มี API ยกเลิก Key) แต่จะหมดอายุเองตาม ExpiresAt
    // ใช้ก่อนทุกขั้นที่ต้องยิง API ด้วย Key ของ Subscription
    private static async Task<bool> EnsureV20ApiKeyAsync(V20AdminApiClient adminClient, string baseUrl, MigrationState state, string outputBasePath, string keyName, int? expireDays)
    {
        #region hide [2026-09-23]
        if (!state.V20SubscriptionId.HasValue)
        {
            PrintError("State has no V20 SubscriptionId. Run 'Create Subscription in V20' first.");
            return false;
        }

        string? renewReason = null;
        if (string.IsNullOrWhiteSpace(state.V20ApiKey))
        {
            renewReason = "no API key in state";
        }
        else if (state.V20ApiKeyExpiresAt.HasValue && state.V20ApiKeyExpiresAt.Value <= DateTime.UtcNow.AddDays(1))
        {
            renewReason = $"API key {state.V20ApiKeyPrefix} expires {state.V20ApiKeyExpiresAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
        else
        {
            var probe = await V20AdminApiClient.ProbeApiKeyAsync(baseUrl, state.V20ApiKey!);
            if (probe == System.Net.HttpStatusCode.Unauthorized)
            {
                renewReason = $"API key {state.V20ApiKeyPrefix} was rejected by V20 (expired/revoked)";
            }
            else if ((int)probe >= 400)
            {
                PrintWarning($"API key check returned HTTP {(int)probe}; key kept as is.");
            }
        }

        if (renewReason == null) return true;

        PrintWarning($"{renewReason} -> issuing a new API key...");
        var keyRes = await adminClient.CreateApiKeyAsync(state.V20SubscriptionId.Value, keyName, expireDays);
        if (!keyRes.Success || keyRes.Data?.ApiKey == null || string.IsNullOrWhiteSpace(keyRes.Data.ApiKey.RawKey))
        {
            PrintApiError("issue API key", keyRes.StatusCode, keyRes.Error);
            return false;
        }

        state.V20ApiKeyPrefix = keyRes.Data.ApiKey.KeyPrefix;
        state.V20ApiKey = keyRes.Data.ApiKey.RawKey;
        state.V20ApiKeyExpiresAt = keyRes.Data.ApiKey.ExpiresAt;
        MigrationStateStore.Save(outputBasePath, state);
        PrintSuccess($"New API key {state.V20ApiKeyPrefix} issued (expires {state.V20ApiKeyExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never"}).");
        return true;
        #endregion
    }

    // [2026-09-23][Claude] อ่านค่า V20Api:* จาก config; ยืนยันตัวด้วยบัญชี Administrator ของ V20
    // AdminEmail ถ้าไม่ได้ตั้งจะถามจาก console, AdminPassword ถามจาก console เสมอ (ไม่แสดงบนจอ) ยกเว้นส่งทาง CLI --V20Api:AdminPassword
    private static (string BaseUrl, string AdminEmail, string AdminPassword, string ApiKeyName, int? ApiKeyExpireDays)? GetV20ApiSettings(IConfiguration config)
    {
        #region hide [2026-09-23]
        string? baseUrl = config["V20Api:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            PrintError("Missing 'V20Api:BaseUrl' in appsettings.json.");
            return null;
        }

        string? adminEmail = config["V20Api:AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            Console.Write("V20 Administrator email: ");
            adminEmail = Console.ReadLine()?.Trim();
        }

        string? adminPassword = config["V20Api:AdminPassword"];
        if (string.IsNullOrEmpty(adminPassword))
        {
            Console.Write($"Password for {adminEmail}: ");
            adminPassword = ReadPasswordFromConsole();
        }

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrEmpty(adminPassword))
        {
            PrintError("V20 Administrator email and password are required.");
            return null;
        }

        string apiKeyName = string.IsNullOrWhiteSpace(config["V20Api:ApiKeyName"]) ? "migration-v16" : config["V20Api:ApiKeyName"]!;
        int? expireDays = int.TryParse(config["V20Api:ApiKeyExpireDays"], out var d) && d > 0 ? d : null;

        return (baseUrl.Trim(), adminEmail.Trim(), adminPassword, apiKeyName, expireDays);
        #endregion
    }

    // [2026-09-23][Claude] อ่านรหัสผ่านจาก console โดยไม่แสดงตัวอักษร (รองรับ Backspace)
    private static string ReadPasswordFromConsole()
    {
        #region hide [2026-09-23]
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) { sb.Length--; Console.Write("\b \b"); }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                sb.Append(key.KeyChar);
                Console.Write('*');
            }
        }
        Console.WriteLine();
        return sb.ToString();
        #endregion
    }

    // [2026-09-23][Claude] Map V16 eSubscribeLevel -> V20 eSubscriptionPlan (อ้างอิง V16: TU.ITAP.ACAMP.WebApp-006/TU.ACAMP.Domain/Models/V1/Identity/TbSubscribe.cs)
    // V16: Free=1 (V16 ถือว่าเป็น Pro จำกัด 3 เดือน), Basic=2, Pro=3, Premium=4
    // V20: Basic=0, Pro=1, Premium=2, Enterprise=3  — ตัวเลขไม่ตรงกัน ห้ามส่งค่าตรง
    private static int MapV16LevelToV20Plan(int v16Level)
    {
        #region hide [2026-09-23]
        return v16Level switch
        {
            1 => 1, // Free    -> Pro (วันหมดอายุคงตาม V16)
            2 => 0, // Basic   -> Basic
            3 => 1, // Pro     -> Pro
            4 => 2, // Premium -> Premium
            _ => 1  // ไม่รู้จัก -> Pro
        };
        #endregion
    }

    private static string MapV16LevelText(int v16Level) => v16Level switch
    {
        1 => "Free", 2 => "Basic", 3 => "Pro", 4 => "Premium", _ => $"Unknown({v16Level})"
    };

    private static string MapV16SubscribeTypeText(int v16Type) => v16Type switch
    {
        1 => "CAMP", 2 => "ACAMP", 3 => "IDA67-ACAMP", 4 => "IDA68-ACAMP", 5 => "IDA69-ACAMP", _ => $"Unknown({v16Type})"
    };

    private static string MapV20PlanText(int v20Plan) => v20Plan switch
    {
        0 => "Basic", 1 => "Pro", 2 => "Premium", 3 => "Enterprise", _ => $"Unknown({v20Plan})"
    };

    // [2026-09-23][Claude] แสดง error จาก V20 API
    private static void PrintApiError(string action, System.Net.HttpStatusCode statusCode, V20AdminApiClient.ApiErrorDto? error)
    {
        #region hide [2026-09-23]
        PrintError($"V20 API failed to {action}: HTTP {(int)statusCode} {error?.Code} - {error?.Message}");
        #endregion
    }

    // [2026-09-23][Claude] อ่านค่า argument แบบ "--name value"
    private static string? ExtractArgValue(string[] args, string name)
    {
        #region hide [2026-09-23]
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].Trim();
            }
        }
        return null;
        #endregion
    }

    // [2026-09-22][Antigravity] Display subscriptions menu numbered 1 to n with 0 as exit
    private static void PrintSubscriptionMenu(List<V16Subscription> subscriptions)
    {
        #region hide [2026-09-22]
        Console.WriteLine("----------------------------------------------------------------");
        Console.WriteLine(" SUBSCRIPTION LIST (V16 SOURCE):");
        Console.WriteLine("----------------------------------------------------------------");
        for (int i = 0; i < subscriptions.Count; i++)
        {
            var sub = subscriptions[i];
            string reg = string.IsNullOrWhiteSpace(sub.RegistrationNo) ? "N/A" : sub.RegistrationNo;
            Console.WriteLine($"   [{i + 1,2}] {sub.Title} (RegNo: {reg})");
        }
        Console.WriteLine("   [ 0] Back to main menu"); // [2026-09-23][Claude] เมนูนี้อยู่หลังเมนูหลักแล้ว
        Console.WriteLine("----------------------------------------------------------------");
        #endregion
    }

    // [2026-09-22][Antigravity] Fetch all available subscriptions from V16 database ordered by active date
    private static async Task<List<V16Subscription>> FetchAllSubscriptionsAsync(V16SourceDbContext dbContext)
    {
        #region hide [2026-09-22]
        return await dbContext.Subscriptions
            .AsNoTracking()
            .OrderByDescending(s => s.ActivatedDate)
            .ToListAsync();
        #endregion
    }

    // [2026-09-22][Antigravity] Execute Phase 1 workflow for a specified Subscription ID
    private static async Task ExecuteMigrationPhase1Async(V16SourceDbContext dbContext, IConfiguration config, Guid subscriptionId)
    {
        #region hide [2026-09-22]
        var subscription = await FetchSubscriptionWithHierarchyAsync(dbContext, subscriptionId);
        if (subscription == null)
        {
            PrintError($"Subscription with ID '{subscriptionId}' was not found in V16 database.");
            return;
        }

        DisplayHierarchySummary(subscription);

        string outputBasePath = GetOutputBasePath(config);
        var (isCreated, rootPath, orgFolders, subFolders, facFolders, dirError) = CreateHierarchyDirectoryStructure(outputBasePath, subscription);
        if (!isCreated)
        {
            PrintError($"Failed to create folder structure: {dirError}");
            return;
        }

        PrintSuccess($"Real folder structure created successfully for Subscription {subscription.Id}:\n" +
                     $"Root Path: {rootPath}\n" +
                     $"Created Folders: {orgFolders} Organization(s), {subFolders} Subsidiary(ies), {facFolders} Factory(ies)");

        // [2026-09-23][Claude] Export organization structure to organizations.xlsx (root) / companies.xlsx (organization folder) / facilities.xlsx (subsidiary folder)
        await ExportOrganizationsToExcelAsync(subscription, rootPath);

        // [2026-09-22][Antigravity] Export emission units to Excel directly into subscription root folder
        await ExportEmissionUnitsToExcelAsync(dbContext, subscription, rootPath);

        // [2026-09-22][Antigravity] Export custom emission groups to Excel directly into subscription root folder
        await ExportEmissionGroupsToExcelAsync(dbContext, subscription, rootPath);

        // [2026-09-23][Claude] Export custom emission sources (custom EF) to Excel directly into subscription root folder
        await ExportEmissionSourcesToExcelAsync(dbContext, subscription, rootPath);

        // [2026-09-22][Antigravity] Export facility topics to Excel directly into each facility folder
        await ExportAllFacilityTopicsToExcelAsync(dbContext, subscription, rootPath);

        // [2026-09-22][Antigravity] Export facility activity events to Excel directly into each facility's events/ folder
        await ExportAllFacilityEventsToExcelAsync(dbContext, subscription, rootPath);

        OpenFolderInExplorer(rootPath);
        #endregion
    }

    // [2026-09-23][Claude] Export โครงสร้างองค์กรของ V16 ตามโฟลเดอร์ระดับแม่ (โครงเดียวกับ CreateHierarchyDirectoryStructure):
    //   {root}/organizations.xlsx                              = Organization ทั้งหมดของ Subscription (-> Corporate)
    //   {root}/{orgId}_{org}/companies.xlsx                    = Subsidiary ของ Organization นั้น (-> Company)
    //   {root}/{orgId}_{org}/{subId}_{sub}/facilities.xlsx     = Facility ของ Subsidiary นั้น
    // Code ใช้รูปแบบเดียวกับขั้น migrate (V16-ORG/SUB/FAC-{id}) และอ้าง Code ของระดับแม่
    // facilities.xlsx มีคอลัมน์ "V20 Title" = ชื่อที่ขั้น migrate จะใช้จริง (ชื่อซ้ำใน Subsidiary เดียวกันเติม " (V16-FAC-{id})")
    private static Task<string?> ExportOrganizationsToExcelAsync(V16Subscription subscription, string targetDir)
    {
        #region hide [2026-09-23]
        try
        {
            Console.WriteLine($"\nExporting organization structure for Subscription: {subscription.Title}...");

            var organizations = subscription.Organizations.OrderBy(o => o.OrderId).ThenBy(o => o.Id).ToList();
            var allFacilities = organizations.SelectMany(o => o.Subsidiaries).SelectMany(s => s.Facilities).ToList();
            var facilityTargetTitles = BuildFacilityTargetTitles(allFacilities);
            var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
            int companyFiles = 0, facilityFiles = 0;

            // 1 ไฟล์ = 1 ชีต: เขียนหัวตาราง + แถวข้อมูล แล้วบันทึก
            void SaveSheet(string dir, string fileName, string sheetName, string[] headers, IEnumerable<object[]> rows)
            {
                Directory.CreateDirectory(dir);
                using var workbook = new XLWorkbook();
                var ws = workbook.Worksheets.Add(sheetName);
                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cell(1, c + 1);
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = headerBg;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
                int row = 2;
                foreach (var values in rows)
                {
                    for (int c = 0; c < values.Length; c++)
                    {
                        ws.Cell(row, c + 1).Value = XLCellValue.FromObject(values[c]);
                    }
                    row++;
                }
                ws.Columns().AdjustToContents();
                ws.Row(1).Height = 25;
                ws.SheetView.FreezeRows(1);
                workbook.SaveAs(Path.Combine(dir, fileName));
            }

            // root: organizations.xlsx
            SaveSheet(targetDir, "organizations.xlsx", "Corporates",
                new[] { "#", "V16 Id", "Code", "Title", "Order", "Subsidiaries", "Facilities" },
                organizations.Select((o, i) => new object[]
                {
                    i + 1, o.Id, $"V16-ORG-{o.Id}", (o.Title ?? string.Empty).Trim(), o.OrderId,
                    o.Subsidiaries.Count, o.Subsidiaries.Sum(s => s.Facilities.Count)
                }));

            foreach (var org in organizations)
            {
                string orgDir = Path.Combine(targetDir, $"{org.Id}_{SanitizeFolderName(org.Title)}");
                var subsidiaries = org.Subsidiaries.OrderBy(s => s.OrderId).ThenBy(s => s.Id).ToList();

                // {org}/companies.xlsx
                SaveSheet(orgDir, "companies.xlsx", "Companies",
                    new[] { "#", "V16 Id", "Code", "Title", "Corporate Code", "Corporate Title", "Equity Share (%)", "Order", "Facilities" },
                    subsidiaries.Select((s, i) => new object[]
                    {
                        i + 1, s.Id, $"V16-SUB-{s.Id}", (s.Title ?? string.Empty).Trim(), $"V16-ORG-{org.Id}", (org.Title ?? string.Empty).Trim(),
                        (double)s.EquityShareApproach, s.OrderId, s.Facilities.Count
                    }));
                companyFiles++;

                foreach (var sub in subsidiaries)
                {
                    string subDir = Path.Combine(orgDir, $"{sub.Id}_{SanitizeFolderName(sub.Title)}");
                    var facilities = sub.Facilities.OrderBy(f => f.OrderId).ThenBy(f => f.Id).ToList();

                    // {org}/{sub}/facilities.xlsx
                    SaveSheet(subDir, "facilities.xlsx", "Facilities",
                        new[] { "#", "V16 Id", "Code", "Title", "V20 Title", "Company Code", "Company Title", "Corporate Code", "Order" },
                        facilities.Select((f, i) => new object[]
                        {
                            i + 1, f.Id, $"V16-FAC-{f.Id}", (f.Title ?? string.Empty).Trim(),
                            facilityTargetTitles.TryGetValue(f.Id, out var target) ? target : string.Empty,
                            $"V16-SUB-{sub.Id}", (sub.Title ?? string.Empty).Trim(), $"V16-ORG-{org.Id}", f.OrderId
                        }));
                    facilityFiles++;
                }
            }

            PrintSuccess($"[EXCEL EXPORTED] organizations.xlsx ({organizations.Count} organization(s)) at root, " +
                         $"{companyFiles} companies.xlsx in organization folders, {facilityFiles} facilities.xlsx in subsidiary folders ({allFacilities.Count} facility(ies)).");
            int renamed = allFacilities.Count(f => facilityTargetTitles.TryGetValue(f.Id, out var t) && !string.Equals(t, (f.Title ?? string.Empty).Trim(), StringComparison.Ordinal));
            if (renamed > 0)
            {
                PrintWarning($"{renamed} facility title(s) will be changed in V20 (duplicate/empty/too long) — see column 'V20 Title'.");
            }
            return Task.FromResult<string?>(targetDir);
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export organization structure to Excel: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Export V16 emission units to Excel formatted for V20 import (custom units or default fallback)
    private static async Task<string?> ExportEmissionUnitsToExcelAsync(V16SourceDbContext dbContext, V16Subscription subscription, string targetDir)
    {
        #region hide [2026-09-22]
        try
        {
            Console.WriteLine($"\nExporting emission units for Subscription: {subscription.Title}...");

            // 1. First fetch custom units specific to this subscription (SubscribeId == subscription.Id)
            var units = await dbContext.EmissionUnits
                .AsNoTracking()
                .Where(u => u.SubscribeId == subscription.Id)
                .OrderBy(u => u.OrderId)
                .ThenBy(u => u.Id)
                .ToListAsync();

            bool isFallback = false;

            // 2. If no custom units exist, fallback to system default units (SubscribeId == null)
            if (units.Count == 0)
            {
                units = await dbContext.EmissionUnits
                    .AsNoTracking()
                    .Where(u => u.SubscribeId == null)
                    .OrderBy(u => u.OrderId)
                    .ThenBy(u => u.Id)
                    .ToListAsync();
                isFallback = true;
            }

            if (units.Count == 0)
            {
                PrintWarning($"No emission units found in V16 database for Subscription {subscription.Id} (neither custom nor default).");
                return null;
            }

            // 3. Create Excel workbook using ClosedXML
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Emission Units");

            // Header row (#, Title*, Remark)
            string[] headers = new[] { "#", "Title*", "Remark" };
            var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
            var headerFg = XLColor.White;

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = worksheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = headerFg;
                cell.Style.Fill.BackgroundColor = headerBg;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            // Data rows
            for (int r = 0; r < units.Count; r++)
            {
                var unit = units[r];
                worksheet.Cell(r + 2, 1).Value = r + 1;
                worksheet.Cell(r + 2, 2).Value = unit.Title ?? string.Empty;
                worksheet.Cell(r + 2, 3).Value = unit.Memo ?? string.Empty;
            }

            worksheet.Columns().AdjustToContents();
            worksheet.Row(1).Height = 25;
            worksheet.SheetView.FreezeRows(1);

            // [2026-09-22][Antigravity] Set simplified filename "emission_units.xlsx" because it resides within the subscription folder
            string fileName = "emission_units.xlsx";

            string filePath = Path.Combine(targetDir, fileName);
            workbook.SaveAs(filePath);

            string unitTypeLabel = isFallback ? "Default System Units (Fallback)" : "Custom Units";
            PrintSuccess($"[EXCEL EXPORTED] {units.Count} {unitTypeLabel} saved to:\n{filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export emission units to Excel: {ex.Message}");
            return null;
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Export V16 custom emission groups to Excel formatted for V20 import (Title, Scope)
    private static async Task<string?> ExportEmissionGroupsToExcelAsync(V16SourceDbContext dbContext, V16Subscription subscription, string targetDir)
    {
        #region hide [2026-09-22]
        try
        {
            Console.WriteLine($"\nExporting custom emission groups for Subscription: {subscription.Title}...");

            string subIdStr = subscription.Id.ToString().ToLower();

            // Fetch custom emission groups specific to this subscription
            var groups = await dbContext.EmissionGroups
                .AsNoTracking()
                .Where(g => (!g.IsReadOnly && g.SubscribeId == subscription.Id) ||
                            (g.SubscribeId == subscription.Id) ||
                            (g.JsonDataString != null && g.JsonDataString.ToLower().Contains(subIdStr)))
                .OrderBy(g => g.ScopeId)
                .ThenBy(g => g.OrderId)
                .ThenBy(g => g.Id)
                .ToListAsync();

            // Create Excel workbook using ClosedXML
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Emission Groups");

            // Header row (#, Title*, Scope*)
            string[] headers = new[] { "#", "Title*", "Scope*" };
            var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
            var headerFg = XLColor.White;

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = worksheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = headerFg;
                cell.Style.Fill.BackgroundColor = headerBg;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            // Data rows
            for (int r = 0; r < groups.Count; r++)
            {
                var group = groups[r];
                worksheet.Cell(r + 2, 1).Value = r + 1;
                worksheet.Cell(r + 2, 2).Value = group.Title ?? string.Empty;
                worksheet.Cell(r + 2, 3).Value = MapScopeIdToScopeText(group.ScopeId);
            }

            worksheet.Columns().AdjustToContents();
            worksheet.Row(1).Height = 25;
            worksheet.SheetView.FreezeRows(1);

            // [2026-09-22][Antigravity] Export filename is simplified to "emission_groups.xlsx" at the same level as emission_units.xlsx
            string fileName = "emission_groups.xlsx";
            string filePath = Path.Combine(targetDir, fileName);
            workbook.SaveAs(filePath);

            PrintSuccess($"[EXCEL EXPORTED] {groups.Count} Custom Emission Group(s) saved to:\n{filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export emission groups to Excel: {ex.Message}");
            return null;
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Map V16 ScopeId (1, 2, 3, 4) to V20 Scope text
    private static string MapScopeIdToScopeText(int scopeId)
    {
        #region hide [2026-09-22]
        return scopeId switch
        {
            1 => "Scope 1",
            2 => "Scope 2",
            3 => "Scope 3",
            4 => "Separate Report",
            _ => "Scope 1"
        };
        #endregion
    }

    // [2026-09-23][Claude] Export V16 custom emission sources (custom EF of this subscription) to Excel formatted for V20 Emission Source import
    // V20 template (EmissionSourceExcelService.GenerateTemplateExcelAsync): #, Title*, Emission, Scope, Unit*, CO2, Fossil CH4, CH4, N2O, SF6, NF3, HFCs, PFCs, Reference
    private static async Task<string?> ExportEmissionSourcesToExcelAsync(V16SourceDbContext dbContext, V16Subscription subscription, string targetDir)
    {
        #region hide [2026-09-23]
        try
        {
            Console.WriteLine($"\nExporting custom emission sources for Subscription: {subscription.Title}...");

            // Custom source = tb_emission_source.SubscribeId == this subscription (same filter as script_migration_16_to_20/migrarion_version_002.sql)
            var sources = await dbContext.EmissionSources
                .AsNoTracking()
                .Include(s => s.Group)
                .Include(s => s.Unit)
                .Where(s => s.SubscribeId == subscription.Id)
                .OrderBy(s => s.Group != null ? s.Group.ScopeId : 0)
                .ThenBy(s => s.GroupId)
                .ThenBy(s => s.Id)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Import Emission Sources");

            string[] headers = new[]
            {
                "#", "Title*", "Emission", "Scope", "Unit*",
                "CO2", "Fossil CH4", "CH4", "N2O", "SF6", "NF3", "HFCs", "PFCs",
                "Reference"
            };
            var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
            var headerFg = XLColor.White;

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = worksheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = headerFg;
                cell.Style.Fill.BackgroundColor = headerBg;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            // Duplicate titles are rejected by V20 parser ("Duplicated title within this file.") -> warn only, keep raw data
            var duplicatedTitles = sources
                .GroupBy(s => (s.Title ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            for (int r = 0; r < sources.Count; r++)
            {
                var src = sources[r];
                int row = r + 2;
                int col = 1;
                worksheet.Cell(row, col++).Value = r + 1;
                worksheet.Cell(row, col++).Value = (src.Title ?? string.Empty).Trim();
                worksheet.Cell(row, col++).Value = src.Group?.Title?.Trim() ?? string.Empty;
                worksheet.Cell(row, col++).Value = src.Group != null ? MapScopeIdToScopeText(src.Group.ScopeId) : string.Empty;
                worksheet.Cell(row, col++).Value = src.Unit?.Title?.Trim() ?? string.Empty;
                worksheet.Cell(row, col++).Value = (double)src.CO2;
                worksheet.Cell(row, col++).Value = (double)src.FossilCH4;
                worksheet.Cell(row, col++).Value = (double)src.CH4;
                worksheet.Cell(row, col++).Value = (double)src.N2O;
                worksheet.Cell(row, col++).Value = (double)src.SF6;
                worksheet.Cell(row, col++).Value = (double)src.NF3;
                worksheet.Cell(row, col++).Value = (double)src.HFCs;
                worksheet.Cell(row, col++).Value = (double)src.PFCs;
                worksheet.Cell(row, col++).Value = src.Ref ?? string.Empty;
            }

            worksheet.Columns().AdjustToContents();
            worksheet.Row(1).Height = 25;
            worksheet.SheetView.FreezeRows(1);

            string fileName = "emission_sources.xlsx";
            string filePath = Path.Combine(targetDir, fileName);
            workbook.SaveAs(filePath);

            PrintSuccess($"[EXCEL EXPORTED] {sources.Count} Custom Emission Source(s) saved to:\n{filePath}");
            if (duplicatedTitles.Count > 0)
            {
                PrintWarning($"Duplicated source title(s) will be rejected by V20 import: {string.Join(", ", duplicatedTitles)}");
            }
            return filePath;
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export emission sources to Excel: {ex.Message}");
            return null;
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Export facility topics to Excel directly into each facility folder
    private static async Task ExportAllFacilityTopicsToExcelAsync(V16SourceDbContext dbContext, V16Subscription subscription, string rootPath)
    {
        #region hide [2026-09-22]
        try
        {
            Console.WriteLine($"\nExporting facility topics for Subscription: {subscription.Title}...");

            int exportedFacilities = 0;
            int totalTopicsExported = 0;

            foreach (var org in subscription.Organizations)
            {
                string orgFolderName = $"{org.Id}_{SanitizeFolderName(org.Title)}";
                string orgDir = Path.Combine(rootPath, orgFolderName);

                foreach (var sub in org.Subsidiaries)
                {
                    string subFolderName = $"{sub.Id}_{SanitizeFolderName(sub.Title)}";
                    string subDir = Path.Combine(orgDir, subFolderName);

                    foreach (var fac in sub.Facilities)
                    {
                        string facFolderName = $"{fac.Id}_{SanitizeFolderName(fac.Title)}";
                        string facDir = Path.Combine(subDir, facFolderName);

                        // Fetch topics for this facility
                        var topics = await dbContext.ActivityTopics
                            .AsNoTracking()
                            .Include(t => t.ActivityGroup)
                                .ThenInclude(ag => ag!.Scope)
                            .Include(t => t.Source)
                                .ThenInclude(s => s!.Unit)
                            .Include(t => t.Source)
                                .ThenInclude(s => s!.Group)
                            .Where(t => t.FacilityId == fac.Id)
                            .OrderBy(t => t.OrderId)
                            .ThenBy(t => t.Id)
                            .ToListAsync();

                        await ExportSingleFacilityTopicsAsync(topics, fac, facDir);
                        if (topics.Count > 0)
                        {
                            exportedFacilities++;
                            totalTopicsExported += topics.Count;
                        }
                    }
                }
            }

            if (exportedFacilities > 0)
            {
                PrintSuccess($"[EXCEL EXPORTED] {totalTopicsExported} Topics exported across {exportedFacilities} Facility(ies) to 'topics.xlsx'.");
            }
            else
            {
                PrintWarning($"No topics found for any facilities under Subscription {subscription.Id}.");
            }
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export facility topics: {ex.Message}");
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Export a single facility's topics to topics.xlsx using ClosedXML
    private static async Task<string?> ExportSingleFacilityTopicsAsync(List<V16ActivityTopic> topics, V16Facility facility, string targetDir)
    {
        #region hide [2026-09-22]
        try
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Topics");

            // Header row
            string[] headers = new[]
            {
                "#",
                "Title / Topic Name*",
                "Source*",
                "Emission",
                "Activity",
                "Scope",
                "Unit",
                "EF",
                "Share (%)",
                "Remark"
            };

            var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
            var headerFg = XLColor.White;

            for (int c = 0; c < headers.Length; c++)
            {
                var cell = worksheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = headerFg;
                cell.Style.Fill.BackgroundColor = headerBg;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            // Data rows
            for (int r = 0; r < topics.Count; r++)
            {
                var topic = topics[r];
                var src = topic.Source;
                var actGrp = topic.ActivityGroup;
                var emGrp = src?.Group;
                var unit = src?.Unit;

                string scopeText = actGrp?.Scope != null
                    ? MapScopeIdToScopeText(actGrp.Scope.Id)
                    : (emGrp != null ? MapScopeIdToScopeText(emGrp.ScopeId) : "Scope 1");

                decimal ef = src?.CalculateTotalEf() ?? 0m;

                worksheet.Cell(r + 2, 1).Value = r + 1;
                worksheet.Cell(r + 2, 2).Value = topic.Title ?? string.Empty;
                worksheet.Cell(r + 2, 3).Value = src?.Title ?? string.Empty;
                worksheet.Cell(r + 2, 4).Value = emGrp?.Title ?? string.Empty;
                worksheet.Cell(r + 2, 5).Value = actGrp?.Title ?? string.Empty;
                worksheet.Cell(r + 2, 6).Value = scopeText;
                worksheet.Cell(r + 2, 7).Value = unit?.Title ?? string.Empty;
                worksheet.Cell(r + 2, 8).Value = (double)ef;
                worksheet.Cell(r + 2, 8).Style.NumberFormat.Format = "0.0000";
                worksheet.Cell(r + 2, 9).Value = (double)topic.EmissionSharePercent;
                worksheet.Cell(r + 2, 9).Style.NumberFormat.Format = "0.0";
                worksheet.Cell(r + 2, 10).Value = topic.Memo ?? string.Empty;
            }

            worksheet.Columns().AdjustToContents();
            worksheet.Row(1).Height = 25;
            worksheet.SheetView.FreezeRows(1);

            string fileName = "topics.xlsx";
            string filePath = Path.Combine(targetDir, fileName);
            workbook.SaveAs(filePath);

            Console.WriteLine($"  -> Facility [{facility.Id} - {facility.Title}]: {topics.Count} topic(s) saved to {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export topics for Facility {facility.Id}: {ex.Message}");
            return null;
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Export V16 activity events to individual Excel files per Topic under each Facility's events/ directory
    private static async Task ExportAllFacilityEventsToExcelAsync(V16SourceDbContext dbContext, V16Subscription subscription, string subscriptionRootPath)
    {
        #region hide [2026-09-22]
        try
        {
            Console.WriteLine($"\nExporting Activity Events for Facilities under Subscription: {subscription.Title}...");

            int totalEventsExported = 0;
            int totalFilesExported = 0;
            int facilitiesWithEvents = 0;
            int truncatedFileNames = 0; // [2026-09-23][Claude] count of event files whose title had to be truncated

            foreach (var org in subscription.Organizations)
            {
                string orgFolderName = $"{org.Id}_{SanitizeFolderName(org.Title)}";
                string orgDir = Path.Combine(subscriptionRootPath, orgFolderName);

                foreach (var sub in org.Subsidiaries)
                {
                    string subFolderName = $"{sub.Id}_{SanitizeFolderName(sub.Title)}";
                    string subDir = Path.Combine(orgDir, subFolderName);

                    foreach (var fac in sub.Facilities)
                    {
                        string facFolderName = $"{fac.Id}_{SanitizeFolderName(fac.Title)}";
                        string facDir = Path.Combine(subDir, facFolderName);

                        // Fetch topics with events for this facility
                        var topics = await dbContext.ActivityTopics
                            .AsNoTracking()
                            .Include(t => t.Source)
                                .ThenInclude(s => s!.Unit)
                            .Include(t => t.Events)
                            .Where(t => t.FacilityId == fac.Id)
                            .OrderBy(t => t.OrderId)
                            .ThenBy(t => t.Id)
                            .ToListAsync();

                        var topicsWithEvents = topics.Where(t => t.Events.Count > 0).ToList();
                        if (topicsWithEvents.Count == 0) continue;

                        string eventsDir = Path.Combine(facDir, "events");
                        Directory.CreateDirectory(eventsDir);

                        facilitiesWithEvents++;
                        // [2026-09-23][Claude] Iterate in the same order as topics.xlsx (OrderId, Id) and prefix file name with
                        // the topic sequence number (= column "#" in topics.xlsx) instead of V16 topic Id.
                        // Sequence is counted over ALL topics of the facility, so topics without events leave a gap in numbering.
                        for (int i = 0; i < topics.Count; i++)
                        {
                            var topic = topics[i];
                            if (topic.Events.Count == 0) continue;

                            int topicSeq = i + 1;

                            // [2026-09-23][Claude] Use full topic title; truncate only when full path exceeds Excel limit (218 chars)
                            string prefix = $"{topicSeq}_";
                            const string ext = ".xlsx";
                            string safeTitle = SanitizeFolderName(topic.Title, int.MaxValue);
                            int fixedLength = Path.Combine(eventsDir, prefix + ext).Length;
                            if (fixedLength + safeTitle.Length > ExcelMaxPathLength)
                            {
                                int allowed = Math.Max(1, ExcelMaxPathLength - fixedLength);
                                safeTitle = SanitizeFolderName(topic.Title, allowed);
                                truncatedFileNames++;
                                PrintWarning($"Path exceeds {ExcelMaxPathLength} chars, title truncated to {safeTitle.Length} chars: [{fac.Id}] {topicSeq}_{topic.Title}");
                            }
                            string fileName = $"{prefix}{safeTitle}{ext}"; // sequence is unique per facility -> no collision

                            string filePath = Path.Combine(eventsDir, fileName);
                            await ExportSingleTopicEventsAsync(topic, filePath);

                            totalFilesExported++;
                            totalEventsExported += topic.Events.Count;
                        }

                        Console.WriteLine($"  -> Facility [{fac.Id} - {fac.Title}]: {topicsWithEvents.Count} topic event file(s) saved to events/");
                    }
                }
            }

            if (totalEventsExported > 0)
            {
                PrintSuccess($"[EXCEL EXPORTED] {totalEventsExported} Activity Events exported across {totalFilesExported} Topic file(s) in {facilitiesWithEvents} Facility(ies).");
                if (truncatedFileNames > 0)
                {
                    PrintWarning($"{truncatedFileNames} event file name(s) truncated to keep full path <= {ExcelMaxPathLength} chars.");
                }
            }
            else
            {
                PrintWarning($"No activity events found for any facilities under Subscription {subscription.Id}.");
            }
        }
        catch (Exception ex)
        {
            PrintError($"Failed to export facility activity events: {ex.Message}");
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Export a single topic's events to Excel formatted for V20 import (Remark, Time (yyyy-MM)*, Quantity*)
    private static Task ExportSingleTopicEventsAsync(V16ActivityTopic topic, string filePath)
    {
        #region hide [2026-09-22]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Activity Events");

        string unitTitle = topic.Source?.Unit?.Title?.Trim() ?? string.Empty;
        string qtyHeader = string.IsNullOrWhiteSpace(unitTitle) ? "Quantity*" : $"Quantity* ({unitTitle})";
        string[] headers = new[] { "Remark", "Time (yyyy-MM)*", qtyHeader };

        var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
        var headerFg = XLColor.White;

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = worksheet.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = headerFg;
            cell.Style.Fill.BackgroundColor = headerBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        // [2026-09-22][Antigravity] Group events by reporting month (yyyy-MM) to ensure exactly 1 record per month for V20 ISO compliance
        var monthlyGroups = topic.Events
            .GroupBy(e => e.TimeStamp.ToString("yyyy-MM"))
            .OrderBy(g => g.Key)
            .ToList();

        for (int r = 0; r < monthlyGroups.Count; r++)
        {
            var group = monthlyGroups[r];
            int row = r + 2;

            // Combine remarks if multiple entries exist in the same month
            var remarks = group
                .Select(ev => !string.IsNullOrWhiteSpace(ev.Title) ? ev.Title.Trim() : (ev.Memo?.Trim() ?? string.Empty))
                .Where(rem => !string.IsNullOrWhiteSpace(rem))
                .Distinct()
                .ToList();
            string combinedRemark = remarks.Count > 0 ? string.Join("; ", remarks) : string.Empty;

            worksheet.Cell(row, 1).Value = combinedRemark;
            worksheet.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            // Time (yyyy-MM)
            var timeCell = worksheet.Cell(row, 2);
            timeCell.Style.NumberFormat.Format = "@";
            timeCell.Value = group.Key;
            timeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Quantity (sum of month)
            var qtyCell = worksheet.Cell(row, 3);
            qtyCell.Value = (decimal)group.Sum(e => e.Quantity);
            qtyCell.Style.NumberFormat.Format = "#,##0.00";
            qtyCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        worksheet.Columns().AdjustToContents();
        worksheet.Row(1).Height = 25;
        worksheet.SheetView.FreezeRows(1);

        workbook.SaveAs(filePath);
        return Task.CompletedTask;
        #endregion
    }

    // [2026-09-22][Antigravity] Open subscription folder in Windows Explorer
    private static void OpenFolderInExplorer(string folderPath)
    {
        #region hide [2026-09-22]
        try
        {
            if (!Directory.Exists(folderPath))
            {
                PrintWarning($"Destination folder does not exist: {folderPath}");
                return;
            }

            Console.WriteLine($"[EXPLORER] Opening destination folder in Windows Explorer: {folderPath}");

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            PrintWarning($"Could not open folder in Explorer: {ex.Message}");
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Configure console input and output encoding to UTF-8 for Thai language support
    private static void ConfigureConsoleEncoding()
    {
        #region hide [2026-09-22]
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        #endregion
    }

    // [2026-09-22][Antigravity] Print application banner
    private static void PrintBanner()
    {
        #region hide [2026-09-22]
        Console.WriteLine("================================================================");
        Console.WriteLine("       CARBON ONE - ACAMP V16 to V20 Migration Tool (Phase 1)    ");
        Console.WriteLine("================================================================");
        #endregion
    }

    // [2026-09-22][Antigravity] Print program exit message
    private static void PrintExitMessage()
    {
        #region hide [2026-09-22]
        Console.WriteLine("\nExiting Migration Tool. Goodbye!");
        #endregion
    }

    // [2026-09-22][Antigravity] Load configuration from appsettings.json and command line arguments
    private static IConfigurationRoot LoadConfiguration(string[] args)
    {
        #region hide [2026-09-22]
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();
        var builder = new ConfigurationBuilder();

        if (File.Exists(Path.Combine(baseDir, "appsettings.json")))
        {
            builder.SetBasePath(baseDir);
        }
        else
        {
            builder.SetBasePath(currentDir);
        }

        return builder
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddCommandLine(args)
            .Build();
        #endregion
    }

    // [2026-09-22][Antigravity] Retrieve V16 connection string from configuration
    private static string? GetV16ConnectionString(IConfiguration config)
    {
        #region hide [2026-09-22]
        return config.GetConnectionString("V16Connection");
        #endregion
    }

    // [2026-09-22][Antigravity] Retrieve output base path anchored relative to current working directory to avoid deep path
    private static string GetOutputBasePath(IConfiguration config)
    {
        #region hide [2026-09-22]
        string configuredPath = config["MigrationSettings:OutputBasePath"] ?? "output";
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        // Anchor output path relative to current working directory to keep path short
        string currentDir = Directory.GetCurrentDirectory();
        if (Directory.Exists(currentDir))
        {
            return Path.GetFullPath(Path.Combine(currentDir, configuredPath));
        }

        string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(exeDirectory, configuredPath);
        #endregion
    }

    // [2026-09-22][Antigravity] Factory method for V16SourceDbContext
    private static V16SourceDbContext CreateV16DbContext(string connectionString)
    {
        #region hide [2026-09-22]
        var optionsBuilder = new DbContextOptionsBuilder<V16SourceDbContext>();
        optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        return new V16SourceDbContext(optionsBuilder.Options);
        #endregion
    }

    // [2026-09-22][Antigravity] Verify database connectivity to V16
    private static async Task<bool> CheckDatabaseConnectionAsync(V16SourceDbContext dbContext)
    {
        #region hide [2026-09-22]
        try
        {
            return await dbContext.Database.CanConnectAsync();
        }
        catch (Exception ex)
        {
            PrintError($"Cannot connect to V16 database: {ex.Message}");
            return false;
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Extract subscription ID from CLI arguments (--subscription-id or -s)
    private static string? ExtractSubscriptionIdFromArgs(string[] args)
    {
        #region hide [2026-09-22]
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--subscription-id" || args[i] == "-s") && i + 1 < args.Length)
            {
                return args[i + 1].Trim();
            }
        }
        return null;
        #endregion
    }

    // [2026-09-22][Antigravity] Validate and parse Subscription GUID string
    private static (bool isValid, Guid guid, string? errorMessage) ParseSubscriptionGuid(string? input)
    {
        #region hide [2026-09-22]
        if (string.IsNullOrWhiteSpace(input))
        {
            return (false, Guid.Empty, "Subscription ID cannot be empty.");
        }

        if (Guid.TryParse(input, out Guid guid))
        {
            return (true, guid, null);
        }

        return (false, Guid.Empty, $"Invalid Subscription ID format: '{input}'. Must be a valid GUID.");
        #endregion
    }

    // [2026-09-22][Antigravity] Fetch subscription and its full hierarchy from V16 database
    private static async Task<V16Subscription?> FetchSubscriptionWithHierarchyAsync(V16SourceDbContext dbContext, Guid subscriptionId)
    {
        #region hide [2026-09-22]
        Console.WriteLine($"\nQuerying V16 database for Subscription: {subscriptionId}...");

        return await dbContext.Subscriptions
            .AsNoTracking()
            .Include(s => s.Organizations)
                .ThenInclude(o => o.Subsidiaries)
                    .ThenInclude(sub => sub.Facilities)
            .FirstOrDefaultAsync(s => s.Id == subscriptionId);
        #endregion
    }

    // [2026-09-22][Antigravity] Display hierarchy summary of Organizations, Subsidiaries, and Facilities
    private static void DisplayHierarchySummary(V16Subscription subscription)
    {
        #region hide [2026-09-22]
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[FOUND] Subscription: {subscription.Title} (RegNo: {subscription.RegistrationNo})");
        Console.ResetColor();

        int orgCount = subscription.Organizations.Count;
        int subCount = subscription.Organizations.Sum(o => o.Subsidiaries.Count);
        int facCount = subscription.Organizations.Sum(o => o.Subsidiaries.Sum(sub => sub.Facilities.Count));

        Console.WriteLine($"  - Organizations: {orgCount}");
        foreach (var org in subscription.Organizations)
        {
            Console.WriteLine($"    * Org [ID: {org.Id}]: {org.Title}");
            foreach (var sub in org.Subsidiaries)
            {
                Console.WriteLine($"      - Subsidiary [ID: {sub.Id}]: {sub.Title} (Equity: {sub.EquityShareApproach}%)");
                foreach (var fac in sub.Facilities)
                {
                    Console.WriteLine($"        + Factory/Facility [ID: {fac.Id}]: {fac.Title}");
                }
            }
        }
        Console.WriteLine($"  - Total Subsidiaries: {subCount}");
        Console.WriteLine($"  - Total Factories/Facilities: {facCount}");
        #endregion
    }

    // [2026-09-22][Antigravity] Sanitize string to be safe for directory naming with configurable maximum length
    private static string SanitizeFolderName(string? name, int maxLength = 25)
    {
        #region hide [2026-09-22]
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }
        string sanitized = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "unnamed";
        }

        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength).TrimEnd(' ', '.', '_', '-');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
        #endregion
    }

    // [2026-09-22][Antigravity] Create real directory hierarchy based on {Id}_{Title} naming convention
    private static (bool isCreated, string rootPath, int orgFolders, int subFolders, int facFolders, string? error) CreateHierarchyDirectoryStructure(string outputBasePath, V16Subscription subscription)
    {
        #region hide [2026-09-22]
        try
        {
            string subFolderName = $"{subscription.Id.ToString()[..8]}_{SanitizeFolderName(subscription.Title, 25)}";
            string subscriptionDir = Path.Combine(outputBasePath, subFolderName);

            // [2026-09-22][Antigravity] ลบโฟลเดอร์เดิมของ Subscription ออกก่อนเพื่อเคลียร์ไฟล์และโครงสร้างเดิมให้สะอาดเสมอ
            if (Directory.Exists(outputBasePath))
            {
                var existingDirs = Directory.GetDirectories(outputBasePath, $"*{subscription.Id.ToString()[..8]}*");
                foreach (var oldDir in existingDirs)
                {
                    try
                    {
                        Directory.Delete(oldDir, recursive: true);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[CLEANUP] Deleted existing subscription folder: {oldDir}");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        PrintWarning($"Could not delete old directory {oldDir}: {ex.Message}");
                    }
                }
            }

            if (Directory.Exists(subscriptionDir))
            {
                try
                {
                    Directory.Delete(subscriptionDir, recursive: true);
                }
                catch (Exception ex)
                {
                    PrintWarning($"Could not delete directory {subscriptionDir}: {ex.Message}");
                }
            }

            Directory.CreateDirectory(subscriptionDir);

            int orgFolders = 0;
            int subFolders = 0;
            int facFolders = 0;

            foreach (var org in subscription.Organizations)
            {
                string orgFolderName = $"{org.Id}_{SanitizeFolderName(org.Title)}";
                string orgDir = Path.Combine(subscriptionDir, orgFolderName);
                Directory.CreateDirectory(orgDir);
                orgFolders++;

                foreach (var sub in org.Subsidiaries)
                {
                    string subFolderNameChild = $"{sub.Id}_{SanitizeFolderName(sub.Title)}";
                    string subDir = Path.Combine(orgDir, subFolderNameChild);
                    Directory.CreateDirectory(subDir);
                    subFolders++;

                    foreach (var fac in sub.Facilities)
                    {
                        string facFolderName = $"{fac.Id}_{SanitizeFolderName(fac.Title)}";
                        string facDir = Path.Combine(subDir, facFolderName);
                        Directory.CreateDirectory(facDir);
                        facFolders++;
                    }
                }
            }

            string fullRootPath = Path.GetFullPath(subscriptionDir);
            return (true, fullRootPath, orgFolders, subFolders, facFolders, null);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, 0, 0, 0, ex.Message);
        }
        #endregion
    }

    // [2026-09-22][Antigravity] Console error output helper
    private static void PrintError(string message)
    {
        #region hide [2026-09-22]
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
        #endregion
    }

    // [2026-09-22][Antigravity] Console warning output helper
    private static void PrintWarning(string message)
    {
        #region hide [2026-09-22]
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN] {message}");
        Console.ResetColor();
        #endregion
    }

    // [2026-09-22][Antigravity] Console success output helper
    private static void PrintSuccess(string message)
    {
        #region hide [2026-09-22]
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("----------------------------------------------------------------");
        Console.WriteLine($"[SUCCESS] {message}");
        Console.WriteLine("----------------------------------------------------------------");
        Console.ResetColor();
        #endregion
    }
}
