using System.Globalization;
using System.Net;
using System.Text;

namespace AttendanceQR.Application.Reporting.DailyReport;

/// <summary>
/// Renders the daily management report as one self-contained HTML page — the owner's design
/// («davamiyyet-…-modern.html»): a table on a computer, cards on a phone, and an A4-landscape print
/// layout the PDF is made from. Nothing is loaded from elsewhere (no fonts, no scripts, no images), so
/// the page opens instantly inside Telegram and the PDF renders the same offline.
///
/// Both the PDF and the page behind the link are produced from the same <see cref="DailyReportData"/>
/// snapshot by this one function, so they cannot disagree.
/// </summary>
public static class DailyReportHtml
{
    private static readonly string[] Months =
        ["Yanvar", "Fevral", "Mart", "Aprel", "May", "İyun", "İyul", "Avqust", "Sentyabr", "Oktyabr", "Noyabr", "Dekabr"];

    public static string DateLong(DateOnly d) => $"{d.Day} {Months[d.Month - 1]} {d.Year}";

    private sealed record Col(string Key, string Label, string HeadHtml, string Css, string Dot, Func<DailyReportCounts, int> Get);

    // The table's status columns, in the design's order. The last three appear only on a day that has
    // somebody in them — the design has no room for empty columns, and an always-empty column is noise.
    private static readonly Col[] CoreCols =
    [
        new("present", "İşdə", "İşdə", "c1", "dc1", c => c.Present),
        new("rest", "İstirahət", "İstirahət", "c2", "dc2", c => c.Rest),
        new("vacation", "Məzuniyyət", "Məzuniyyət", "c3", "dc3", c => c.Vacation),
        new("absent", "İşə gəlməyib", "İşə<br>gəlməyib", "c4", "dc4", c => c.Absent),
        new("permission", "İcazə", "İcazə", "c5", "dc5", c => c.Permission),
        new("sick", "Xəstəlik", "Xəstəlik", "c6", "dc6", c => c.Sick),
        new("trip", "Ezamiyyət", "Ezamiyyət", "c7", "dc7", c => c.Trip),
    ];

    private static readonly Col[] OptionalCols =
    [
        new("unpaid", "Ödənişsiz", "Ödənişsiz", "c8", "dc8", c => c.Unpaid),
        new("flex", "Sərbəst qrafik", "Sərbəst<br>qrafik", "c9", "dc9", c => c.FlexNoScan),
        new("onboarding", "Aktivləşdirməyib", "Aktivləş-<br>dirməyib", "c10", "dc10", c => c.Onboarding),
    ];

