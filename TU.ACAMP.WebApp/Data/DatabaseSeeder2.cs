using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;

namespace TU.ACAMP.WebApp.Data;

public static class DatabaseSeeder2
{
    #region class field  
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    #endregion

    #region class method 
    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Starting database seeding process (Subscription only) strictly from JSON files...");
        try
        {
            // [2026-09-19][Antigravity] เรียก Seed Identity และ Library ผ่านเมธอดส่วนกลาง SeedIdentityAndLibraryAsync
            await SeedIdentityAndLibraryAsync(context, logger);
            await SeedSubscriptionsAsync(context, logger);
            await SeedSubscriptionUsersAsync(context, logger);

            // Seed Organization Structure (1 Sub -> 1 Corporate -> 1 Company -> 3 Facilities)
            await SeedOrganizationStructureAsync(context, logger);
            // [2026-09-13: Antigravity] Seed Topic ระดับ Facility (Scope 1) พร้อม Monthly Events (2024-01 ถึง 2025-12, Qty: 100)
           // await SeedFacilityScope1TopicAndEventsAsync(context, logger);
            // [2026-09-13: Antigravity] Seed Topic ระดับ Facility (Scope 2) พร้อม Monthly Events (2024-01 ถึง 2025-12, Qty: 1000)
            await SeedFacilityScope2TopicAndEventsAsync(context, logger);
            await SeedDefaultApiKeysAsync(context, logger);

            logger.LogInformation("Subscription database seeding completed successfully from JSON files.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during database seeding.");
            throw;
        }
        #endregion
    }

    // [2026-09-19][Antigravity] เมธอดสำหรับ Seed Identity (Roles, Users, UserRoles) และ Master Data (Units, Groups, Sources, Mappings) ให้ Seeder อื่นเรียกใช้ร่วมกันได้
    public static async Task SeedIdentityAndLibraryAsync(ApplicationDbContext context, ILogger logger)
    {
        await SeedRolesAsync(context, logger);
        await SeedUsersAsync(context, logger);
        await SeedUserRolesAsync(context, logger);

        // Seed Library / Master Data
        await SeedEmissionUnitsAsync(context, logger);
        await SeedEmissionGroupsAsync(context, logger);
        await SeedActivityGroupsAsync(context, logger);
        await SeedEmissionSourcesAsync(context, logger);
        await SeedEmissionMappingsAsync(context, logger);
    }

    private static string GetJsonFilePath(string fileName)
    {
        #region hide [2026-08-15]
        var baseDir = AppContext.BaseDirectory;
        var currentDir = Directory.GetCurrentDirectory();

        var candidates = new[]
        {
            Path.Combine(baseDir, "Files", "JSON", fileName),
            Path.Combine(baseDir, "Files", "json", fileName),
            Path.Combine(currentDir, "TU.ACAMP.WebApp", "Files", "JSON", fileName),
            Path.Combine(currentDir, "TU.ACAMP.WebApp", "Files", "json", fileName),
            Path.Combine(currentDir, "Files", "JSON", fileName),
            Path.Combine(currentDir, "Files", "json", fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
        #endregion
    }

    /// <summary>
    /// อ่านค่า bool จาก JsonNode แบบยืดหยุ่น รองรับทั้ง true/false, ตัวเลข 0/1 (MySQL export)
    /// และสตริง "true"/"1"/"Y" โดยไม่โยน InvalidOperationException
    /// </summary>
    private static bool ReadBool(JsonNode? node, bool defaultValue = false)
    {
        #region hide [2026-08-17]
        if (node is not JsonValue value) return defaultValue;

        if (value.TryGetValue<bool>(out var boolResult)) return boolResult;
        if (value.TryGetValue<int>(out var intResult)) return intResult != 0;
        if (value.TryGetValue<long>(out var longResult)) return longResult != 0;
        if (value.TryGetValue<decimal>(out var decResult)) return decResult != 0;

        var raw = value.ToString().Trim();
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        if (bool.TryParse(raw, out var parsedBool)) return parsedBool;
        if (int.TryParse(raw, out var parsedInt)) return parsedInt != 0;

        return raw.Equals("Y", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("YES", StringComparison.OrdinalIgnoreCase)
            || defaultValue;
        #endregion
    }

    /// <summary>
    /// อ่านค่า int จาก JsonNode แบบยืดหยุ่น รองรับทั้งตัวเลขและสตริงตัวเลข
    /// </summary>
    private static int ReadInt(JsonNode? node, int defaultValue = 0)
    {
        #region hide [2026-08-17]
        if (node is not JsonValue value) return defaultValue;

        if (value.TryGetValue<int>(out var intResult)) return intResult;
        if (value.TryGetValue<long>(out var longResult)) return (int)longResult;
        if (value.TryGetValue<decimal>(out var decResult)) return (int)decResult;

        return int.TryParse(value.ToString().Trim(), out var parsed) ? parsed : defaultValue;
        #endregion
    }

    /// <summary>
    /// อ่านค่า decimal จาก JsonNode แบบยืดหยุ่น รองรับทั้งตัวเลข float/double/decimal, scientific notation และสตริงตัวเลข
    /// </summary>
    private static decimal ReadDecimal(JsonNode? node, decimal defaultValue = 0m)
    {
        #region hide [2026-08-17]
        if (node is not JsonValue value) return defaultValue;

        if (value.TryGetValue<decimal>(out var decResult)) return decResult;
        if (value.TryGetValue<double>(out var dblResult)) return (decimal)dblResult;
        if (value.TryGetValue<float>(out var fltResult)) return (decimal)fltResult;
        if (value.TryGetValue<int>(out var intResult)) return intResult;
        if (value.TryGetValue<long>(out var longResult)) return longResult;

        var raw = value.ToString().Trim();
        if (string.IsNullOrEmpty(raw)) return defaultValue;

        if (decimal.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedDec))
            return parsedDec;

        if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedDbl))
            return (decimal)parsedDbl;

        return defaultValue;
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedRolesAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Seeding Roles from system_roles.json...");
        var jsonPath = GetJsonFilePath("system_roles.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'system_roles.json' at probed paths. Skipping role seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var rolesNode = JsonNode.Parse(jsonString, documentOptions: JsonOptions);
        if (rolesNode is JsonArray rolesArray)
        {
            var count = 0;
            foreach (var item in rolesArray)
            {
                if (item is JsonObject obj)
                {
                    var roleId = obj["Id"]?.ToString() ?? Guid.NewGuid().ToString();
                    var roleName = obj["Name"]?.ToString() ?? string.Empty;
                    var normalizedName = obj["NormalizedName"]?.ToString() ?? roleName.ToUpperInvariant();

                    if (!string.IsNullOrEmpty(roleName) && !await context.Roles.AnyAsync(r => r.Name == roleName))
                    {
                        await context.Roles.AddAsync(new IdentityRole
                        {
                            Id = roleId,
                            Name = roleName,
                            NormalizedName = normalizedName,
                            ConcurrencyStamp = Guid.NewGuid().ToString()
                        });
                        count++;
                        logger.LogInformation("Seeded '{Role}' role from JSON.", roleName);
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync();
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedUsersAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Seeding Users from system_users.json...");
        var jsonPath = GetJsonFilePath("system_users.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'system_users.json' at probed paths. Skipping user seeding.");
            return;
        }

        var hasher = new PasswordHasher<ApplicationUser>();
        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var usersNode = JsonNode.Parse(jsonString, documentOptions: JsonOptions);
        if (usersNode is JsonArray usersArray)
        {
            var count = 0;
            foreach (var item in usersArray)
            {
                if (item is JsonObject obj)
                {
                    var userId = obj["Id"]?.ToString() ?? Guid.NewGuid().ToString();
                    var userName = obj["UserName"]?.ToString() ?? string.Empty;
                    var email = obj["Email"]?.ToString() ?? userName;
                    var rawPassword = obj["Password"]?.ToString() ?? "1234";

                    if (!string.IsNullOrEmpty(email) && !await context.Users.AnyAsync(u => u.Email == email))
                    {
                        var user = new ApplicationUser
                        {
                            Id = userId,
                            UserName = userName,
                            NormalizedUserName = userName.ToUpperInvariant(),
                            Email = email,
                            NormalizedEmail = email.ToUpperInvariant(),
                            EmailConfirmed = true,
                            SecurityStamp = Guid.NewGuid().ToString("D"),
                            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
                            LockoutEnabled = true
                        };
                        user.PasswordHash = hasher.HashPassword(user, rawPassword);
                        await context.Users.AddAsync(user);
                        count++;
                        logger.LogInformation("Seeded User ({Email}) from JSON.", email);
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync();
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedUserRolesAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Seeding User Roles from system_user_roles.json...");
        var jsonPath = GetJsonFilePath("system_user_roles.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'system_user_roles.json' at probed paths. Skipping user roles seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var userRolesNode = JsonNode.Parse(jsonString, documentOptions: JsonOptions);
        if (userRolesNode is JsonArray userRolesArray)
        {
            var count = 0;
            foreach (var item in userRolesArray)
            {
                if (item is JsonObject obj)
                {
                    var userId = obj["UserId"]?.ToString() ?? string.Empty;
                    var roleId = obj["RoleId"]?.ToString() ?? string.Empty;

                    if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(roleId))
                    {
                        if (!await context.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId))
                        {
                            await context.UserRoles.AddAsync(new IdentityUserRole<string>
                            {
                                UserId = userId,
                                RoleId = roleId
                            });
                            count++;
                            logger.LogInformation("Mapped User {UserId} to Role {RoleId} from JSON.", userId, roleId);
                        }
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync();
            }
        }
        #endregion
    }

    private static async Task SeedSubscriptionsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Seeding Subscriptions from system_subscriptions.json...");
        var jsonPath = GetJsonFilePath("system_subscriptions.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'system_subscriptions.json' at probed paths. Skipping subscriptions seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var subsNode = JsonNode.Parse(jsonString, documentOptions: JsonOptions);
        if (subsNode is JsonArray subsArray)
        {
            var count = 0;
            foreach (var item in subsArray)
            {
                if (item is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"]);
                    var title = obj["Title"]?.ToString() ?? obj["Name"]?.ToString() ?? string.Empty;
                    var ownerEmail = obj["OwnerEmail"]?.ToString() ?? string.Empty;
                    var ownerName = obj["OwnerName"]?.ToString() ?? string.Empty;
                    var ownerPhone = obj["OwnerPhone"]?.ToString() ?? obj["PhoneNumber"]?.ToString() ?? string.Empty;
                    var taxId = obj["TaxId"]?.ToString() ?? string.Empty;
                    var contactPerson = obj["ContactPerson"]?.ToString() ?? string.Empty;
                    var billingAddress = obj["BillingAddress"]?.ToString() ?? string.Empty;
                    var remarks = obj["Remarks"]?.ToString() ?? string.Empty;

                    var planEnum = eSubscriptionPlan.Basic;
                    if (obj["SubscriptionPlan"] != null)
                    {
                        Enum.TryParse<eSubscriptionPlan>(obj["SubscriptionPlan"]?.ToString(), true, out planEnum);
                    }

                    var ownerTypeEnum = eSubscriptionOwnerType.Individual;
                    if (obj["OwnerType"] != null)
                    {
                        Enum.TryParse<eSubscriptionOwnerType>(obj["OwnerType"]?.ToString(), true, out ownerTypeEnum);
                    }

                    var startDate = DateTime.UtcNow.AddMonths(-1);
                    if (obj["StartDateOffsetMonths"] != null && int.TryParse(obj["StartDateOffsetMonths"]?.ToString(), out int offsetStartMonths))
                    {
                        startDate = DateTime.UtcNow.AddMonths(offsetStartMonths);
                    }
                    else if (obj["StartDateOffsetYears"] != null && int.TryParse(obj["StartDateOffsetYears"]?.ToString(), out int offsetStartYears))
                    {
                        startDate = DateTime.UtcNow.AddYears(offsetStartYears);
                    }

                    var expiryDate = DateTime.UtcNow.AddYears(1);
                    if (obj["ExpiryDateOffsetMonths"] != null && int.TryParse(obj["ExpiryDateOffsetMonths"]?.ToString(), out int offsetExpMonths))
                    {
                        expiryDate = DateTime.UtcNow.AddMonths(offsetExpMonths);
                    }
                    else if (obj["ExpiryDateOffsetDays"] != null && int.TryParse(obj["ExpiryDateOffsetDays"]?.ToString(), out int offsetExpDays))
                    {
                        expiryDate = DateTime.UtcNow.AddDays(offsetExpDays);
                    }

                    var isActive = true;
                    if (obj["IsActive"] != null)
                    {
                        bool.TryParse(obj["IsActive"]?.ToString(), out isActive);
                    }

                    var sub = new TbSubscription
                    {
                        Title = title,
                        OwnerEmail = ownerEmail,
                        OwnerName = ownerName,
                        OwnerPhone = ownerPhone,
                        TaxId = taxId,
                        ContactPerson = contactPerson,
                        BillingAddress = billingAddress,
                        SubscriptionPlan = planEnum,
                        OwnerType = ownerTypeEnum,
                        StartDate = startDate,
                        ExpiryDate = expiryDate,
                        IsActive = isActive,
                        Remarks = remarks
                    };
                    if (id > 0)
                    {
                        sub.Id = id;
                    }
                    sub.ApplyPlanQuotas(planEnum);

                    var exists = await context.Subscriptions.AnyAsync(s => s.TaxId == taxId || s.OwnerEmail == ownerEmail);
                    if (!exists)
                    {
                        await context.Subscriptions.AddAsync(sub);
                        count++;
                        logger.LogInformation("Seeded subscription: '{Title}' (Owner: {Email}) from JSON.", sub.Title, sub.OwnerEmail);
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
            }
        }
        #endregion
    }

    private static async Task SeedSubscriptionUsersAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Seeding Subscription Users from system_subscription_users.json...");
        var jsonPath = GetJsonFilePath("system_subscription_users.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'system_subscription_users.json' at probed paths. Skipping subscription users seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var subUsersNode = JsonNode.Parse(jsonString, documentOptions: JsonOptions);
        if (subUsersNode is JsonArray subUsersArray)
        {
            var allSubs = await context.Subscriptions.AsNoTracking().ToListAsync();
            var subMapById = allSubs.ToDictionary(s => s.Id);

            var count = 0;
            foreach (var item in subUsersArray)
            {
                if (item is JsonObject obj)
                {
                    var email = obj["Email"]?.ToString() ?? string.Empty;
                    var subId = ReadInt(obj["SubscriptionId"]);
                    if (string.IsNullOrEmpty(email)) continue;

                    if (!subMapById.ContainsKey(subId))
                    {
                        var matchedSub = allSubs.FirstOrDefault(s => s.OwnerEmail == email || s.Id == subId);
                        if (matchedSub != null)
                        {
                            subId = matchedSub.Id;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var roleEnum = eSubscriptionRole.Viewer;
                    if (obj["Role"] != null)
                    {
                        Enum.TryParse<eSubscriptionRole>(obj["Role"]?.ToString(), true, out roleEnum);
                    }

                    var remarks = obj["Remarks"]?.ToString() ?? string.Empty;

                    if (!await context.SubscriptionUsers.AnyAsync(su => su.Email == email && su.SubscriptionId == subId))
                    {
                        await context.SubscriptionUsers.AddAsync(new TbSubscriptionUser
                        {
                            Email = email,
                            Role = roleEnum,
                            SubscriptionId = subId,
                            Remarks = remarks
                        });
                        count++;
                        logger.LogInformation("Mapped subscription user: {Email} -> Subscription ID {SubId} from JSON.", email, subId);
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedEmissionUnitsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        logger.LogInformation("Seeding Emission Units from tb_emission_unit.json...");
        var jsonPath = GetJsonFilePath("tb_emission_unit.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'tb_emission_unit.json' at probed paths. Skipping emission units seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var unitsNode = JsonNode.Parse(jsonString);
        if (unitsNode is JsonArray unitsArray)
        {
            var count = 0;
            foreach (var item in unitsArray)
            {
                if (item is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"]);
                    var title = obj["Title"]?.ToString() ?? string.Empty;
                    var remark = obj["Remark"]?.ToString() ?? obj["Memo"]?.ToString() ?? string.Empty;
                    var isReadOnly = ReadBool(obj["IsReadOnly"], true);

                    var existing = await context.Units.FindAsync(id);
                    if (existing == null)
                    {
                        var unit = new TbEmissionUnit
                        {
                            Id = id,
                            Title = title,
                            Remark = remark,
                            SourceType = isReadOnly ? eSourceType.Default : eSourceType.Custom,
                            SubscriptionId = null,
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.Units.AddAsync(unit);
                        count++;
                    }
                    else
                    {
                        existing.Title = title;
                        existing.Remark = remark;
                        existing.SourceType = isReadOnly ? eSourceType.Default : eSourceType.Custom;
                        existing.SubscriptionId = null;
                        existing.IsActive = true;
                        existing.IsDeleted = false;
                        count++;
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Synced {Count} Emission Units from JSON.", count);
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedEmissionGroupsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        if (await context.EmissionGroups.AnyAsync()) return;

        logger.LogInformation("Seeding Emission Groups from tb_emission_group.json...");
        var jsonPath = GetJsonFilePath("tb_emission_group.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'tb_emission_group.json' at probed paths. Skipping emission groups seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var groupsNode = JsonNode.Parse(jsonString);
        if (groupsNode is JsonArray groupsArray)
        {
            var count = 0;
            foreach (var item in groupsArray)
            {
                if (item is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"]);
                    var code = obj["Code"]?.ToString() ?? obj["GroupCode"]?.ToString() ?? string.Empty;
                    var title = obj["Title"]?.ToString() ?? obj["NameEnglish"]?.ToString() ?? string.Empty;
                    var titleTh = obj["TitleTh"]?.ToString() ?? obj["NameThai"]?.ToString() ?? string.Empty;
                    var remark = obj["Remark"]?.ToString() ?? string.Empty;
                    var isReadOnly = ReadBool(obj["IsReadOnly"], true);

                    var tgoScope = eTgoScopeNo.Scope1;
                    if (obj["TgoScopeNo"] != null)
                    {
                        if (int.TryParse(obj["TgoScopeNo"]?.ToString(), out int tgoInt))
                            tgoScope = (eTgoScopeNo)tgoInt;
                        else
                            Enum.TryParse<eTgoScopeNo>(obj["TgoScopeNo"]?.ToString(), true, out tgoScope);
                    }

                    var isoCategory = eIsoCategoryNo.Category1;
                    if (obj["IsoCategoryNo"] != null)
                    {
                        if (int.TryParse(obj["IsoCategoryNo"]?.ToString(), out int isoInt))
                            isoCategory = (eIsoCategoryNo)isoInt;
                        else
                            Enum.TryParse<eIsoCategoryNo>(obj["IsoCategoryNo"]?.ToString(), true, out isoCategory);
                    }

                    var group = new TbEmissionGroup
                    {
                        Id = id,
                        Code = code,
                        Title = title,
                        TitleTh = titleTh,
                        Remark = remark,
                        TgoScopeNo = tgoScope,
                        IsoCategoryNo = isoCategory,
                        IsReadOnly = isReadOnly,
                        SourceType = isReadOnly ? eSourceType.Default : eSourceType.Custom,
                        SubscriptionId = null
                    };

                    if (!await context.EmissionGroups.AnyAsync(g => g.Id == id))
                    {
                        await context.EmissionGroups.AddAsync(group);
                        count++;
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} Emission Groups from JSON.", count);
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedActivityGroupsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-15]
        if (await context.ActivityGroups.AnyAsync()) return;

        logger.LogInformation("Seeding Activity Groups from tb_activity_group.json...");
        var jsonPath = GetJsonFilePath("tb_activity_group.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'tb_activity_group.json' at probed paths. Skipping activity groups seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var groupsNode = JsonNode.Parse(jsonString);
        if (groupsNode is JsonArray groupsArray)
        {
            var count = 0;
            foreach (var item in groupsArray)
            {
                if (item is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"]);
                    var code = obj["Code"]?.ToString() ?? obj["GroupCode"]?.ToString() ?? string.Empty;
                    var title = obj["Title"]?.ToString() ?? obj["TgoNameEn"]?.ToString() ?? string.Empty;
                    var titleTh = obj["TitleTh"]?.ToString() ?? obj["TgoNameTh"]?.ToString() ?? obj["IsoNameTh"]?.ToString() ?? string.Empty;
                    var remark = obj["Remark"]?.ToString() ?? string.Empty;

                    var tgoScope = eTgoScopeNo.Scope1;
                    if (obj["TgoScopeNo"] != null)
                    {
                        if (int.TryParse(obj["TgoScopeNo"]?.ToString(), out int tgoInt))
                            tgoScope = (eTgoScopeNo)tgoInt;
                        else
                            Enum.TryParse<eTgoScopeNo>(obj["TgoScopeNo"]?.ToString(), true, out tgoScope);
                    }

                    var isoCategory = eIsoCategoryNo.Category1;
                    if (obj["IsoCategoryNo"] != null)
                    {
                        if (int.TryParse(obj["IsoCategoryNo"]?.ToString(), out int isoInt))
                            isoCategory = (eIsoCategoryNo)isoInt;
                        else
                            Enum.TryParse<eIsoCategoryNo>(obj["IsoCategoryNo"]?.ToString(), true, out isoCategory);
                    }

                    var group = new TbActivityGroup
                    {
                        Id = id,
                        Code = code,
                        Title = title,
                        TitleTh = titleTh,
                        Remark = remark,
                        TgoScopeNo = tgoScope,
                        IsoCategoryNo = isoCategory
                    };

                    if (!await context.ActivityGroups.AnyAsync(g => g.Id == id))
                    {
                        await context.ActivityGroups.AddAsync(group);
                        count++;
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} Activity Groups from JSON.", count);
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedEmissionSourcesAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-17]
        var isSeeded = await context.EmissionSources.AnyAsync();
        if (isSeeded && await context.EmissionSources.AnyAsync(s => s.Id == -1)) return;

        logger.LogInformation("Seeding Emission Sources from tb_emission_source.json...");
        var jsonPath = GetJsonFilePath("tb_emission_source.json");
        if (!File.Exists(jsonPath))
        {
            logger.LogError("Could not find 'tb_emission_source.json' at probed paths. Skipping emission sources seeding.");
            return;
        }

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var sourcesNode = JsonNode.Parse(jsonString);
        if (sourcesNode is JsonArray sourcesArray)
        {
            var count = 0;
            foreach (var item in sourcesArray)
            {
                if (item is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"]);
                    if (isSeeded && id != -1) continue;
                    var title = obj["Title"]?.ToString() ?? string.Empty;
                    var remark = obj["Memo"]?.ToString() ?? obj["Remark"]?.ToString() ?? string.Empty;
                    var groupId = ReadInt(obj["GroupId"]);
                    var unitId = ReadInt(obj["UnitId"]);
                    var refStr = obj["Ref"]?.ToString() ?? string.Empty;

                    var co2 = ReadDecimal(obj["CO2"]);
                    var fossilCh4 = ReadDecimal(obj["FossilCH4"]);
                    var ch4 = ReadDecimal(obj["CH4"]);
                    var n2o = ReadDecimal(obj["N2O"]);
                    var sf6 = ReadDecimal(obj["SF6"]);
                    var nf3 = ReadDecimal(obj["NF3"]);
                    var hfcs = ReadDecimal(obj["HFCs"]);
                    var pfcs = ReadDecimal(obj["PFCs"]);

                    var gwpCo2 = ReadDecimal(obj["gwpCO2"], 1m);
                    var gwpFossilCh4 = ReadDecimal(obj["gwpFossilCH4"], 30m);
                    var gwpCh4 = ReadDecimal(obj["gwpCH4"], 28m);
                    var gwpN2o = ReadDecimal(obj["gwpN2O"], 265m);
                    var gwpSf6 = ReadDecimal(obj["gwpSF6"], 23500m);
                    var gwpNf3 = ReadDecimal(obj["gwpNF3"], 16100m);
                    var gwpHfcs = ReadDecimal(obj["gwpHFCs"], 1m);
                    var gwpPfcs = ReadDecimal(obj["gwpPFCs"], 1m);

                    var selfCollect = ReadBool(obj["SelfCollect"], false);
                    var supplier = ReadBool(obj["Supplier"], false);
                    var thlcidb = ReadBool(obj["THLCIDB"], false);
                    var tgoef = ReadBool(obj["TGOEF"], false);
                    var thaiRes = ReadBool(obj["ThaiRes"], false);
                    var intDb = ReadBool(obj["IntDB"], false);
                    var other = ReadBool(obj["Other"], false);
                    var substitute = ReadBool(obj["Substitute"] ?? obj["Subsitute"], false);

                    var isReadOnly = ReadBool(obj["IsReadOnly"], true);

                    DateTime? dateApproved = null;
                    if (DateTime.TryParse(obj["approved_date"]?.ToString() ?? obj["DateApproved"]?.ToString(), out var parsedAppr))
                        dateApproved = parsedAppr;

                    DateTime? dateExpire = null;
                    if (DateTime.TryParse(obj["expire_date"]?.ToString() ?? obj["DateExpire"]?.ToString(), out var parsedExp))
                        dateExpire = parsedExp;

                    var source = new TbEmissionSource
                    {
                        Id = id,
                        Title = title,
                        Remark = remark,
                        GroupId = groupId,
                        UnitId = unitId,
                        Ref = refStr,
                        CO2 = co2,
                        FossilCH4 = fossilCh4,
                        CH4 = ch4,
                        N2O = n2o,
                        SF6 = sf6,
                        NF3 = nf3,
                        HFCs = hfcs,
                        PFCs = pfcs,
                        gwpCO2 = gwpCo2,
                        gwpFossilCH4 = gwpFossilCh4,
                        gwpCH4 = gwpCh4,
                        gwpN2O = gwpN2o,
                        gwpSF6 = gwpSf6,
                        gwpNF3 = gwpNf3,
                        gwpHFCs = gwpHfcs,
                        gwpPFCs = gwpPfcs,
                        SelfCollect = selfCollect,
                        Supplier = supplier,
                        THLCIDB = thlcidb,
                        TGOEF = tgoef,
                        ThaiRes = thaiRes,
                        IntDB = intDb,
                        Other = other,
                        Substitute = substitute,
                        IsReadOnly = isReadOnly,
                        DateApproved = dateApproved,
                        DateExpire = dateExpire,
                        SourceType = isReadOnly ? eSourceType.Default : eSourceType.Custom,
                        SubscriptionId = null,
                        IsActive = true,
                        IsDeleted = false
                    };

                    if (!await context.EmissionSources.AnyAsync(s => s.Id == id))
                    {
                        await context.EmissionSources.AddAsync(source);
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} Emission Sources from JSON.", count);
            }
        }
        #endregion
    }

    // [2026-09-23][Claude] private -> internal เพื่อให้ DatabaseSeeder6 เรียกทีละตารางได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedEmissionMappingsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-20]
        var filePath = GetJsonFilePath("tb_emission_mapping.json");
        if (!File.Exists(filePath))
        {
            logger.LogWarning("Emission mappings seed file not found at: {Path}", filePath);
            return;
        }

        var json = await File.ReadAllTextAsync(filePath);
        var array = JsonNode.Parse(json, null, JsonOptions)?.AsArray();

        if (array != null)
        {
            var count = 0;
            foreach (var node in array)
            {
                if (node is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"], 0);
                    var title = obj["Title"]?.ToString()?.Trim() ?? string.Empty;
                    var remark = obj["Remark"]?.ToString()?.Trim() ?? string.Empty;
                    var ratio = ReadDecimalNullable(obj["UnitConversionRatio"]);
                    var s1Id = ReadInt(obj["Scope1ChildId"], 0);
                    var s1Title = obj["Scope1ChildTitle"]?.ToString()?.Trim() ?? string.Empty;
                    var s1Unit = obj["Scope1ChildUnit"]?.ToString()?.Trim() ?? string.Empty;
                    var s2Id = ReadInt(obj["Scope2ChildId"], 0);
                    var s2Title = obj["Scope2ChildTitle"]?.ToString()?.Trim() ?? string.Empty;
                    var s2Unit = obj["Scope2ChildUnit"]?.ToString()?.Trim() ?? string.Empty;
                    var s3Id = ReadInt(obj["Scope3ChildId"], 0);
                    var s3Title = obj["Scope3ChildTitle"]?.ToString()?.Trim() ?? string.Empty;
                    var s3Unit = obj["Scope3ChildUnit"]?.ToString()?.Trim() ?? string.Empty;
                    var bioId = ReadInt(obj["BiogenicChildId"], 0);
                    var bioTitle = obj["BiogenicChildTitle"]?.ToString()?.Trim() ?? string.Empty;
                    var bioUnit = obj["BiogenicChildUnit"]?.ToString()?.Trim() ?? string.Empty;
                    var bioPercent = ReadDecimal(obj["BiogenicSharePercent"], 0m);

                    var mapping = new TbEmissionMapping
                    {
                        Id = id,
                        Title = title,
                        Remark = remark,
                        UnitConversionRatio = ratio,
                        Scope1ChildId = s1Id,
                        Scope1ChildTitle = s1Title,
                        Scope1ChildUnit = s1Unit,
                        Scope2ChildId = s2Id,
                        Scope2ChildTitle = s2Title,
                        Scope2ChildUnit = s2Unit,
                        Scope3ChildId = s3Id,
                        Scope3ChildTitle = s3Title,
                        Scope3ChildUnit = s3Unit,
                        BiogenicChildId = bioId,
                        BiogenicChildTitle = bioTitle,
                        BiogenicChildUnit = bioUnit,
                        BiogenicSharePercent = bioPercent,
                        SourceType = eSourceType.Default,
                        SubscriptionId = null,
                        IsActive = true,
                        IsDeleted = false
                    };

                    if (!await context.EmissionMappings.AnyAsync(m => m.Id == id))
                    {
                        await context.EmissionMappings.AddAsync(mapping);
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} Emission Mappings from JSON.", count);
            }
        }
        #endregion
    }

    private static decimal? ReadDecimalNullable(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<decimal>(out var dec)) return dec;
        if (value.TryGetValue<double>(out var dbl)) return (decimal)dbl;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (decimal.TryParse(value.ToString().Trim(), out var parsedDec)) return parsedDec;
        return null;
    }

    public record SeedTopicTemplate(
        string Title,
        string GroupCode,
        string[] SourceKeywords,
        decimal MonthlyBaseQty,
        decimal MonthlyVariance
    );

    private static async Task<List<SeedTopicTemplate>> LoadTopicTemplatesAsync(ILogger logger)
    {
        var filePath = GetJsonFilePath("system_topic_templates.json");
        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var array = JsonNode.Parse(json, null, JsonOptions)?.AsArray();
                if (array != null && array.Count > 0)
                {
                    var list = new List<SeedTopicTemplate>();
                    foreach (var node in array)
                    {
                        if (node is JsonObject obj)
                        {
                            var title = obj["Title"]?.ToString() ?? string.Empty;
                            var groupCode = obj["GroupCode"]?.ToString() ?? string.Empty;
                            var keywordsNode = obj["SourceKeywords"]?.AsArray();
                            var keywords = keywordsNode != null
                                ? keywordsNode.Select(k => k?.ToString() ?? "").Where(k => !string.IsNullOrEmpty(k)).ToArray()
                                : Array.Empty<string>();
                            var baseQty = ReadDecimal(obj["MonthlyBaseQty"], 1000m);
                            var variance = ReadDecimal(obj["MonthlyVariance"], 100m);

                            if (!string.IsNullOrEmpty(title))
                            {
                                list.Add(new SeedTopicTemplate(title, groupCode, keywords, baseQty, variance));
                            }
                        }
                    }
                    if (list.Count > 0)
                    {
                        return list;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load topic templates from JSON. Falling back to default list.");
            }
        }

        return new List<SeedTopicTemplate>
        {
            new("การใช้น้ำมันดีเซลสำหรับเครื่องกำเนิดไฟฟ้าสำรอง (Emergency Generator Diesel)", "0101", new[] { "Diesel B7", "Diesel" }, 1200m, 300m),
            new("การใช้ก๊าซ LPG สำหรับหม้อต้มไอน้ำและกระบวนการทำความร้อน (Process Boiler LPG)", "0101", new[] { "LPG" }, 4500m, 800m),
            new("การใช้ไฟฟ้า MEA อาคารสำนักงานใหญ่ (MEA Grid Electricity - HQ Building)", "0201", new[] { "Electricity", "Grid", "MEA" }, 45000m, 8000m)
        };
    }

    private static TbEmissionSource FindBestSource(List<TbEmissionSource> sources, string[] keywords, eTgoScopeNo scope)
    {
        foreach (var kw in keywords)
        {
            var match = sources.FirstOrDefault(s => s.Title.Contains(kw, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        foreach (var kw in keywords)
        {
            var match = sources.FirstOrDefault(s => s.Ref != null && s.Ref.Contains(kw, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        var scopeMatch = sources.FirstOrDefault(s => s.Group?.TgoScopeNo == scope);
        return scopeMatch ?? sources.First();
    }

    private static TbActivityGroup FindBestActivityGroup(List<TbActivityGroup> ags, string groupCode)
    {
        var match = ags.FirstOrDefault(ag => ag.Code == groupCode || ag.Id.ToString() == groupCode);
        if (match != null) return match;

        if (groupCode.StartsWith("0101")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && ag.Code.EndsWith("01")) ?? ags.First();
        if (groupCode.StartsWith("0102")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && ag.Code.EndsWith("02")) ?? ags.First();
        if (groupCode.StartsWith("0103")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && ag.Code.EndsWith("03")) ?? ags.First();
        if (groupCode.StartsWith("0104")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && ag.Code.EndsWith("04")) ?? ags.First();
        if (groupCode.StartsWith("0201")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && ag.Code.EndsWith("01")) ?? ags.First();
        if (groupCode.StartsWith("0202")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && ag.Code.EndsWith("02")) ?? ags.First();
        if (groupCode.StartsWith("03")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3) ?? ags.First();
        if (groupCode.StartsWith("04")) return ags.FirstOrDefault(ag => ag.TgoScopeNo == (eTgoScopeNo)4 || ag.Code.StartsWith("04") || ag.Code.StartsWith("SR")) ?? ags.First();

        return ags.First();
    }

    public record OrgHierarchy(TbCorporate Corporate, TbCompany Company, TbFacility Facility);

    public static async Task<OrgHierarchy> SeedOrganizationLevelsAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        logger.LogInformation("Seeding Organization Levels (Corporate -> Company -> Facility) from JSON...");
        var orgJsonPath = GetJsonFilePath("system_organization_structure.json");
        string corpCode = "CRP-001", corpTitle = "Global Green Corporate Group", corpRemark = "Corporate Headquarter Level";
        decimal corpThreshold = 5.0m;
        string compCode = "COM-001", compTitle = "Eco Tech Manufacturing Thailand Co., Ltd.", compRemark = "Company Subsidiary Level";
        int compEquity = 100;
        string facCode = "FAC-001", facTitle = "Bangkok Smart Manufacturing Plant Facility", facRemark = "Facility Plant Level";

        if (File.Exists(orgJsonPath))
        {
            try
            {
                var orgJson = await File.ReadAllTextAsync(orgJsonPath);
                var orgArray = JsonNode.Parse(orgJson, null, JsonOptions)?.AsArray();
                var orgItem = orgArray?.FirstOrDefault(n => ReadInt(n?["SubscriptionId"], 1) == subId) ?? orgArray?.FirstOrDefault();
                if (orgItem is JsonObject orgObj)
                {
                    if (orgObj["Corporate"] is JsonObject cObj)
                    {
                        corpCode = cObj["Code"]?.ToString() ?? corpCode;
                        corpTitle = cObj["Title"]?.ToString() ?? corpTitle;
                        corpRemark = cObj["Remark"]?.ToString() ?? corpRemark;
                        corpThreshold = ReadDecimal(cObj["MaterialityThreshold"], corpThreshold);
                    }
                    if (orgObj["Company"] is JsonObject compObj)
                    {
                        compCode = compObj["Code"]?.ToString() ?? compCode;
                        compTitle = compObj["Title"]?.ToString() ?? compTitle;
                        compRemark = compObj["Remark"]?.ToString() ?? compRemark;
                        compEquity = ReadInt(compObj["EquityShare"], compEquity);
                    }
                    if (orgObj["Facility"] is JsonObject facObj)
                    {
                        facCode = facObj["Code"]?.ToString() ?? facCode;
                        facTitle = facObj["Title"]?.ToString() ?? facTitle;
                        facRemark = facObj["Remark"]?.ToString() ?? facRemark;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load system_organization_structure.json. Using fallback values.");
            }
        }

        // 1. Corporate
        var corp = await context.Corporates.FirstOrDefaultAsync(c => c.SubscriptionId == subId && !c.IsDeleted);
        if (corp == null)
        {
            corp = new TbCorporate
            {
                SubscriptionId = subId,
                Code = corpCode,
                Title = corpTitle,
                Remark = corpRemark,
                MaterialityThreshold = corpThreshold,
                IsActive = true,
                IsDeleted = false
            };
            await context.Corporates.AddAsync(corp);
            await context.SaveChangesAsync("System");
            logger.LogInformation("Seeded Corporate '{Title}' ({Code}).", corp.Title, corp.Code);
        }

        // 2. Company
        var comp = await context.Companies.FirstOrDefaultAsync(c => c.CorporateId == corp.Id && !c.IsDeleted);
        if (comp == null)
        {
            comp = new TbCompany
            {
                CorporateId = corp.Id,
                Code = compCode,
                Title = compTitle,
                EquityShare = compEquity,
                Remark = compRemark,
                IsActive = true,
                IsDeleted = false
            };
            await context.Companies.AddAsync(comp);
            await context.SaveChangesAsync("System");
            logger.LogInformation("Seeded Company '{Title}' ({Code}).", comp.Title, comp.Code);
        }

        // 3. Facility
        var fac = await context.Facilities.FirstOrDefaultAsync(f => f.CompanyId == comp.Id && !f.IsDeleted);
        if (fac == null)
        {
            fac = new TbFacility
            {
                CompanyId = comp.Id,
                Code = facCode,
                Title = facTitle,
                Remark = facRemark,
                IsActive = true,
                IsDeleted = false
            };
            await context.Facilities.AddAsync(fac);
            await context.SaveChangesAsync("System");
            logger.LogInformation("Seeded Facility '{Title}' ({Code}).", fac.Title, fac.Code);
        }

        return new OrgHierarchy(corp, comp, fac);
    }

    public static async Task<List<(TbActivityTopic Topic, SeedTopicTemplate Template)>> SeedTopicsAsync(
        ApplicationDbContext context,
        int subId,
        OrgHierarchy org,
        ILogger logger)
    {
        logger.LogInformation("Seeding Topics & Mappings across Organization Levels from JSON...");
        var emissionGroups = await context.EmissionGroups.Where(g => !g.IsDeleted).ToListAsync();
        var activityGroups = await context.ActivityGroups.Where(ag => !ag.IsDeleted).ToListAsync();
        var masterSources = await context.EmissionSources.Where(s => !s.IsDeleted).Include(s => s.Group).ToListAsync();
        int defaultEgId = emissionGroups.FirstOrDefault()?.Id ?? 1;

        var topicTemplates = await LoadTopicTemplatesAsync(logger);

        var levelTargets = new[]
        {
            (Level: eTopicLevel.Corporate, TargetId: org.Corporate.Id, Prefix: "Corporate HQ"),
            (Level: eTopicLevel.Company, TargetId: org.Company.Id, Prefix: "Company"),
            (Level: eTopicLevel.Facility, TargetId: org.Facility.Id, Prefix: "Facility Plant")
        };

        var result = new List<(TbActivityTopic Topic, SeedTopicTemplate Template)>();

        foreach (var target in levelTargets)
        {
            for (int i = 0; i < topicTemplates.Count; i++)
            {
                var tpl = topicTemplates[i];
                var chosenAg = FindBestActivityGroup(activityGroups, tpl.GroupCode);
                var tgoScope = chosenAg.TgoScopeNo;
                var chosenSource = FindBestSource(masterSources, tpl.SourceKeywords, tgoScope);
                var matchedEgId = chosenSource.GroupId > 0 ? chosenSource.GroupId : defaultEgId;

                var topicTitle = $"{tpl.Title} [{target.Prefix}]";
                var topic = await context.Topics.FirstOrDefaultAsync(t =>
                    t.SubscriptionId == subId &&
                    t.Level == target.Level &&
                    t.TargetId == target.TargetId &&
                    t.Title == topicTitle &&
                    !t.IsDeleted);

                if (topic == null)
                {
                    topic = new TbActivityTopic
                    {
                        SubscriptionId = subId,
                        Level = target.Level,
                        TargetId = target.TargetId,
                        EmissionGroupId = matchedEgId,
                        Title = topicTitle,
                        Remark = $"{target.Prefix} - Standard GHG Inventory Activity",
                        OrderId = i + 1,
                        IsActive = true,
                        IsDeleted = false
                    };

                    await context.Topics.AddAsync(topic);
                    await context.SaveChangesAsync("System");

                    var masterChild = new TbActivityTopicChild
                    {
                        TopicId = topic.Id,
                        ActivityGroupId = chosenAg.Id,
                        SourceId = chosenSource.Id,
                        UnitConversionRatio = 1.0m,
                        EmissionSharePercent = 100.0f,
                        IsActive = true,
                        IsDeleted = false
                    };
                    await context.TopicChilds.AddAsync(masterChild);
                    await context.SaveChangesAsync("System");
                }

                result.Add((topic, tpl));
            }
        }

        logger.LogInformation("Seeded/Verified {Count} Topics with Emission Mappings.", result.Count);
        return result;
    }

    public static async Task<int> SeedTopicEventsAsync(
        ApplicationDbContext context,
        int subId,
        List<(TbActivityTopic Topic, SeedTopicTemplate Template)> topicTuples,
        ILogger logger)
    {
        logger.LogInformation("Seeding Activity Events for Topics...");
        int totalEventsCreated = 0;

        for (int i = 0; i < topicTuples.Count; i++)
        {
            var (topic, tpl) = topicTuples[i];

            // Check if events already exist for this topic
            var hasEvents = await context.Events.AnyAsync(e => e.TopicId == topic.Id);
            if (hasEvents) continue;

            // Generate Monthly Events (12 for 2025, 8 for 2026)
            for (int m = 1; m <= 12; m++)
            {
                decimal variation = ((i * 17 + m * 29) % 20 - 10) / 100m;
                decimal qty = Math.Round(tpl.MonthlyBaseQty * (1m + variation), 2);

                var evt = new TbActivityEvent
                {
                    TopicId = topic.Id,
                    SubscriptionId = subId,
                    Quantity = qty,
                    TimeStamp = new DateTime(2025, m, 15, 0, 0, 0, DateTimeKind.Utc),
                    Remark = $"บันทึกข้อมูลกิจกรรมประจำเดือน 2025/{m:D2}"
                };
                await context.Events.AddAsync(evt);
                totalEventsCreated++;
            }

            for (int m = 1; m <= 8; m++)
            {
                decimal variation = ((i * 19 + m * 31) % 20 - 10) / 100m;
                decimal qty = Math.Round(tpl.MonthlyBaseQty * (1m + variation), 2);

                var evt = new TbActivityEvent
                {
                    TopicId = topic.Id,
                    SubscriptionId = subId,
                    Quantity = qty,
                    TimeStamp = new DateTime(2026, m, 15, 0, 0, 0, DateTimeKind.Utc),
                    Remark = $"บันทึกข้อมูลกิจกรรมประจำเดือน 2026/{m:D2}"
                };
                await context.Events.AddAsync(evt);
                totalEventsCreated++;
            }
        }

        if (totalEventsCreated > 0)
        {
            await context.SaveChangesAsync("System");
            logger.LogInformation("Seeded {Count} monthly Activity Events.", totalEventsCreated);
        }

        return totalEventsCreated;
    }

    public static async Task SeedOrganizationStructureAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-09-13: Antigravity] Seed 1 Corporate, 1 Company, 3 Facilities
        logger.LogInformation("Starting Organization structure seeding (1 Corporate -> 1 Company -> 3 Facilities)...");
        try
        {
            // 1. ตรวจสอบหรือสร้าง Subscription เริ่มต้น
            var sub = await context.Subscriptions.FirstOrDefaultAsync(s => s.Id == 1 && !s.IsDeleted)
                      ?? await context.Subscriptions.FirstOrDefaultAsync(s => !s.IsDeleted);

            if (sub == null)
            {
                sub = new TbSubscription
                {
                    Title = "Global Sustainability Organization Subscription",
                    OwnerEmail = "admin@acamp.com",
                    SubscriptionPlan = eSubscriptionPlan.Pro,
                    AllowCorporateCount = 10,
                    AllowCorporateUserCount = 20,
                    IsActive = true,
                    IsDeleted = false
                };
                await context.Subscriptions.AddAsync(sub);
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded default Subscription: '{Title}'.", sub.Title);
            }
            int subId = sub.Id;

            // 2. โหลดค่าโครงสร้างองค์กรจาก system_organization_structure.json หรือใช้ค่า Fallback
            // [2026-09-13: Antigravity] ปรับข้อมูลโครงสร้างองค์กรเป็นบริษัทจำลอง (Demo Company) ป้องกันการสับสนกับข้อมูลจริง
            var orgJsonPath = GetJsonFilePath("system_organization_structure.json");
            string corpCode = "CRP-001", corpTitle = "บริษัท กรีน อินโนวา โฮลดิ้ง จำกัด (มหาชน)", corpRemark = "องค์กรระดับรวมงบ (Corporate Level - Demo)";
            decimal corpThreshold = 5.0m;
            string compCode = "COM-001", compTitle = "บริษัท กรีนเทค แมนูแฟคเจอริ่ง (ประเทศไทย) จำกัด", compRemark = "นิติบุคคลผู้รายงาน (Company Level - Demo) - Equity Share 100%";
            int compEquity = 100;

            var facilityDefs = new List<(string Code, string Title, string Remark)>
            {
                ("FAC-001", "โรงงานสมาร์ทอีโค ชลบุรี", "โรงงานผลิตและประกอบหลัก (Facility Level - Demo)"),
                ("FAC-002", "โรงงานแปรรูปวัตถุดิบสะอาด ระยอง", "โรงงานแปรรูปวัตถุดิบ (Facility Level - Demo)"),
                ("FAC-003", "ศูนย์กระจายสินค้ากรีนโลจิสติกส์ อยุธยา", "ศูนย์คลังสินค้าและกระจายสินค้า (Facility Level - Demo)")
            };

            if (File.Exists(orgJsonPath))
            {
                try
                {
                    var orgJson = await File.ReadAllTextAsync(orgJsonPath);
                    var orgArray = JsonNode.Parse(orgJson, null, JsonOptions)?.AsArray();
                    var orgItem = orgArray?.FirstOrDefault(n => ReadInt(n?["SubscriptionId"], 1) == subId) ?? orgArray?.FirstOrDefault();
                    if (orgItem is JsonObject orgObj)
                    {
                        if (orgObj["Corporate"] is JsonObject cObj)
                        {
                            corpCode = cObj["Code"]?.ToString() ?? corpCode;
                            corpTitle = cObj["Title"]?.ToString() ?? corpTitle;
                            corpRemark = cObj["Remark"]?.ToString() ?? corpRemark;
                            corpThreshold = ReadDecimal(cObj["MaterialityThreshold"], corpThreshold);
                        }
                        if (orgObj["Company"] is JsonObject compObj)
                        {
                            compCode = compObj["Code"]?.ToString() ?? compCode;
                            compTitle = compObj["Title"]?.ToString() ?? compTitle;
                            compRemark = compObj["Remark"]?.ToString() ?? compRemark;
                            compEquity = ReadInt(compObj["EquityShare"], compEquity);
                        }
                        if (orgObj["Facilities"] is JsonArray facArr && facArr.Count > 0)
                        {
                            facilityDefs.Clear();
                            foreach (var item in facArr)
                            {
                                if (item is JsonObject fObj)
                                {
                                    var fCode = fObj["Code"]?.ToString() ?? $"FAC-{facilityDefs.Count + 1:D3}";
                                    var fTitle = fObj["Title"]?.ToString() ?? $"Facility {facilityDefs.Count + 1}";
                                    var fRemark = fObj["Remark"]?.ToString() ?? string.Empty;
                                    facilityDefs.Add((fCode, fTitle, fRemark));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to load system_organization_structure.json. Using fallback values.");
                }
            }

            // 3. Seed Corporate (1 Corporate)
            var corp = await context.Corporates.FirstOrDefaultAsync(c => c.SubscriptionId == subId && !c.IsDeleted);
            if (corp == null)
            {
                corp = new TbCorporate
                {
                    SubscriptionId = subId,
                    Code = corpCode,
                    Title = corpTitle,
                    Remark = corpRemark,
                    MaterialityThreshold = corpThreshold,
                    IsActive = true,
                    IsDeleted = false
                };
                corp.EnsureKeyIdGenerated();
                await context.Corporates.AddAsync(corp);
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded Corporate '{Title}' ({Code}).", corp.Title, corp.Code);
            }

            // 4. Seed Company (1 Company)
            var comp = await context.Companies.FirstOrDefaultAsync(c => c.CorporateId == corp.Id && !c.IsDeleted);
            if (comp == null)
            {
                comp = new TbCompany
                {
                    CorporateId = corp.Id,
                    Code = compCode,
                    Title = compTitle,
                    EquityShare = compEquity,
                    Remark = compRemark,
                    IsActive = true,
                    IsDeleted = false
                };
                comp.EnsureKeyIdGenerated();
                await context.Companies.AddAsync(comp);
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded Company '{Title}' ({Code}).", comp.Title, comp.Code);
            }

            // 5. Seed Facilities (3 Facilities under the Company)
            var existingFacilities = await context.Facilities
                .Where(f => f.CompanyId == comp.Id && !f.IsDeleted)
                .ToListAsync();

            int facCreatedCount = 0;
            foreach (var def in facilityDefs)
            {
                if (!existingFacilities.Any(f => f.Code == def.Code || f.Title == def.Title))
                {
                    var fac = new TbFacility
                    {
                        CompanyId = comp.Id,
                        Code = def.Code,
                        Title = def.Title,
                        Remark = def.Remark,
                        IsActive = true,
                        IsDeleted = false
                    };
                    fac.EnsureKeyIdGenerated();
                    await context.Facilities.AddAsync(fac);
                    facCreatedCount++;
                    logger.LogInformation("Seeded Facility '{Title}' ({Code}) under Company '{CompTitle}'.", fac.Title, fac.Code, comp.Title);
                }
            }

            if (facCreatedCount > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} new facilities under Company ID {CompId}.", facCreatedCount, comp.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding organization structure (1 Corporate, 1 Company, 3 Facilities).");
            throw;
        }
        #endregion
    }

    public static async Task SeedFacilityScope1TopicAndEventsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-09-13: Antigravity] Seed Scope 1 Facility Topic พร้อม Monthly Events (2024-01 ถึง 2025-12, Qty: 100)
        logger.LogInformation("Seeding Facility Scope 1 Topic and Monthly Events (2024-01 -> 2025-12, Qty: 100)...");
        try
        {
            // 1. ค้นหา Facility เป้าหมาย (FAC-001 หรือ Facility แรกที่พร้อมใช้งาน)
            var facility = await context.Facilities.FirstOrDefaultAsync(f => f.Code == "FAC-001" && !f.IsDeleted)
                           ?? await context.Facilities.FirstOrDefaultAsync(f => !f.IsDeleted);

            if (facility == null)
            {
                logger.LogWarning("No active Facility found to seed Scope 1 Topic.");
                return;
            }

            // ดึง SubscriptionId ที่เกี่ยวข้อง
            int subId = 1;
            var company = await context.Companies.Include(c => c.Corporate).FirstOrDefaultAsync(c => c.Id == facility.CompanyId);
            if (company?.Corporate != null && company.Corporate.SubscriptionId > 0)
            {
                subId = company.Corporate.SubscriptionId;
            }

            // 2. ค้นหา Activity Group ระดับ Scope 1 (เช่น 0101 การเผาไหม้ที่อยู่กับที่ หรือ Scope 1 อื่นๆ)
            var actGroup = await context.ActivityGroups
                .FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && ag.Code == "0101" && !ag.IsDeleted)
                ?? await context.ActivityGroups
                .FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && !ag.IsDeleted);

            if (actGroup == null)
            {
                logger.LogWarning("Scope 1 ActivityGroup not found in database.");
                return;
            }

            // 3. กำหนด Master Emission Source เป็น Diesel (Id: 15) โดยตรงสำหรับการจำลองข้อมูล
            // [2026-09-13: Antigravity] กำหนดให้ SourceId = 15 (Diesel) โดยตรงตามข้อกำหนดการจำลองข้อมูล
            var emissionSource = await context.EmissionSources
                .Include(s => s.Group)
                .FirstOrDefaultAsync(s => s.Id == 15 && !s.IsDeleted)
                ?? await context.EmissionSources
                .Include(s => s.Group)
                .FirstOrDefaultAsync(s => s.Id > 0 && s.Title == "Diesel" && !s.IsDeleted);

            if (emissionSource == null)
            {
                logger.LogWarning("EmissionSource Id=15 (Diesel) not found in database.");
                return;
            }
            int egId = emissionSource.GroupId > 0 ? emissionSource.GroupId : 1;

            // 4. ตรวจสอบหรือสร้าง Topic ระดับ Facility (1 Topic, Scope 1)
            var topicTitle = "การใช้น้ำมันดีเซลสำหรับเครื่องกำเนิดไฟฟ้าสำรอง (Stationary Diesel Generator)";
            var topic = await context.Topics
                .Include(t => t.Childs)
                .FirstOrDefaultAsync(t => t.Level == eTopicLevel.Facility
                                       && t.TargetId == facility.Id
                                       && t.Title == topicTitle
                                       && !t.IsDeleted);

            if (topic == null)
            {
                topic = new TbActivityTopic
                {
                    SubscriptionId = subId,
                    Level = eTopicLevel.Facility,
                    TargetId = facility.Id,
                    EmissionGroupId = egId,
                    Title = topicTitle,
                    Remark = $"Scope 1 Facility Activity - {facility.Title}",
                    OrderId = 1,
                    IsActive = true,
                    IsDeleted = false
                };

                await context.Topics.AddAsync(topic);
                var entry = context.Entry(topic);
                if (entry.Metadata.FindProperty("TbFacilityId") != null)
                {
                    entry.Property("TbFacilityId").CurrentValue = facility.Id;
                }
                await context.SaveChangesAsync("System");

                // ผูก Master Child ตามมาตรฐานระบบ
                var masterChild = new TbActivityTopicChild
                {
                    TopicId = topic.Id,
                    ActivityGroupId = actGroup.Id,
                    SourceId = emissionSource.Id,
                    UnitConversionRatio = 1.0m,
                    EmissionSharePercent = 100.0f,
                    IsActive = true,
                    IsDeleted = false
                };
                await context.TopicChilds.AddAsync(masterChild);

                // ตรวจสอบ Auto-mapping ตาม TbEmissionMapping (Scope 3 WTT / Biogenic CO2) ตามมาตรฐาน Topic Integrity
                var mapping = await context.EmissionMappings
                    .FirstOrDefaultAsync(m => m.Scope1ChildId == emissionSource.Id && !m.IsDeleted);
                if (mapping != null)
                {
                    if (mapping.Scope3ChildId > 0)
                    {
                        var s3Group = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted) ?? actGroup;
                        var s3Child = new TbActivityTopicChild
                        {
                            TopicId = topic.Id,
                            ActivityGroupId = s3Group.Id,
                            SourceId = mapping.Scope3ChildId,
                            UnitConversionRatio = mapping.UnitConversionRatio ?? 1.0m,
                            EmissionSharePercent = 100.0f,
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.TopicChilds.AddAsync(s3Child);
                    }
                    if (mapping.BiogenicChildId > 0)
                    {
                        var bioGroup = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && !ag.IsDeleted) ?? actGroup;
                        var bioChild = new TbActivityTopicChild
                        {
                            TopicId = topic.Id,
                            ActivityGroupId = bioGroup.Id,
                            SourceId = mapping.BiogenicChildId,
                            UnitConversionRatio = 1.0m,
                            EmissionSharePercent = 100.0f,
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.TopicChilds.AddAsync(bioChild);
                    }
                }
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded Facility Scope 1 Topic: '{Title}' (ID: {TopicId}) for Facility: '{FacTitle}' (ID: {FacId}).", topic.Title, topic.Id, facility.Title, facility.Id);
            }
            else
            {
                // [2026-09-13: Antigravity] หาก Topic เดิมผูกกับ Unknown Source (Id <= 0 หรือ Title Unknown) ให้อัปเดตเป็น Diesel B7 Source และซิงก์ Mapping Childs
                var unknownChildren = topic.Childs.Where(c => !c.IsDeleted && (c.SourceId <= 0 || c.Source?.Title == "Unknown")).ToList();
                if (unknownChildren.Count != 0)
                {
                    foreach (var uc in unknownChildren)
                    {
                        uc.SourceId = emissionSource.Id;
                    }
                    topic.EmissionGroupId = egId;

                    // เติม Mapping Childs หากยังไม่มี
                    var mapping = await context.EmissionMappings
                        .FirstOrDefaultAsync(m => m.Scope1ChildId == emissionSource.Id && !m.IsDeleted);
                    if (mapping != null)
                    {
                        if (mapping.Scope3ChildId > 0 && !topic.Childs.Any(c => !c.IsDeleted && c.SourceId == mapping.Scope3ChildId))
                        {
                            var s3Group = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted) ?? actGroup;
                            await context.TopicChilds.AddAsync(new TbActivityTopicChild
                            {
                                TopicId = topic.Id,
                                ActivityGroupId = s3Group.Id,
                                SourceId = mapping.Scope3ChildId,
                                UnitConversionRatio = mapping.UnitConversionRatio ?? 1.0m,
                                EmissionSharePercent = 100.0f,
                                IsActive = true,
                                IsDeleted = false
                            });
                        }
                        if (mapping.BiogenicChildId > 0 && !topic.Childs.Any(c => !c.IsDeleted && c.SourceId == mapping.BiogenicChildId))
                        {
                            var bioGroup = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && !ag.IsDeleted) ?? actGroup;
                            await context.TopicChilds.AddAsync(new TbActivityTopicChild
                            {
                                TopicId = topic.Id,
                                ActivityGroupId = bioGroup.Id,
                                SourceId = mapping.BiogenicChildId,
                                UnitConversionRatio = 1.0m,
                                EmissionSharePercent = 100.0f,
                                IsActive = true,
                                IsDeleted = false
                            });
                        }
                    }
                    await context.SaveChangesAsync("System");
                    logger.LogInformation("Updated Facility Scope 1 Topic ID {TopicId} from Unknown to Source ID {SourceId} ({Title}).", topic.Id, emissionSource.Id, emissionSource.Title);
                }
            }

            // 5. สร้าง Activity Events ทุกเดือนตั้งแต่ 2024-01 ถึง 2025-12 ปริมาณ 100 เดือนละ 1 ครั้ง
            int eventsCreated = 0;
            for (int y = 2024; y <= 2025; y++)
            {
                for (int m = 1; m <= 12; m++)
                {
                    var eventDate = new DateTime(y, m, 15, 0, 0, 0, DateTimeKind.Utc);
                    var monthStart = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
                    var monthEnd = monthStart.AddMonths(1);

                    var exists = await context.Events.AnyAsync(e =>
                        e.TopicId == topic.Id &&
                        !e.IsDeleted &&
                        e.TimeStamp >= monthStart &&
                        e.TimeStamp < monthEnd);

                    if (!exists)
                    {
                        var evt = new TbActivityEvent
                        {
                            TopicId = topic.Id,
                            SubscriptionId = subId,
                            Quantity = 100m,
                            TimeStamp = eventDate,
                            Remark = $"Monthly Scope 1 Activity Event {y}/{m:D2} (Quantity: 100)",
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.Events.AddAsync(evt);
                        eventsCreated++;
                    }
                }
            }

            if (eventsCreated > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} monthly activity events (Qty: 100) for Topic ID {TopicId} from 2024-01 to 2025-12.", eventsCreated, topic.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding Facility Scope 1 Topic and Monthly Events.");
            throw;
        }
        #endregion
    }

    public static async Task SeedFacilityScope2TopicAndEventsAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-09-13: Antigravity] Seed Scope 2 Facility Topic พร้อม Monthly Events (2024-01 ถึง 2025-12, Qty: 1000)
        logger.LogInformation("Seeding Facility Scope 2 Topic and Monthly Events (2024-01 -> 2025-12, Qty: 1000)...");
        try
        {
            // 1. ค้นหา Facility เป้าหมาย (FAC-001 หรือ Facility แรกที่พร้อมใช้งาน)
            var facility = await context.Facilities.FirstOrDefaultAsync(f => f.Code == "FAC-001" && !f.IsDeleted)
                           ?? await context.Facilities.FirstOrDefaultAsync(f => !f.IsDeleted);

            if (facility == null)
            {
                logger.LogWarning("No active Facility found to seed Scope 2 Topic.");
                return;
            }

            // ดึง SubscriptionId ที่เกี่ยวข้อง
            int subId = 1;
            var company = await context.Companies.Include(c => c.Corporate).FirstOrDefaultAsync(c => c.Id == facility.CompanyId);
            if (company?.Corporate != null && company.Corporate.SubscriptionId > 0)
            {
                subId = company.Corporate.SubscriptionId;
            }

            // 2. ค้นหา Activity Group ระดับ Scope 2 (รหัส 0201 การใช้ไฟฟ้าที่ซื้อมา / Purchased Electricity)
            var actGroup = await context.ActivityGroups
                .FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && ag.Code == "0201" && !ag.IsDeleted)
                ?? await context.ActivityGroups
                .FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && !ag.IsDeleted);

            if (actGroup == null)
            {
                logger.LogWarning("Scope 2 ActivityGroup not found in database.");
                return;
            }

            // 3. กำหนด Master Emission Source เป็น ไฟฟ้า Grid Mix (Id: 131 - grid mix ปี 2022-2024 หรือ Id: 128)
            var emissionSource = await context.EmissionSources
                .Include(s => s.Group)
                .FirstOrDefaultAsync(s => s.Id == 131 && !s.IsDeleted)
                ?? await context.EmissionSources
                .Include(s => s.Group)
                .FirstOrDefaultAsync(s => s.Id == 128 && !s.IsDeleted)
                ?? await context.EmissionSources
                .Include(s => s.Group)
                .FirstOrDefaultAsync(s => s.Id > 0 && s.Title.Contains("ไฟฟ้าแบบ grid mix") && !s.IsDeleted);

            if (emissionSource == null)
            {
                logger.LogWarning("EmissionSource Id=131/128 (Purchased Electricity) not found in database.");
                return;
            }
            int egId = emissionSource.GroupId > 0 ? emissionSource.GroupId : 7;

            // 4. ตรวจสอบหรือสร้าง Topic ระดับ Facility (1 Topic, Scope 2)
            var topicTitle = "การใช้พลังงานไฟฟ้าจากโครงข่าย (Purchased Grid Electricity)";
            var topic = await context.Topics
                .Include(t => t.Childs)
                .FirstOrDefaultAsync(t => t.Level == eTopicLevel.Facility
                                       && t.TargetId == facility.Id
                                       && t.Title == topicTitle
                                       && !t.IsDeleted);

            if (topic == null)
            {
                topic = new TbActivityTopic
                {
                    SubscriptionId = subId,
                    Level = eTopicLevel.Facility,
                    TargetId = facility.Id,
                    EmissionGroupId = egId,
                    Title = topicTitle,
                    Remark = $"Scope 2 Facility Activity - {facility.Title}",
                    OrderId = 2,
                    IsActive = true,
                    IsDeleted = false
                };

                await context.Topics.AddAsync(topic);
                var entry = context.Entry(topic);
                if (entry.Metadata.FindProperty("TbFacilityId") != null)
                {
                    entry.Property("TbFacilityId").CurrentValue = facility.Id;
                }
                await context.SaveChangesAsync("System");

                // ผูก Master Scope 2 Child ตามมาตรฐานระบบ
                var masterChild = new TbActivityTopicChild
                {
                    TopicId = topic.Id,
                    ActivityGroupId = actGroup.Id,
                    SourceId = emissionSource.Id,
                    UnitConversionRatio = 1.0m,
                    EmissionSharePercent = 100.0f,
                    IsActive = true,
                    IsDeleted = false
                };
                await context.TopicChilds.AddAsync(masterChild);

                // ตรวจสอบ Auto-mapping ตาม TbEmissionMapping (Scope 3 WTT / Biogenic CO2) ตามมาตรฐาน Topic Integrity
                var mapping = await context.EmissionMappings
                    .FirstOrDefaultAsync(m => m.Scope2ChildId == emissionSource.Id && !m.IsDeleted);
                if (mapping != null)
                {
                    if (mapping.Scope3ChildId > 0)
                    {
                        // [2026-09-13: Antigravity] การได้มาซึ่งเชื้อเพลิง/พลังงาน (WTT) จัดอยู่ใน Cat 0303 (Fuel- and Energy-Related Activities) ตามมาตรฐาน TGO/GHG Protocol
                        var s3Group = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && ag.Code == "0303" && !ag.IsDeleted)
                                      ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted)
                                      ?? actGroup;
                        var s3Child = new TbActivityTopicChild
                        {
                            TopicId = topic.Id,
                            ActivityGroupId = s3Group.Id,
                            SourceId = mapping.Scope3ChildId,
                            UnitConversionRatio = mapping.UnitConversionRatio ?? 1.0m,
                            EmissionSharePercent = 100.0f,
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.TopicChilds.AddAsync(s3Child);
                    }
                    if (mapping.BiogenicChildId > 0)
                    {
                        var bioGroup = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && !ag.IsDeleted) ?? actGroup;
                        var bioChild = new TbActivityTopicChild
                        {
                            TopicId = topic.Id,
                            ActivityGroupId = bioGroup.Id,
                            SourceId = mapping.BiogenicChildId,
                            UnitConversionRatio = 1.0m,
                            EmissionSharePercent = 100.0f,
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.TopicChilds.AddAsync(bioChild);
                    }
                }
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded Facility Scope 2 Topic: '{Title}' (ID: {TopicId}) for Facility: '{FacTitle}' (ID: {FacId}).", topic.Title, topic.Id, facility.Title, facility.Id);
            }
            else
            {
                // หาก Topic เดิมผูกกับ Unknown Source (Id <= 0 หรือ Title Unknown) ให้อัปเดตเป็น Electricity Grid Mix Source และซิงก์ Mapping Childs
                var unknownChildren = topic.Childs.Where(c => !c.IsDeleted && (c.SourceId <= 0 || c.Source?.Title == "Unknown")).ToList();
                if (unknownChildren.Count != 0)
                {
                    foreach (var uc in unknownChildren)
                    {
                        uc.SourceId = emissionSource.Id;
                    }
                    topic.EmissionGroupId = egId;

                    // เติม Mapping Childs หากยังไม่มี
                    var mapping = await context.EmissionMappings
                        .FirstOrDefaultAsync(m => m.Scope2ChildId == emissionSource.Id && !m.IsDeleted);
                    if (mapping != null)
                    {
                        if (mapping.Scope3ChildId > 0 && !topic.Childs.Any(c => !c.IsDeleted && c.SourceId == mapping.Scope3ChildId))
                        {
                            var s3Group = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && ag.Code == "0303" && !ag.IsDeleted)
                                          ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted)
                                          ?? actGroup;
                            await context.TopicChilds.AddAsync(new TbActivityTopicChild
                            {
                                TopicId = topic.Id,
                                ActivityGroupId = s3Group.Id,
                                SourceId = mapping.Scope3ChildId,
                                UnitConversionRatio = mapping.UnitConversionRatio ?? 1.0m,
                                EmissionSharePercent = 100.0f,
                                IsActive = true,
                                IsDeleted = false
                            });
                        }
                        if (mapping.BiogenicChildId > 0 && !topic.Childs.Any(c => !c.IsDeleted && c.SourceId == mapping.BiogenicChildId))
                        {
                            var bioGroup = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && !ag.IsDeleted) ?? actGroup;
                            await context.TopicChilds.AddAsync(new TbActivityTopicChild
                            {
                                TopicId = topic.Id,
                                ActivityGroupId = bioGroup.Id,
                                SourceId = mapping.BiogenicChildId,
                                UnitConversionRatio = 1.0m,
                                EmissionSharePercent = 100.0f,
                                IsActive = true,
                                IsDeleted = false
                            });
                        }
                    }
                    await context.SaveChangesAsync("System");
                    logger.LogInformation("Updated Facility Scope 2 Topic ID {TopicId} from Unknown to Source ID {SourceId} ({Title}).", topic.Id, emissionSource.Id, emissionSource.Title);
                }
            }

            // 5. สร้าง Activity Events ทุกเดือนตั้งแต่ 2024-01 ถึง 2025-12 ปริมาณ 1000 kWh เดือนละ 1 ครั้ง
            int eventsCreated = 0;
            for (int y = 2024; y <= 2025; y++)
            {
                for (int m = 1; m <= 12; m++)
                {
                    var eventDate = new DateTime(y, m, 15, 0, 0, 0, DateTimeKind.Utc);
                    var monthStart = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
                    var monthEnd = monthStart.AddMonths(1);

                    var exists = await context.Events.AnyAsync(e =>
                        e.TopicId == topic.Id &&
                        !e.IsDeleted &&
                        e.TimeStamp >= monthStart &&
                        e.TimeStamp < monthEnd);

                    if (!exists)
                    {
                        var evt = new TbActivityEvent
                        {
                            TopicId = topic.Id,
                            SubscriptionId = subId,
                            Quantity = 1000m,
                            TimeStamp = eventDate,
                            Remark = $"Monthly Scope 2 Activity Event {y}/{m:D2} (Quantity: 1000 kWh)",
                            IsActive = true,
                            IsDeleted = false
                        };
                        await context.Events.AddAsync(evt);
                        eventsCreated++;
                    }
                }
            }

            if (eventsCreated > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded {Count} monthly activity events (Qty: 1000 kWh) for Topic ID {TopicId} from 2024-01 to 2025-12.", eventsCreated, topic.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding Facility Scope 2 Topic and Monthly Events.");
            throw;
        }
        #endregion
    }

    // [2026-09-19][Antigravity] ปรับเป็น public static ให้ Seeder อื่นเรียกใช้งานได้
    public static async Task SeedDefaultApiKeysAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-08-24]
        try
        {
            var apiKeyJsonPath = GetJsonFilePath("system_api_keys.json");
            if (File.Exists(apiKeyJsonPath))
            {
                var json = await File.ReadAllTextAsync(apiKeyJsonPath);
                var array = JsonNode.Parse(json, null, JsonOptions)?.AsArray();
                if (array != null)
                {
                    foreach (var node in array)
                    {
                        if (node is JsonObject obj)
                        {
                            var subId = ReadInt(obj["SubscriptionId"], 1);
                            var keyName = obj["KeyName"]?.ToString() ?? "Default API Key";
                            var keyPrefix = obj["KeyPrefix"]?.ToString() ?? "acamp_api_onecs";
                            var keyHash = obj["KeyHash"]?.ToString() ?? "f0102610d471797507e2fd679799760c7bb7b5292c448d71287a0b6af8bcdae7";
                            var isActive = ReadBool(obj["IsActive"], true);

                            var defaultKey = await context.ApiKeys.FirstOrDefaultAsync(k => k.KeyPrefix == keyPrefix);
                            if (defaultKey == null)
                            {
                                defaultKey = new TbApiKey
                                {
                                    SubscriptionId = subId,
                                    KeyName = keyName,
                                    KeyPrefix = keyPrefix,
                                    KeyHash = keyHash,
                                    IsActive = isActive,
                                    CreatedAt = DateTime.UtcNow
                                };

                                await context.ApiKeys.AddAsync(defaultKey);
                                await context.SaveChangesAsync("System");
                                logger.LogInformation("Seeded API Key '{Prefix}' from JSON for Subscription ID {SubId}.", keyPrefix, subId);
                            }
                            else if (defaultKey.KeyHash != keyHash)
                            {
                                defaultKey.KeyHash = keyHash;
                                await context.SaveChangesAsync("System");
                                logger.LogInformation("Updated KeyHash for API Key {Prefix} from JSON.", keyPrefix);
                            }
                        }
                    }
                    return;
                }
            }

            var correctHash = "f0102610d471797507e2fd679799760c7bb7b5292c448d71287a0b6af8bcdae7";
            var existingKey = await context.ApiKeys.FirstOrDefaultAsync(k => k.KeyPrefix == "acamp_api_onecs");
            if (existingKey == null)
            {
                existingKey = new TbApiKey
                {
                    SubscriptionId = 1,
                    KeyName = "Default Workspace Integration Key",
                    KeyPrefix = "acamp_api_onecs",
                    KeyHash = correctHash,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                await context.ApiKeys.AddAsync(existingKey);
                await context.SaveChangesAsync("System");
                logger.LogInformation("Seeded default API Key for Subscription ID 1.");
            }
            else if (existingKey.KeyHash != correctHash)
            {
                existingKey.KeyHash = correctHash;
                await context.SaveChangesAsync("System");
                logger.LogInformation("Updated KeyHash for default API Key acamp_api_onecs.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding default API Keys.");
        }
        #endregion
    }
    #endregion
}
