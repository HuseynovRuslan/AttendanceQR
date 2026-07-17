using AttendanceQR.Application.Common;
using Microsoft.Extensions.Configuration;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// appsettings.json is read at STARTUP, not at build — a malformed one compiles perfectly and then
/// takes the whole API down on the next deploy. This loads the real file the way Program.cs does.
/// </summary>
public class AppSettingsParseTests
{
    private static IConfigurationRoot RealAppSettings()
    {
        // Walk up from the test bin directory to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AttendanceQR.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "src", "AttendanceQR.Api", "appsettings.json");
        Assert.True(File.Exists(path), $"appsettings.json not found at {path}");

        return new ConfigurationBuilder().AddJsonFile(path).Build();
    }

    [Fact]
    public void The_real_appsettings_parses()
    {
        // Also the answer to "does the JSON config provider allow // comments" — it does
        // (JsonCommentHandling.Skip), but that is worth a test rather than a memory.
        var config = RealAppSettings();
        Assert.Equal("Asia/Baku", config["App:TimeZone"]);
    }

    [Fact]
    public void It_binds_onto_AppOptions()
    {
        var options = new AppOptions();
        RealAppSettings().GetSection(AppOptions.SectionName).Bind(options);

        Assert.Equal("Asia/Baku", options.TimeZone);
        // Ships empty: naming a super-admin is an environment decision, never a committed one.
        Assert.Empty(options.SuperAdminIdList());
    }
}