    public static string Render(DailyReportData d)
    {
        var cols = CoreCols.Concat(OptionalCols.Where(c => c.Get(d.Totals) > 0)).ToArray();
        var t = d.Totals;
        var ratePct = t.Employees == 0 ? 0 : (int)Math.Round(100.0 * t.Present / t.Employees);
        var sb = new StringBuilder(64 * 1024);

        sb.Append("<!doctype html><html lang=\"az\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1,viewport-fit=cover\">")
          .Append("<meta name=\"robots\" content=\"noindex,nofollow\"><meta name=\"theme-color\" content=\"#F6F7F9\">")
          .Append("<title>Davamiyyət · ").Append(E(DateLong(d.Date))).Append("</title><style>").Append(Css).Append("</style></head><body>");

        // Head
        var sub = new List<string> { E(d.CompanyName), E(DateLong(d.Date)), $"{d.Areas.Count} ərazi", $"saat {d.TakenAtLocal:HH:mm} vəziyyəti" };
        if (!string.IsNullOrEmpty(d.ScopeNote)) sub.Add(E(d.ScopeNote));
        sb.Append("<div class=\"head\"><div><h1>Gündəlik davamiyyət</h1><div class=\"sub\">")
          .Append(string.Join(" · ", sub)).Append("</div></div>")
          .Append("<div class=\"rate\"><b>").Append(ratePct).Append("%</b><span>")
          .Append(t.Present).Append(" / ").Append(t.Employees).Append(" işdə</span></div></div>");

        // Ştat / İşçi sayı / Vakant
        sb.Append("<div class=\"kpis3\">")
          .Append(Kpi2(d.HeadcountTotal?.ToString(CultureInfo.InvariantCulture) ?? "—", "Ştat"))
          .Append(Kpi2(t.Employees.ToString(CultureInfo.InvariantCulture), "İşçi sayı"))
          .Append(Kpi2(d.HeadcountTotal is null ? "—" : d.Vacancies.ToString(CultureInfo.InvariantCulture), "Vakant yer"))
          .Append("</div>");

        // Six headline figures (İcazə and Xəstəlik share one tile here; the table keeps them apart)
        sb.Append("<div class=\"kpis\">")
          .Append(Kpi("#7CC4A0", t.Present, "İşdə"))
          .Append(Kpi("#8FB2E6", t.Rest, "İstirahət"))
          .Append(Kpi("#E9C679", t.Vacation, "Məzuniyyət"))
          .Append(Kpi("#E8A0A0", t.Absent, "İşə gəlməyib"))
          .Append(Kpi("#BBA3DB", t.Permission + t.Sick, "İcazə / Xəstəlik"))
          .Append(Kpi("#7FC3CF", t.Trip, "Ezamiyyət"))
          .Append("</div>");

        sb.Append("<div class=\"layout\">");

        // Phone: one card per area
        sb.Append("<div class=\"mcards\">");
        foreach (var a in d.Areas) sb.Append(MCard(a.Name, a.Counts, cols, false));
        sb.Append(MCard("CƏMİ", t, cols, true));
        sb.Append("</div>");

        // Computer / PDF: the table
        sb.Append("<div class=\"card main\"><table><thead><tr><th>№</th><th>Ərazi</th><th class=\"num\">Ştat</th><th class=\"num\">İşçi sayı</th>");
        foreach (var c in cols)
            sb.Append("<th class=\"num\"><i class=\"hd ").Append(c.Dot).Append("\"></i><br>").Append(c.HeadHtml).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        var i = 0;
        foreach (var a in d.Areas)
        {
            i++;
            sb.Append("<tr><td class=\"idx\">").Append(i).Append("</td><td class=\"name\">").Append(E(a.Name)).Append("</td>")
              .Append("<td class=\"num stat\">").Append(a.Headcount?.ToString(CultureInfo.InvariantCulture) ?? "<span class=\"zero\">–</span>").Append("</td>")
              .Append("<td class=\"num total\">").Append(a.Counts.Employees).Append("</td><td class=\"br\"></td>");
            foreach (var c in cols) sb.Append(Cell(c, c.Get(a.Counts)));
            sb.Append("</tr>");
        }
        sb.Append("<tr class=\"sum\"><td></td><td class=\"name\">CƏMİ</td><td class=\"num stat\"><b>")
          .Append(d.HeadcountTotal?.ToString(CultureInfo.InvariantCulture) ?? "–").Append("</b></td><td class=\"num total total2\"><b>")
          .Append(t.Employees).Append("</b></td><td class=\"br\"></td>");
        foreach (var c in cols) sb.Append(Cell(c, c.Get(t)));
        sb.Append("</tr></tbody></table></div>");

        // Side column
        sb.Append("<div class=\"col\">");
        var pending = d.Areas.Where(a => a.Counts.Pending > 0).OrderByDescending(a => a.Counts.Pending).ToList();
        sb.Append(SideList("Növbəsi başlamayıb", pending.Count == 0 ? "Hamının növbəsi başlayıb" : $"{pending.Count} ərazi · cəmi {t.Pending} nəfər",
            pending.Select(a => (a.Name, a.Counts.Pending)).ToList(), "#E9B26A"));
        if (d.HeadcountTotal is not null)
        {
            var vac = d.Areas.Where(a => a.Vacancies > 0).OrderByDescending(a => a.Vacancies).ToList();
            sb.Append(SideList("Vakant yer", vac.Count == 0 ? "Vakant yer yoxdur" : $"{vac.Count} ərazi · cəmi {d.Vacancies} yer",
                vac.Select(a => (a.Name, a.Vacancies)).ToList(), "#9DB4D9"));
        }
        if (d.Structures.Count > 0)
        {
            sb.Append("<div class=\"card side\"><div class=\"st\">Struktur üzrə</div><div class=\"ss\">İşdə / işçi sayı</div>");
            foreach (var s in d.Structures)
            {
                var pct = s.Employees == 0 ? 0 : (int)Math.Round(100.0 * s.Present / s.Employees);
                sb.Append("<div class=\"ni\"><div class=\"nr\"><span>").Append(E(s.Name)).Append(" <small>· ").Append(s.Areas)
                  .Append(" ərazi</small></span><b>").Append(s.Present).Append(" / ").Append(s.Employees).Append("</b></div>")
                  .Append("<div class=\"bar\"><i style=\"width:").Append(pct).Append("%;background:#7CC4A0\"></i></div></div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div></div>");

        sb.Append("<div class=\"foot\">QRLog · ").Append(E(d.CompanyName)).Append(" · ")
          .Append(d.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).Append(' ')
          .Append(d.TakenAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture)).Append("</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Kpi2(string value, string label)
        => $"<div class=\"kpi kpi2\"><div class=\"kv\">{value}</div><div class=\"kl\">{label}</div></div>";

    private static string Kpi(string color, int value, string label)
        => $"<div class=\"kpi\"><i class=\"dot\" style=\"background:{color}\"></i><div><div class=\"kv\">{value}</div><div class=\"kl\">{label}</div></div></div>";

    private static string Cell(Col c, int v)
        => v == 0
            ? "<td class=\"num z\"><span class=\"zero\">–</span></td>"
            : $"<td class=\"num\"><span class=\"pill {c.Css}\"><em>{c.Label}</em>{v}</span></td>";

    private static string MCard(string name, DailyReportCounts counts, Col[] cols, bool total)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"mcard").Append(total ? " mtot" : "").Append("\"><div class=\"mh\"><span>").Append(E(name))
          .Append("</span><b>").Append(counts.Employees).Append("<small> nəfər</small></b></div><div class=\"mg\">");
        foreach (var c in cols)
        {
            var v = c.Get(counts);
            sb.Append("<div class=\"mc ").Append(v == 0 ? "mz" : c.Css).Append("\"><div class=\"ml\">").Append(c.Label)
              .Append("</div><div class=\"mv\">").Append(v).Append("</div></div>");
        }
        if (counts.Pending > 0)
            sb.Append("<div class=\"mc cp\"><div class=\"ml\">Növbəsi başlamayıb</div><div class=\"mv\">").Append(counts.Pending).Append("</div></div>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    private static string SideList(string title, string subtitle, List<(string Name, int Value)> items, string color)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"card side\"><div class=\"st\">").Append(title).Append("</div><div class=\"ss\">").Append(E(subtitle)).Append("</div>");
        var max = items.Count == 0 ? 1 : Math.Max(1, items.Max(x => x.Value));
        // The biggest few: a list of sixty branches is a table, and the table is already on the page.
        foreach (var (name, value) in items.Take(SideListMax))
        {
            var w = Math.Max(4, (int)Math.Round(100.0 * value / max));
            sb.Append("<div class=\"ni\"><div class=\"nr\"><span>").Append(E(name)).Append("</span><b>").Append(value)
              .Append("</b></div><div class=\"bar\"><i style=\"width:").Append(w).Append("%;background:").Append(color).Append("\"></i></div></div>");
        }
        if (items.Count > SideListMax)
            sb.Append("<div class=\"nr\" style=\"margin-bottom:10px\"><small>+ daha ").Append(items.Count - SideListMax).Append(" ərazi</small></div>");
        sb.Append("</div>");
        return sb.ToString();
    }

    private const int SideListMax = 8;

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    // The owner's stylesheet, kept as it was. Additions are marked: a footer, three optional colours,
    // the «not started» chip on the phone cards, and print rules for a report longer than one page.
    private const string Css = """
@page{size:A4 landscape;margin:0}
*{box-sizing:border-box;margin:0;padding:0}
html,body{min-height:100%}
body{font-family:'Inter',system-ui,-apple-system,'Segoe UI',Roboto,'Noto Sans',Arial,sans-serif;background:#F6F7F9;color:#1F2937;max-width:1180px;margin:0 auto;padding:26px 34px 30px;-webkit-print-color-adjust:exact;print-color-adjust:exact;font-feature-settings:'tnum'}
.head{display:flex;justify-content:space-between;align-items:flex-end;margin-bottom:10px}
h1{font-size:21px;font-weight:650;letter-spacing:-.3px;color:#111827}
.sub{font-size:11.5px;color:#6B7280;margin-top:3px}
.rate{text-align:right} .rate b{font-size:21px;font-weight:650;color:#2F7D5B}
.rate span{display:block;font-size:10.5px;color:#6B7280;margin-top:2px}
.kpis{display:grid;grid-template-columns:repeat(6,1fr);gap:10px;margin-bottom:14px}
.kpi{background:#fff;border-radius:11px;padding:9px 13px;display:flex;align-items:center;gap:10px;box-shadow:0 1px 2px rgba(17,24,39,.05)}
.kv{font-size:19px;font-weight:650;line-height:1;color:#111827} .kl{font-size:10px;color:#6B7280;margin-top:3px}
.dot{width:8px;height:30px;border-radius:4px;display:block}
.card{background:#fff;border-radius:13px;box-shadow:0 1px 2px rgba(17,24,39,.05);padding:4px 10px 6px}
table{width:100%;border-collapse:collapse;font-size:11px}
th{font-size:9.5px;font-weight:600;color:#6B7280;text-align:left;padding:8px 4px 7px;border-bottom:1px solid #E5E7EB;line-height:1.2}
th.num,td.num{text-align:center}
td{height:19px;padding:0 4px;border-bottom:1px solid #F1F3F5}
tr:last-child td{border-bottom:0}
.idx{width:28px;color:#9CA3AF;font-size:10px} .name{font-weight:500;color:#1F2937}
.stat{color:#6B7280}
.zero{color:#D1D5DB}
.pill{display:inline-block;min-width:24px;padding:1.5px 7px;border-radius:99px;font-weight:600;font-size:10.5px}
.total{font-weight:650;color:#111827;background:#FAFAFB}
tr.sum td{border-top:1.5px solid #E5E7EB;background:#F3F5F7;height:24px;font-weight:650;color:#111827}
tr.sum td.name{letter-spacing:.5px} .total2{background:#E9EDF1}
.c1{background:#E3F2EA;color:#1F6B4A} .c2{background:#E4EDFA;color:#2F5C9E} .c3{background:#FBF0D9;color:#8A6112}
.c4{background:#FBE4E4;color:#A13A3A} .c5{background:#EEE7F8;color:#6A4A9C} .c6{background:#FBE5EE;color:#A0426B} .c7{background:#DFF1F4;color:#26707F}
.c8{background:#EEF0F3;color:#4B5563} .c9{background:#E6F4EF;color:#2F6F5C} .c10{background:#F3F4F6;color:#6B7280} .cp{background:#FCEFD9;color:#8A5A12}
.hd{display:inline-block;width:14px;height:4px;border-radius:2px;margin-bottom:4px}
.dc1{background:#7CC4A0}.dc2{background:#8FB2E6}.dc3{background:#E9C679}.dc4{background:#E8A0A0}.dc5{background:#BBA3DB}.dc6{background:#E59BB8}.dc7{background:#7FC3CF}
.dc8{background:#B8BEC8}.dc9{background:#8CCAB6}.dc10{background:#C9CDD3}
.layout{display:flex;gap:12px;align-items:flex-start} .main{flex:1}
.col{width:240px;display:flex;flex-direction:column;gap:12px}
.side{padding:15px 16px 6px}
.st{font-size:13px;font-weight:650;color:#111827} .ss{font-size:10.5px;color:#6B7280;margin:3px 0 13px}
.ni{margin-bottom:12px} .nr{display:flex;justify-content:space-between;font-size:11px;margin-bottom:5px;gap:8px} .nr b{font-weight:650;white-space:nowrap} .nr small{color:#9CA3AF;font-weight:500}
.bar{height:5px;background:#EEF0F3;border-radius:3px} .bar i{display:block;height:5px;border-radius:3px}
.kpis3{display:grid;grid-template-columns:repeat(3,1fr);gap:10px;margin-bottom:10px}
.kpi2{flex-direction:row;align-items:baseline;gap:10px;padding:9px 16px;background:#fff} .kpi2 .kv{font-size:19px} .kpi2 .kl{font-size:11px;margin:0}
.mcards{display:none}
em{display:none;font-style:normal} td.br{display:none}
tr.sum .pill{background:none!important;color:inherit;padding:0;min-width:0;font-weight:650;font-size:11px}
.foot{margin-top:14px;font-size:9.5px;color:#9CA3AF;text-align:right}
@media print{body{width:1123px;padding:26px 34px 18px;max-width:none;margin:0} tr{break-inside:avoid} thead{display:table-header-group} .side{break-inside:avoid} .layout{flex-direction:column;align-items:stretch} .col{width:auto;flex-direction:row;align-items:flex-start} .col>.side{flex:1;min-width:0}}
@media screen and (max-width:899px){
 body{padding:max(16px,env(safe-area-inset-top)) 14px 28px;font-size:14px}
 h1{font-size:20px} .sub{font-size:11px} .rate b{font-size:22px}
 .kpis3{gap:8px;margin-bottom:8px} .kpi2{flex-direction:column;align-items:flex-start;gap:3px;padding:11px 12px} .kpi2 .kv{font-size:22px} .kpi2 .kl{font-size:10.5px} .kpis{grid-template-columns:repeat(3,1fr);gap:8px;margin-bottom:12px}
 .kpi{flex-direction:column;align-items:flex-start;gap:7px;padding:11px 12px}
 .dot{width:22px;height:4px} .kv{font-size:22px} .kl{font-size:10.5px}
 .layout{flex-direction:column;gap:12px} .main{width:100%;padding:6px 14px 8px}
 .col{width:100%} .side{padding:16px 16px 6px}
 .st{font-size:14px} .nr{font-size:13px}
 .main{display:none} .mcards{display:flex;flex-direction:column;gap:10px;width:100%;order:1} .col{order:0}
 .foot{text-align:center}
}
.mcard{background:#fff;border-radius:13px;box-shadow:0 1px 2px rgba(17,24,39,.05);padding:13px 12px 12px}
.mh{display:flex;justify-content:space-between;align-items:baseline;margin-bottom:10px;gap:8px}
.mh span{font-size:14.5px;font-weight:650;color:#111827} .mh b{font-size:17px;font-weight:650;color:#111827;white-space:nowrap} .mh small{font-size:10.5px;font-weight:500;color:#9CA3AF}
.mg{display:grid;grid-template-columns:repeat(3,1fr);gap:6px}
.mc{border-radius:9px;padding:7px 9px 8px} .ml{font-size:10px;font-weight:500;opacity:.85} .mv{font-size:18px;font-weight:650;margin-top:2px;line-height:1.1}
.mz{background:#F5F6F8;color:#B4BAC4}
.mtot{background:#EEF1F4;box-shadow:none}
""";
}
