using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;

namespace TU.ACAMP.WebApp.Data;

// [2026-09-14][Gemini] สร้าง DatabaseSeeder3 สำหรับทดสอบ UI ในการจัดการส่วนงาน องค์กร (Corporate, Company, Facility)
// โดยสร้างข้อมูลแบบจำลองในโค้ด C# โดยตรง ไม่ต้องสร้างไฟล์ JSON (5 Corporates, แต่ละ Corp มีสุ่ม 3-15 Companies, แต่ละ Company มีสุ่ม 1-8 Facilities)
public static class DatabaseSeeder3
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
        // [2026-09-14][Gemini] เริ่มกระบวนการ Seeding ข้อมูลชุดทดสอบ UI องค์กรเวอร์ชัน 3
        logger.LogInformation("Starting database seeding process version 3 (Organization UI Stress Testing)...");
        try
        {
            await SeedRolesAsync(context, logger);
            await SeedUsersAsync(context, logger);
            await SeedUserRolesAsync(context, logger);
            await SeedSubscriptionsAsync(context, logger);
            await SeedSubscriptionUsersAsync(context, logger);

            // Seed Library / Master Data
            await SeedEmissionUnitsAsync(context, logger);
            await SeedEmissionGroupsAsync(context, logger);
            await SeedActivityGroupsAsync(context, logger);
            await SeedEmissionSourcesAsync(context, logger);
            await SeedEmissionMappingsAsync(context, logger);

            // Seed Organization Structure (5 Corporates -> Random 3-15 Companies -> Random 1-8 Facilities)
            await SeedOrganizationStructureAsync(context, logger);

            // Seed Facility Scope 2 Topic และ Events ให้ Facility แรก เพื่อให้ Dashboard/Report ยังคงมีข้อมูลทดสอบ
            await SeedFacilityScope2TopicAndEventsAsync(context, logger);

            await SeedDefaultApiKeysAsync(context, logger);

            logger.LogInformation("DatabaseSeeder3 completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during DatabaseSeeder3 seeding.");
            throw;
        }
    }

    // [2026-09-14][Gemini] ค้นหาพาธไฟล์ JSON แบบยืดหยุ่น รองรับการรันจากทั้ง IDE, dotnet cli, หรือ bin directory และรองรับชื่อ alias
    private static string GetJsonFilePath(string fileName)
    {
        var targetFileNames = new List<string> { fileName };
        if (fileName.Equals("system_users_roles.json", StringComparison.OrdinalIgnoreCase))
            targetFileNames.Add("system_user_roles.json");
        else if (fileName.Equals("system_user_roles.json", StringComparison.OrdinalIgnoreCase))
            targetFileNames.Add("system_users_roles.json");

        if (fileName.Equals("system_subscriptions_users.json", StringComparison.OrdinalIgnoreCase))
            targetFileNames.Add("system_subscription_users.json");
        else if (fileName.Equals("system_subscription_users.json", StringComparison.OrdinalIgnoreCase))
            targetFileNames.Add("system_subscriptions_users.json");

        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        // ไต่ระดับไดเรกทอรีขึ้นไปหา root project (สูงสุด 5 ชั้น)
        foreach (var root in searchRoots.ToList())
        {
            var dir = new DirectoryInfo(root);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                searchRoots.Add(dir.FullName);
                dir = dir.Parent;
            }
        }

        var relativeFolderPatterns = new[]
        {
            Path.Combine("Files", "JSON"),
            Path.Combine("Files", "json"),
            Path.Combine("TU.ACAMP.WebApp", "Files", "JSON"),
            Path.Combine("TU.ACAMP.WebApp", "Files", "json")
        };

        foreach (var fn in targetFileNames)
        {
            foreach (var root in searchRoots)
            {
                foreach (var rel in relativeFolderPatterns)
                {
                    var fullPath = Path.Combine(root, rel, fn);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "Files", "JSON", fileName);
    }

    private static bool ReadBool(JsonNode? node, bool defaultValue = false)
    {
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
    }

    private static int ReadInt(JsonNode? node, int defaultValue = 0)
    {
        if (node is not JsonValue value) return defaultValue;

        if (value.TryGetValue<int>(out var intResult)) return intResult;
        if (value.TryGetValue<long>(out var longResult)) return (int)longResult;
        if (value.TryGetValue<decimal>(out var decResult)) return (int)decResult;

        return int.TryParse(value.ToString().Trim(), out var parsed) ? parsed : defaultValue;
    }

    private static decimal ReadDecimal(JsonNode? node, decimal defaultValue = 0m)
    {
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
    }

    private static async Task SeedRolesAsync(ApplicationDbContext context, ILogger logger)
    {
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
    }

    private static async Task SeedUsersAsync(ApplicationDbContext context, ILogger logger)
    {
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
    }

    private static async Task SeedUserRolesAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding UserRoles from system_user_roles.json...");
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
                        var userExists = await context.Users.AnyAsync(u => u.Id == userId);
                        var roleExists = await context.Roles.AnyAsync(r => r.Id == roleId);

                        if (userExists && roleExists)
                        {
                            var userRoleExists = await context.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
                            if (!userRoleExists)
                            {
                                await context.UserRoles.AddAsync(new IdentityUserRole<string>
                                {
                                    UserId = userId,
                                    RoleId = roleId
                                });
                                count++;
                            }
                        }
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Seeded {Count} User-Role relationships.", count);
            }
        }
    }

    private static async Task SeedSubscriptionsAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding Subscriptions...");
        var sub = await context.Subscriptions.FirstOrDefaultAsync(s => s.Id == 1 && !s.IsDeleted)
                  ?? await context.Subscriptions.FirstOrDefaultAsync(s => !s.IsDeleted);

        if (sub == null)
        {
            sub = new TbSubscription
            {
                Id = 1,
                Title = "Enterprise Global Sustainability Subscription",
                TaxId = "0105558000001",
                OwnerName = "ACAMP Enterprise Demo Tenant",
                OwnerEmail = "admin@acamp.com",
                OwnerPhone = "02-123-4567",
                ContactPerson = "System Administrator",
                BillingAddress = "ACAMP Headquarter, Bangkok, Thailand",
                SubscriptionPlan = eSubscriptionPlan.Enterprise,
                OwnerType = eSubscriptionOwnerType.Juristic,
                StartDate = DateTime.UtcNow.AddMonths(-6),
                ExpiryDate = DateTime.UtcNow.AddYears(2),
                AllowCorporateCount = 25,
                AllowCompanyCount = 150,
                AllowFacilityCount = 1000,
                AllowUserCount = 50,
                IsActive = true,
                IsDeleted = false
            };
            await context.Subscriptions.AddAsync(sub);
            await context.SaveChangesAsync("System");
            logger.LogInformation("Seeded default Enterprise Subscription: '{Title}' with high capacity quotas.", sub.Title);
        }
        else
        {
            // [2026-09-14][Gemini] ปรับโควตา Subscription ให้รองรับการทดสอบ 5 Corporates, 3-15 Companies, 1-8 Facilities
            bool updated = false;
            if (sub.AllowCorporateCount < 25)
            {
                sub.AllowCorporateCount = 25;
                updated = true;
            }
            if (sub.AllowCompanyCount < 150)
            {
                sub.AllowCompanyCount = 150;
                updated = true;
            }
            if (sub.AllowFacilityCount < 1000)
            {
                sub.AllowFacilityCount = 1000;
                updated = true;
            }
            if (sub.SubscriptionPlan != eSubscriptionPlan.Enterprise)
            {
                sub.SubscriptionPlan = eSubscriptionPlan.Enterprise;
                updated = true;
            }

            if (updated)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Updated Subscription ID {SubId} quotas to Enterprise limits (Corp: 25, Comp: 150, Fac: 1000).", sub.Id);
            }
        }
    }

    private static async Task SeedSubscriptionUsersAsync(ApplicationDbContext context, ILogger logger)
    {
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
            var count = 0;
            foreach (var item in subUsersArray)
            {
                if (item is JsonObject obj)
                {
                    var id = ReadInt(obj["Id"]);
                    var subId = ReadInt(obj["SubscriptionId"]);
                    var email = obj["Email"]?.ToString() ?? string.Empty;
                    var remarks = obj["Remarks"]?.ToString() ?? string.Empty;
                    var roleEnum = eSubscriptionRole.Viewer;
                    if (obj["Role"] != null)
                    {
                        Enum.TryParse<eSubscriptionRole>(obj["Role"]?.ToString(), true, out roleEnum);
                    }

                    var isActive = true;
                    if (obj["IsActive"] != null)
                    {
                        bool.TryParse(obj["IsActive"]?.ToString(), out isActive);
                    }

                    if (subId > 0 && !string.IsNullOrEmpty(email))
                    {
                        var subExists = await context.Subscriptions.AnyAsync(s => s.Id == subId);
                        if (subExists)
                        {
                            var existingSubUser = await context.SubscriptionUsers.FirstOrDefaultAsync(su => su.SubscriptionId == subId && su.Email == email);
                            if (existingSubUser == null)
                            {
                                var subUser = new TbSubscriptionUser
                                {
                                    SubscriptionId = subId,
                                    Email = email,
                                    Role = roleEnum,
                                    Remarks = remarks,
                                    IsActive = isActive,
                                    IsDeleted = false
                                };
                                await context.SubscriptionUsers.AddAsync(subUser);
                                count++;
                                logger.LogInformation("Seeded Subscription User ({Email}) for Subscription ID {SubId}.", email, subId);
                            }
                        }
                    }
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
            }
        }
    }

    private static async Task SeedEmissionUnitsAsync(ApplicationDbContext context, ILogger logger)
    {
        if (await context.Units.AnyAsync()) return;

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
                }
            }
            if (count > 0)
            {
                await context.SaveChangesAsync("System");
                logger.LogInformation("Synced {Count} Emission Units from JSON.", count);
            }
        }
    }

    private static async Task SeedEmissionGroupsAsync(ApplicationDbContext context, ILogger logger)
    {
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
    }

    private static async Task SeedActivityGroupsAsync(ApplicationDbContext context, ILogger logger)
    {
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
    }

    private static async Task SeedEmissionSourcesAsync(ApplicationDbContext context, ILogger logger)
    {
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
    }

    private static async Task SeedEmissionMappingsAsync(ApplicationDbContext context, ILogger logger)
    {
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

    /// <summary>
    /// [2026-09-14][Gemini] สร้างข้อมูลโครงสร้างองค์กร 5 Corporates ในโค้ดโดยตรง
    /// แต่ละ Corporate สุ่ม 3-15 บริษัท และแต่ละบริษัทสุ่ม 1-8 โรงงาน/สถานที่
    /// </summary>
    public static async Task SeedOrganizationStructureAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Starting code-based Organization Structure seeding (5 Corporates, random 3-15 Companies each, random 1-8 Facilities each)...");
        try
        {
            var sub = await context.Subscriptions.FirstOrDefaultAsync(s => s.Id == 1 && !s.IsDeleted)
                      ?? await context.Subscriptions.FirstOrDefaultAsync(s => !s.IsDeleted);

            if (sub == null)
            {
                logger.LogError("No active subscription found. Skipping organization seeding.");
                return;
            }
            int subId = sub.Id;

            // [2026-09-14][Gemini] กำหนดข้อมูล 5 Corporate Profiles ในโค้ด C# โดยตรง ไม่ต้องสร้างไฟล์ JSON
            var corporateBlueprints = new[]
            {
                new
                {
                    Code = "CRP-001",
                    Title = "Apex Global Energy & Utilities Public Co., Ltd.",
                    Remark = "Clean power, renewable energy, and grid infrastructure group",
                    ShortName = "Apex Energy",
                    CompanyKeywords = new[]
                    {
                        "Renewable Power", "Solar Energy", "Wind Power", "Smart Grid Solutions",
                        "Biomass Energy", "Clean Gas", "Battery Storage", "Hydro Generation",
                        "District Cooling", "Energy Dispatch", "Thermal Systems", "Power Services",
                        "Offshore Wind", "Hydrogen Energy", "Microgrid Tech"
                    }
                },
                new
                {
                    Code = "CRP-002",
                    Title = "Siam Horizon Industrial Materials Group Co., Ltd.",
                    Remark = "Heavy industrial manufacturing, advanced materials, and specialty chemicals",
                    ShortName = "Siam Horizon",
                    CompanyKeywords = new[]
                    {
                        "Specialty Chemicals", "Polymer & Resin", "Composite Materials", "Industrial Coatings",
                        "Green Cement", "Advanced Ceramics", "Steel Fabrication", "Recycled Plastics",
                        "Bio-Chemical Synthetics", "Industrial Gases", "Aggregates & Mineral", "Precision Metallurgy",
                        "Packaging Solutions", "Membrane Filtration", "Surface Engineering"
                    }
                },
                new
                {
                    Code = "CRP-003",
                    Title = "Pacific Agri-Food & Bio-Tech Holdings PLC",
                    Remark = "Sustainable agriculture, food processing, and bio-based nutrition products",
                    ShortName = "Pacific AgriFood",
                    CompanyKeywords = new[]
                    {
                        "Smart Farming", "Grain Processing", "Bio-Nutrient Products", "Dairy & Beverages",
                        "Cold-Chain Logistics", "Aquaculture Innovations", "Plant Protein Labs", "Sugar & Bio-Ethanol",
                        "Feed & Livestock", "Functional Nutrition", "Fruit Extraction", "Packaging & Bottling",
                        "Bio-Fertilizer", "Milling Systems", "Culinary Ingredients"
                    }
                },
                new
                {
                    Code = "CRP-004",
                    Title = "Metro Logistics & Green Supply Chain Public Co., Ltd.",
                    Remark = "Intermodal transportation, multi-temperature warehousing, and green transport fleets",
                    ShortName = "Metro Logistics",
                    CompanyKeywords = new[]
                    {
                        "Intermodal Freight", "Cold Storage Logistics", "Urban Express Delivery", "Maritime Port Services",
                        "Air Freight Forwarding", "Cross-Border Fleet", "Fleet Telematics", "Reverse Logistics",
                        "Automated Fulfillment", "Bulk Liquid Transport", "Customs Brokerage", "Rail Logistics",
                        "Container Depots", "Distribution Centers", "Cargo Terminal Hub"
                    }
                },
                new
                {
                    Code = "CRP-005",
                    Title = "Thai Advanced Technology & Smart Electronics Corp.",
                    Remark = "Microelectronic components, IoT hardware, and smart automation solutions",
                    ShortName = "Thai Advanced Tech",
                    CompanyKeywords = new[]
                    {
                        "Semiconductor Assembly", "Microchip Packaging", "IoT Sensor Tech", "Printed Circuit Fab",
                        "Precision Optics", "Industrial Automation", "Power Electronics", "Smart Metering",
                        "Telecom Hardware", "Embedded Systems", "Battery Control Tech", "Robotics Assembly",
                        "Automotive Electronics", "Fiber Optic Networks", "Cleanroom Engineering"
                    }
                }
            };

            // พูลสำหรับตั้งชื่อ Facility
            var locations = new[]
            {
                "Bangkok", "Chonburi", "Rayong", "Ayutthaya", "Samut Prakan",
                "Saraburi", "Pathum Thani", "Chachoengsao", "Prachinburi",
                "Chiang Mai", "Khon Kaen", "Songkhla"
            };

            var facilityTypes = new[]
            {
                "Manufacturing Plant", "Assembly Center", "Central Warehouse",
                "Operations Hub", "Processing Unit", "Distribution Center",
                "R&D Technology Center", "Logistics Terminal"
            };

            int totalCorpsCreated = 0;
            int totalCompaniesCreated = 0;
            int totalFacilitiesCreated = 0;

            for (int corpIdx = 0; corpIdx < corporateBlueprints.Length; corpIdx++)
            {
                var bp = corporateBlueprints[corpIdx];
                int corpNumber = corpIdx + 1;

                // ตรวจสอบ Corporate ว่ามีอยู่แล้วหรือไม่
                var corp = await context.Corporates.FirstOrDefaultAsync(c => c.SubscriptionId == subId && c.Code == bp.Code && !c.IsDeleted);
                if (corp == null)
                {
                    corp = new TbCorporate
                    {
                        SubscriptionId = subId,
                        Code = bp.Code,
                        Title = bp.Title,
                        Remark = bp.Remark,
                        MaterialityThreshold = 5.0m,
                        OrderId = corpNumber,
                        IsActive = true,
                        IsDeleted = false
                    };
                    corp.EnsureKeyIdGenerated();
                    await context.Corporates.AddAsync(corp);
                    await context.SaveChangesAsync("System");
                    totalCorpsCreated++;
                    logger.LogInformation("Seeded Corporate #{No}: '{Title}' ({Code})", corpNumber, corp.Title, corp.Code);
                }

                // ตรวจสอบ Company ภายใต้ Corporate นี้
                var existingCompanies = await context.Companies
                    .Where(co => co.CorporateId == corp.Id && !co.IsDeleted)
                    .ToListAsync();

                // สุ่มจำนวนบริษัท 3-15 บริษัทตามข้อกำหนด
                int targetCompanyCount = Random.Shared.Next(3, 16);

                if (existingCompanies.Count < targetCompanyCount)
                {
                    int startIndex = existingCompanies.Count + 1;
                    for (int compIdx = startIndex; compIdx <= targetCompanyCount; compIdx++)
                    {
                        var kw = bp.CompanyKeywords[(compIdx - 1) % bp.CompanyKeywords.Length];
                        var compCode = $"COM-{corpNumber:D2}-{compIdx:D2}";
                        var compTitle = $"{bp.ShortName} {kw} Co., Ltd.";
                        int equity = 100;

                        var company = new TbCompany
                        {
                            CorporateId = corp.Id,
                            Code = compCode,
                            Title = compTitle,
                            EquityShare = equity,
                            Remark = $"{kw} operations subsidiary - Equity {equity}%",
                            OrderId = compIdx,
                            IsActive = true,
                            IsDeleted = false
                        };
                        company.EnsureKeyIdGenerated();
                        await context.Companies.AddAsync(company);
                        await context.SaveChangesAsync("System");
                        existingCompanies.Add(company);
                        totalCompaniesCreated++;
                    }
                }

                // สำหรับแต่ละ Company ตรวจสอบและสร้าง Facilities แบบสุ่ม 1-8 Facility
                foreach (var comp in existingCompanies)
                {
                    var existingFacs = await context.Facilities
                        .Where(f => f.CompanyId == comp.Id && !f.IsDeleted)
                        .ToListAsync();

                    int targetFacilityCount = Random.Shared.Next(1, 9);

                    if (existingFacs.Count < targetFacilityCount)
                    {
                        int startFacIndex = existingFacs.Count + 1;
                        for (int fIdx = startFacIndex; fIdx <= targetFacilityCount; fIdx++)
                        {
                            var loc = locations[(corpIdx + comp.OrderId + fIdx) % locations.Length];
                            var fType = facilityTypes[(fIdx - 1) % facilityTypes.Length];
                            var facCode = $"{comp.Code.Replace("COM-", "FAC-")}-{fIdx:D2}";
                            var facTitle = $"{comp.Title.Replace(" Co., Ltd.", "")} - {loc} {fType}";

                            var facility = new TbFacility
                            {
                                CompanyId = comp.Id,
                                Code = facCode,
                                Title = facTitle,
                                Remark = $"Primary {fType.ToLowerInvariant()} facility in {loc}",
                                OrderId = fIdx,
                                IsActive = true,
                                IsDeleted = false
                            };
                            facility.EnsureKeyIdGenerated();
                            await context.Facilities.AddAsync(facility);
                            totalFacilitiesCreated++;
                        }
                    }
                }

                await context.SaveChangesAsync("System");
            }

            logger.LogInformation("Organization Seeding Summary: Created {Corps} Corporates, {Comps} Companies, {Facs} Facilities.",
                totalCorpsCreated, totalCompaniesCreated, totalFacilitiesCreated);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during SeedOrganizationStructureAsync in DatabaseSeeder3.");
            throw;
        }
    }

    public static async Task SeedFacilityScope2TopicAndEventsAsync(ApplicationDbContext context, ILogger logger)
    {
        try
        {
            var facility = await context.Facilities.FirstOrDefaultAsync(f => !f.IsDeleted);
            if (facility == null) return;

            var sub = await context.Subscriptions.FirstOrDefaultAsync(s => !s.IsDeleted);
            if (sub == null) return;
            int subId = sub.Id;

            var emissionSource = await context.EmissionSources
                .Include(s => s.Group)
                .Include(s => s.Unit)
                .FirstOrDefaultAsync(s => s.Group != null && s.Group.TgoScopeNo == eTgoScopeNo.Scope2 && !s.IsDeleted)
                ?? await context.EmissionSources.FirstOrDefaultAsync(s => !s.IsDeleted);

            if (emissionSource == null) return;

            int egId = emissionSource.GroupId;
            int unitId = emissionSource.UnitId;

            var actGroup = await context.ActivityGroups
                .FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && !ag.IsDeleted)
                ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => !ag.IsDeleted);

            if (actGroup == null) return;

            string topicTitle = "Grid Electricity Consumption (Facility Level - Scope 2)";
            var topic = await context.Topics
                .Include(t => t.Childs)
                .FirstOrDefaultAsync(t => t.TargetId == facility.Id && t.Level == eTopicLevel.Facility && t.Title == topicTitle && !t.IsDeleted);

            if (topic == null)
            {
                topic = new TbActivityTopic
                {
                    SubscriptionId = subId,
                    TargetId = facility.Id,
                    Level = eTopicLevel.Facility,
                    Title = topicTitle,
                    Remark = "Purchased grid electricity for main facility operations",
                    EmissionGroupId = egId,
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

                var primaryChild = new TbActivityTopicChild
                {
                    TopicId = topic.Id,
                    ActivityGroupId = actGroup.Id,
                    SourceId = emissionSource.Id,
                    UnitConversionRatio = 1.0m,
                    EmissionSharePercent = 100.0f,
                    IsActive = true,
                    IsDeleted = false
                };
                await context.TopicChilds.AddAsync(primaryChild);

                var mapping = await context.EmissionMappings
                    .FirstOrDefaultAsync(m => m.Scope2ChildId == emissionSource.Id && !m.IsDeleted);

                if (mapping != null)
                {
                    if (mapping.Scope3ChildId > 0)
                    {
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
                }
                await context.SaveChangesAsync("System");
            }

            // Seed sample events for 2024 - 2025
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
                            Remark = $"Monthly Scope 2 Activity Event {y}/{m:D2} (Qty: 1000 kWh)",
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
                logger.LogInformation("Seeded {Count} sample monthly Scope 2 activity events for Topic ID {TopicId}.", eventsCreated, topic.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Non-critical: Failed to seed sample facility topic/events.");
        }
    }

    private static async Task SeedDefaultApiKeysAsync(ApplicationDbContext context, ILogger logger)
    {
        try
        {
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
                logger.LogInformation("Seeded default API Key acamp_api_onecs.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Non-critical: Error seeding default API Keys.");
        }
    }
    #endregion
}
