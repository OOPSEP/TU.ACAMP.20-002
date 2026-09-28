using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace TU.ACAMP.Tests;

public class DateTimeScannerTests
{
    private readonly ITestOutputHelper _output;

    public DateTimeScannerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ScanDateTimeUsagesAcrossSolution()
    {
        var solutionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var searchDirs = new[]
        {
            Path.Combine(solutionDir, "TU.ACAMP.Domain"),
            Path.Combine(solutionDir, "TU.ACAMP.WebApp")
        };

        var pattern = new Regex(@"\b(DateTime\.Now|DateTime\.UtcNow|\.ToLocalTime\(\))\b");

        int matchCount = 0;
        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor")) &&
                            !f.Contains(@"\obj\") &&
                            !f.Contains(@"\bin\") &&
                            !f.Contains(@"\Migrations\"));

            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var matches = pattern.Matches(line);
                    if (matches.Count > 0)
                    {
                        var relativePath = Path.GetRelativePath(solutionDir, file);
                        _output.WriteLine($"{relativePath}:{i + 1} | {line.Trim()}");
                        matchCount++;
                    }
                }
            }
        }

        _output.WriteLine($"Total DateTime.Now/UtcNow/ToLocalTime instances found: {matchCount}");
    }
}
