using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TU.ACAMP.WebApp.Data;
using Xunit;
using Xunit.Abstractions;

namespace TU.ACAMP.Tests;

public class DatabaseInvestigationTests
{
    private readonly ITestOutputHelper _output;

    public DatabaseInvestigationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SurveyGHG11AffectedRowsInDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
            .Build();

        var connectionString = config.GetConnectionString("DefaultConnection") 
            ?? "Server=127.0.0.1;Uid=dti_user;Pwd=dti6873;Connection Timeout=10;Database=acamp_V20_002;Port=3306;SslMode=Required;Character Set=utf8mb4;";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        try
        {
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            using var context = new ApplicationDbContext(optionsBuilder.Options);

            var total0322Sources = await context.EmissionSources
                .Include(s => s.Group)
                .Where(s => !s.IsDeleted && s.Group != null && s.Group.Code == "0322")
                .CountAsync();

            var total0322Childs = await context.TopicChilds
                .Include(tc => tc.Source).ThenInclude(s => s!.Group)
                .Where(tc => !tc.IsDeleted && tc.Source != null && tc.Source.Group != null && tc.Source.Group.Code == "0322")
                .CountAsync();

            var query = from tc in context.TopicChilds
                        join t in context.Topics on tc.TopicId equals t.Id
                        join s in context.EmissionSources on tc.SourceId equals s.Id
                        join eg in context.EmissionGroups on s.GroupId equals eg.Id
                        where !tc.IsDeleted && !t.IsDeleted && eg.Code == "0322" && tc.ActivityGroupId == 11
                        select new
                        {
                            tc.Id,
                            TopicId = t.Id,
                            TopicTitle = t.Title,
                            SourceName = s.Title,
                            tc.ActivityGroupId,
                            ConversionRatio = tc.UnitConversionRatio ?? 1m,
                            SharePercent = (decimal)tc.EmissionSharePercent
                        };

            var list = await query.Take(50).ToListAsync();
            _output.WriteLine($"[GHG-11 DB SURVEY] Total 0322 Sources: {total0322Sources}");
            _output.WriteLine($"[GHG-11 DB SURVEY] Total 0322 TopicChilds: {total0322Childs}");
            _output.WriteLine($"[GHG-11 DB SURVEY] Total affected rows (mapped to ActivityGroupId=11): {list.Count}");
            foreach (var item in list)
            {
                _output.WriteLine($"TopicId {item.TopicId} | {item.TopicTitle} | Source: {item.SourceName}");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"[GHG-11 DB SURVEY] Connection status: {ex.Message}");
        }
    }
}
