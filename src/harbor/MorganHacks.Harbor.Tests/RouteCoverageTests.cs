using System.Text.Json;
using System.Text.RegularExpressions;

namespace MorganHacks.Harbor.Tests;

/// <summary>
/// Harbor is the only way in, and its route table is an allowlist with no
/// catch-all. That is the right shape, but it means a route can be missing and
/// nothing anywhere says so: atlas serves the endpoint, harbor answers 404, and
/// the browser reports a signed-in user with no data. The whole applicant portal
/// shipped that way once and was found by hand.
/// </summary>
/// <remarks>
/// These tests read the config as a file rather than through the host, because
/// both failures they guard against happen before any route is ever matched.
/// </remarks>
public class RouteCoverageTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "harbor")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string ConfigPath() =>
        Path.Combine(Root(), "src", "harbor", "MorganHacks.Harbor", "appsettings.json");

    [Fact]
    public void The_route_table_is_pure_ascii()
    {
        // A single non-ASCII character -- an em dash pasted into a comment is
        // the way it happens -- stops the configuration binding outright. No
        // exception is raised and no line is logged. Every route 404s at once,
        // which reads like the gateway is down rather than like a typo.
        var text = File.ReadAllText(ConfigPath());

        var offenders = text
            .Select((c, i) => (c, i))
            .Where(x => x.c > 127)
            .Select(x => $"U+{(int)x.c:X4} at offset {x.i}")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_atlas_endpoint_group_is_reachable_through_harbor()
    {
        var groups = Directory
            .EnumerateFiles(Path.Combine(Root(), "src", "atlas"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"MapGroup\(""(/[a-z0-9-]+)"""))
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

        // If this is empty the scan broke, and an empty set would pass silently.
        Assert.NotEmpty(groups);

        var paths = Routes().ToArray();
        var unreachable = groups
            .Where(g => !paths.Any(p => p.StartsWith($"/api{g}/", StringComparison.Ordinal)
                                     || p.Equals($"/api{g}", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(unreachable);
    }

    private static IEnumerable<string> Routes()
    {
        // The file carries // comments, which JsonDocument rejects.
        var json = File.ReadAllText(ConfigPath());
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        foreach (var route in doc.RootElement
                     .GetProperty("ReverseProxy")
                     .GetProperty("Routes")
                     .EnumerateObject())
        {
            yield return route.Value.GetProperty("Match").GetProperty("Path").GetString()!;
        }
    }
}
