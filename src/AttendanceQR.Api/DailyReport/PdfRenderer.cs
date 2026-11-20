using System.Diagnostics;
using System.Text;
using AttendanceQR.Application.Common;

namespace AttendanceQR.Api.DailyReport;

public interface IPdfRenderer
{
    /// <summary>Prints a self-contained HTML page to a PDF (A4 landscape comes from the page's own @page rule).</summary>
    Task<byte[]> RenderAsync(string html, CancellationToken ct);
}

/// <summary>
/// Prints the report's HTML with headless Chrome — the same page the link opens, so the PDF is the design,
/// not an approximation of it. One short-lived process per report, in its own throwaway profile and
/// temp folder; the page loads nothing from the network, so nothing waits on one.
/// </summary>
public sealed class ChromePdfRenderer : IPdfRenderer
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    // One Chrome at a time: a report is a few hundred MB of browser for a second or two, and the
    // backend shares its memory ceiling with the scan path. Ten «send now» clicks queue, not stack.
    private static readonly SemaphoreSlim OneAtATime = new(1, 1);
    private readonly DailyReportOptions _options;
    private readonly ILogger<ChromePdfRenderer> _logger;

    public ChromePdfRenderer(DailyReportOptions options, ILogger<ChromePdfRenderer> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(string html, CancellationToken ct)
    {
        await OneAtATime.WaitAsync(ct);
        try { return await RenderOneAsync(html, ct); }
        finally { OneAtATime.Release(); }
    }

    private async Task<byte[]> RenderOneAsync(string html, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "qrlog-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var htmlPath = Path.Combine(dir, "report.html");
            var pdfPath = Path.Combine(dir, "report.pdf");
            await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false), ct);

            var psi = new ProcessStartInfo(_options.ChromePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            // The container user's home may not be writable; Chrome keeps its crash/config files there.
            psi.Environment["HOME"] = dir;
            foreach (var arg in new[]
                     {
                         "--headless=new", "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage",
                         "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                         "--disable-background-networking", "--disable-sync", "--hide-scrollbars", "--mute-audio",
                         $"--user-data-dir={Path.Combine(dir, "profile")}",
                         "--no-pdf-header-footer", "--run-all-compositor-stages-before-draw",
                         $"--print-to-pdf={pdfPath}",
                         new Uri(htmlPath).AbsoluteUri,
                     })
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Chrome did not start");
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            _ = proc.StandardOutput.ReadToEndAsync(ct);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(Limit);
            try
            {
                await proc.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
                throw new TimeoutException("PDF rendering timed out");
            }

            if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
            {
                var err = await stderrTask;
                _logger.LogWarning("Chrome produced no PDF (exit {Code}): {Err}", proc.ExitCode, err.Length > 400 ? err[..400] : err);
                throw new InvalidOperationException($"PDF rendering failed (exit {proc.ExitCode})");
            }
            return await File.ReadAllBytesAsync(pdfPath, ct);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* a temp folder; the OS cleans it eventually */ }
        }
    }
}
