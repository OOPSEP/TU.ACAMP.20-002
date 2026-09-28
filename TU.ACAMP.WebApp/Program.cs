namespace TU.ACAMP.WebApp;

using Serilog;
using Serilog.Events;
using TU.ACAMP.WebApp.Services;

public class Program
{
    public static async Task Main(string[] args)
    {
        #region hide [2026-08-15]
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "Logs/acamp-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("Starting ACAMP Web Application host...");

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            TU.ACAMP.Domain.Tools.AppTimeZone.SetDefaultTimeZone(builder.Configuration["App:TimeZone"]);

            builder.AddApplicationServices();

            var app = builder.Build();

            app.ConfigurePipeline();

            await app.MigrateAndSeedDatabaseAsync();

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "ACAMP Web Application host terminated unexpectedly.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
        #endregion
    }
}
