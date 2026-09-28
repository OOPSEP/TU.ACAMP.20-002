using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Radzen;
using TU.ACAMP.WebApp.Components;
using TU.ACAMP.WebApp.Components.Account;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Emissions;
using TU.ACAMP.WebApp.Services.Identity;
using TU.ACAMP.WebApp.Services.Common;
using TU.ACAMP.WebApp.Services.Organization;

namespace TU.ACAMP.WebApp.Services;

public static class DependencyInjection
{
    public static WebApplicationBuilder AddApplicationServices(this WebApplicationBuilder builder)
    {
        #region hide [2026-08-15]
        // Blazor Components & Radzen UI
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddRadzenComponents();

        // Database (MySQL via Pomelo)
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
                mysqlOptions =>
                {
                    mysqlOptions.SchemaBehavior(
                        Pomelo.EntityFrameworkCore.MySql.Infrastructure.MySqlSchemaBehavior.Ignore);
                    mysqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                }));

        builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

        // Authentication & Identity State
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        }).AddIdentityCookies();

        // Identity Core
        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.SignIn.RequireConfirmedEmail = true;
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 4;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequiredUniqueChars = 1;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders();

        builder.Services.AddTransient<EmailSender>();
        builder.Services.AddTransient<IEmailSender<ApplicationUser>, EmailSender>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        // Application Business Services
        builder.Services.AddScoped<SubscriptionService>();
        builder.Services.AddScoped<IdentityAdminService>();
        builder.Services.AddScoped<CorporateService>();
        builder.Services.AddScoped<CorporateUserService>();
        builder.Services.AddScoped<CompanyService>();
        builder.Services.AddScoped<FacilityService>();
        builder.Services.AddScoped<ActivityGroupService>();
        builder.Services.AddScoped<ActivityTopicService>();
        builder.Services.AddScoped<ActivityTopicExcelService>();
        builder.Services.AddScoped<ActivityEventService>();
        builder.Services.AddScoped<ActivityEventExcelService>();
        builder.Services.AddScoped<EmissionGroupService>();
        builder.Services.AddScoped<EmissionGroupExcelService>();
        builder.Services.AddScoped<EmissionUnitService>();
        builder.Services.AddScoped<EmissionUnitExcelService>();
        builder.Services.AddScoped<EmissionSourceService>();
        builder.Services.AddScoped<EmissionSourceExcelService>();
        builder.Services.AddScoped<EmissionMappingService>();
        builder.Services.AddScoped<FileStorageService>();
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Dashboard.ExecutiveDashboardService>();
        // [2026-09-17][Gemini] Register ExecutiveDashboardService2 for Executive Dashboard V2
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Dashboard.ExecutiveDashboardService2>();
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Dashboard.NetZeroTargetService>();
        // [2026-09-26][Claude] Net-Zero Pathway Dashboard (D2)
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Dashboard.NetZeroDashboardService>();
        // [2026-09-25][Antigravity] Register GhgTargetAlertService and GhgTargetAlertWorker for automated threshold monitoring
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Dashboard.GhgTargetAlertService>();
        builder.Services.AddHostedService<TU.ACAMP.WebApp.Services.Dashboard.GhgTargetAlertWorker>();
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Reports.CfoReportService>();
        // [2026-09-24][Claude] Import / Export Excel ของตารางให้คะแนน Fr-03.2 ใน CFO Wizard
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Reports.CfoFr032ExcelService>();
        // [2026-09-24][Claude] ISSB (IFRS S2) Climate-related Disclosure ต่อยอดจาก CFO snapshot
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Reports.IssbClimateDisclosureService>();
        // [2026-09-23][Claude] รายงาน Word จากเทมเพลต อบก. (ใช้โดย CfoReportStorageService ตอนสร้าง snapshot)
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Reports.CfoWordReportService>();
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Reports.CfoReportStorageService>();

        // API v2 Services & Controllers
        builder.Services.AddMemoryCache();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<GhgEventBusService>();
        builder.Services.AddControllers();
        builder.Services.AddScoped<ApiKeyService>();
        builder.Services.AddScoped<TU.ACAMP.WebApp.Services.Migration.MigrationReportService>(); // [2026-09-23][Claude] รายงานผล migrate (Admin API)
        builder.Services.AddSingleton<PlanRateLimiterService>();
        builder.Services.AddScoped<EventIngestionService>();
        builder.Services.AddScoped<EventAttachmentService>();
        builder.Services.AddHostedService<EventStagingJanitorWorker>();

        // Swagger / OpenAPI documentation
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            #region hide [2026-08-24]
            c.SwaggerDoc("v2", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "TU.ACAMP Event Ingestion API",
                Version = "v2",
                Description = "API v2 for Activity Event Ingestion and File Attachment"
            });

            c.AddSecurityDefinition("X-API-Key", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Description = "X-API-Key header required for authentication (e.g. acamp_1a2b3c4d_...)",
                Name = "X-API-Key",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                Scheme = "ApiKeyScheme"
            });

            c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "X-API-Key"
                        }
                    },
                    Array.Empty<string>()
                }
            });
            #endregion
        });

        return builder;
        #endregion
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        #region hide [2026-08-15]
        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseAntiforgery();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            #region hide [2026-08-24]
            c.SwaggerEndpoint("/swagger/v2/swagger.json", "TU.ACAMP API v2");
            c.RoutePrefix = "swagger";
            #endregion
        });

        app.MapStaticAssets();
        app.MapControllers();
        app.MapHub<TU.ACAMP.WebApp.Hubs.EventNotificationHub>("/hubs/event-notification");
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Identity endpoints (/Account/Logout, /Account/PerformExternalLogin, etc.)
        app.MapAdditionalIdentityEndpoints();

        return app;
        #endregion
    }

    public static async Task MigrateAndSeedDatabaseAsync(this WebApplication app)
    {
        #region hide [2026-08-15]
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            var logger = services.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Applying pending database migrations...");
            await context.Database.MigrateAsync();

            logger.LogInformation("Seeding initial database records...");
            //  await DatabaseSeeder1.SeedAsync(context, logger);
            //  await DatabaseSeeder2.SeedAsync(context, logger);
            //  await DatabaseSeeder3.SeedAsync(context, logger);
            // [2026-09-17][Gemini] ใช้ DatabaseSeeder4 สำหรับทดสอบการ Export CFO Report to Excel ครบทุกขอบเขต 1-4 (ขอบเขต 3 มี Cat 1, 5, 6)
            // [2026-09-18][Claude] DatabaseSeeder5 = ชุดทดสอบการออกรายงานแบบเต็ม (3 ปี, รายการเข้า/ออก,
            // Scope 3 ครบ 9 หมวด, เกินโควตาแถว, ratio != 1, share < 100, สถานประกอบการที่ไม่มีข้อมูล)
            // กลับไปใช้ชุด happy path เดิมได้โดยสลับกลับไปเรียก DatabaseSeeder4
            //  await DatabaseSeeder5.SeedAsync(context, logger);
            // [2026-09-19][Antigravity] DatabaseSeeder6 = ชุดข้อมูลทดสอบการ Migrate จากระบบเก่า (บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด) โดยไม่มี Topic เริ่มต้น
            //await DatabaseSeeder6.SeedAsync(context, logger);
            // [2026-09-24][Claude] DatabaseSeeder7 = องค์กรจำลอง (1 Corporate / 1 Company / 2 Facilities) ข้อมูลครบ 4 ขอบเขต ม.ค. 2023 - เดือนล่าสุดที่ปิดงวด
            // เป็น Subscription แยกของตัวเอง ไม่ยุ่งกับข้อมูล BSCM ของ Seeder6 / ปิดได้โดย comment บรรทัดด้านล่าง
            await DatabaseSeeder7.SeedAsync(context, logger);
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while migrating and seeding the database.");
        }
        #endregion
    }
}
