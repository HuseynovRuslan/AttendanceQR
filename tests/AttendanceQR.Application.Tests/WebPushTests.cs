using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Web Push as the browsers read it: RFC 8291 aes128gcm, signed per RFC 8292 — and what a send comes to.
///
/// The library this replaced wrote the ECDH key and secret as big integers: whenever one began with a zero byte it
/// came out short and the browser silently dropped the message — 11 of 3000 measured pushes. Its every failure but
/// 404/410 also came back as "reached", so a timeout or a 503 looked exactly like a delivered push.
/// These pin the RFC's own example byte for byte, the zero-byte keys explicitly, a few thousand fresh keys through
/// an independent decryption, the VAPID token, and that only a push service's 2xx ever counts as delivered.
/// </summary>
public class WebPushTests
{
    // --- RFC 8291 Appendix A -------------------------------------------------------------------------------

    private const string RfcPlaintext = "When I grow up, I want to be a watermelon";
    private const string RfcAsPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string RfcAsPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string RfcUaPrivate = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    private const string RfcUaPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string RfcSalt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string RfcAuth = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string RfcBody =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    private static byte[] B(string b64url) => WebPushCrypto.Base64UrlDecode(b64url);

    [Fact]
    public void The_rfc_8291_example_is_reproduced_byte_for_byte()
    {
        using var server = KeyPair(B(RfcAsPrivate), B(RfcAsPublic));
        var body = WebPushCrypto.Encrypt(Encoding.UTF8.GetBytes(RfcPlaintext), B(RfcUaPublic), B(RfcAuth), server, B(RfcSalt));
        Assert.Equal(RfcBody, WebPushCrypto.Base64UrlEncode(body));
    }

    [Fact]
    public void The_rfc_8291_example_decrypts_with_the_user_agent_key()
    {
        // Proves the vector itself is right: a wrong value anywhere fails the GCM tag, not just a comparison.
        using var ua = KeyPair(B(RfcUaPrivate), B(RfcUaPublic));
        Assert.Equal(RfcPlaintext, Encoding.UTF8.GetString(Decrypt(B(RfcBody), ua, B(RfcAuth))));
    }

    // --- the zero-byte keys the old encoder lost -------------------------------------------------------------

    [Fact]
    public void A_server_key_whose_coordinate_starts_with_a_zero_byte_still_decrypts()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);
        var uaPublic = SpkiPoint(ua);

        var found = 0;
        for (var tries = 0; tries < 20_000 && found < 4; tries++)
        {
            using var server = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var q = server.ExportParameters(false).Q;
            if (q.X![0] != 0 && q.Y![0] != 0) continue;
            found++;

            var body = WebPushCrypto.Encrypt("{\"t\":1}"u8, uaPublic, auth, server, RandomNumberGenerator.GetBytes(16));
            Assert.Equal(65, body[20]);                           // idlen: always the full point
            Assert.Equal(SpkiPoint(server), body[21..86]);        // exactly the key, zeros included
            Assert.Equal("{\"t\":1}", Encoding.UTF8.GetString(Decrypt(body, ua, auth)));
        }
        Assert.Equal(4, found);
    }

    [Fact]
    public void Three_thousand_fresh_subscriptions_all_decrypt()
    {
        // The regression in numbers: the old encoder failed ~1 in 270. Fresh keys on both sides every time,
        // decrypted by a routine that shares no code with the encoder. Must be all of them.
        const int n = 3000;
        int ok = 0, leadingZero = 0;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { title = "Əliyev Elvin işə gəldi", body = "09:25 · Mərkəz", url = "/admin/notifications" });
        for (var i = 0; i < n; i++)
        {
            using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var auth = RandomNumberGenerator.GetBytes(16);
            var body = WebPushCrypto.Encrypt(payload, SpkiPoint(ua), auth);
            if (body[22] == 0 || body[54] == 0) leadingZero++;   // server X / Y began with a zero byte
            if (Decrypt(body, ua, auth).AsSpan().SequenceEqual(payload)) ok++;
        }
        Assert.Equal(n, ok);
        Assert.True(leadingZero > 0, "no zero-leading key came up — the run did not exercise the old bug");
    }

    [Fact]
    public void A_payload_too_large_for_one_record_is_refused_before_sending()
    {
        using var ua = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var auth = RandomNumberGenerator.GetBytes(16);
        Assert.Equal(4096, WebPushCrypto.Encrypt(new byte[WebPushCrypto.MaxPlaintextLength], SpkiPoint(ua), auth).Length);
        Assert.Throws<ArgumentException>(() =>
            WebPushCrypto.Encrypt(new byte[WebPushCrypto.MaxPlaintextLength + 1], SpkiPoint(ua), auth));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2 }, new byte[] { 0, 0, 1, 2 })]
    [InlineData(new byte[] { 0, 0, 1, 2 }, new byte[] { 0, 0, 1, 2 })]
    [InlineData(new byte[] { 0, 0, 0, 1, 2 }, new byte[] { 0, 0, 1, 2 })]
    public void LeftPad_gives_exactly_the_width(byte[] input, byte[] expected) =>
        Assert.Equal(expected, WebPushCrypto.LeftPad(input, 4));

    // --- VAPID ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_vapid_header_carries_a_verifiable_es256_token_for_the_push_service_origin()
    {
        using var vapidKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (pub, priv) = VapidKeys(vapidKey);
        var clock = new StepClock(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
        using var signer = new VapidSigner(pub, priv, "mailto:ops@example.com", clock);

        var header = signer.AuthorizationFor(new Uri("https://fcm.googleapis.com/fcm/send/abc:def"));
        Assert.StartsWith("vapid t=", header);
        var parts = header["vapid t=".Length..].Split(", k=");
        Assert.Equal(pub, parts[1]);

        var jwt = parts[0].Split('.');
        Assert.Equal(3, jwt.Length);
        using var head = JsonDocument.Parse(B(jwt[0]));
        Assert.Equal("ES256", head.RootElement.GetProperty("alg").GetString());
        Assert.Equal("JWT", head.RootElement.GetProperty("typ").GetString());
        using var claims = JsonDocument.Parse(B(jwt[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal("mailto:ops@example.com", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal(clock.GetUtcNow().AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());

        var signature = B(jwt[2]);
        Assert.Equal(64, signature.Length);  // r || s, not DER
        Assert.True(vapidKey.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void A_vapid_token_is_reused_for_hours_then_renewed_long_before_it_expires()
    {
        using var vapidKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (pub, priv) = VapidKeys(vapidKey);
        var clock = new StepClock(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
        using var signer = new VapidSigner(pub, priv, "mailto:ops@example.com", clock);

        var first = signer.TokenFor("https://push.example");
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(first, signer.TokenFor("https://push.example"));
        Assert.NotEqual(first, signer.TokenFor("https://other.example"));  // one per origin
        clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));    // 6 h left of 12: renew
        Assert.NotEqual(first, signer.TokenFor("https://push.example"));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/x", "https://fcm.googleapis.com")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/x", "https://updates.push.services.mozilla.com")]
    [InlineData("http://127.0.0.1:5310/push/x", "http://127.0.0.1:5310")]
    public void The_audience_is_the_endpoint_origin(string endpoint, string audience) =>
        Assert.Equal(audience, VapidSigner.Audience(new Uri(endpoint)));

    // --- what a send comes to ------------------------------------------------------------------------------

    [Theory]
    [InlineData(201, PushSendResult.Delivered)]
    [InlineData(200, PushSendResult.Delivered)]
    [InlineData(202, PushSendResult.Delivered)]
    [InlineData(404, PushSendResult.Gone)]
    [InlineData(410, PushSendResult.Gone)]
    [InlineData(408, PushSendResult.Transient)]
    [InlineData(429, PushSendResult.Transient)]
    [InlineData(500, PushSendResult.Transient)]
    [InlineData(502, PushSendResult.Transient)]
    [InlineData(503, PushSendResult.Transient)]
    [InlineData(400, PushSendResult.Rejected)]
    [InlineData(401, PushSendResult.Rejected)]
    [InlineData(403, PushSendResult.Rejected)]
    [InlineData(413, PushSendResult.Rejected)]
    public async Task A_push_service_answer_becomes_an_outcome(int status, PushSendResult expected)
    {
        var rig = new SenderRig(_ => new HttpResponseMessage((HttpStatusCode)status));
        Assert.Equal(expected, await rig.SendAsync());
    }

    [Fact]
    public async Task The_request_is_what_a_push_service_expects_and_the_browser_can_open()
    {
        var rig = new SenderRig(_ => new HttpResponseMessage(HttpStatusCode.Created));
        Assert.Equal(PushSendResult.Delivered, await rig.SendAsync("Əliyev Elvin işə gəldi", "09:25", "/admin/notifications"));

        var req = Assert.Single(rig.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(rig.Endpoint, req.Uri);
        Assert.Equal(["aes128gcm"], req.ContentEncoding);
        Assert.Equal("application/octet-stream", req.ContentType);
        Assert.Equal("2419200", req.Ttl);
        Assert.StartsWith("vapid t=", req.Authorization);

        using var json = JsonDocument.Parse(Decrypt(req.Body, rig.Browser, rig.Auth));
        Assert.Equal("Əliyev Elvin işə gəldi", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("09:25", json.RootElement.GetProperty("body").GetString());
        Assert.Equal("/admin/notifications", json.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task An_unreachable_push_service_is_transient_never_delivered()
    {
        var rig = new SenderRig(_ => throw new HttpRequestException("connection refused"));
        Assert.Equal(PushSendResult.Transient, await rig.SendAsync());
    }

    [Fact]
    public async Task The_callers_own_deadline_propagates_so_the_caller_can_count_it()
    {
        var rig = new SenderRig(_ => new HttpResponseMessage(HttpStatusCode.Created), delay: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.SendAsync(ct: cts.Token));
    }

    [Fact]
    public async Task Without_vapid_keys_nothing_is_sent_and_it_says_so()
    {
        var sender = new WebPushSender(new PushOptions(), NullLogger<WebPushSender>.Instance);
        Assert.Equal(PushSendResult.NotConfigured,
            await sender.SendAsync("https://push.example/x", "AA", "AA", "t", "b", null));
    }

    [Fact]
    public async Task Keys_that_do_not_parse_are_refused_not_retried_and_not_pruned()
    {
        var rig = new SenderRig(_ => new HttpResponseMessage(HttpStatusCode.Created));
        Assert.Equal(PushSendResult.Rejected, await rig.Sender.SendAsync(rig.Endpoint.ToString(), "not-a-key", "AAAA", "t", "b", null));
        Assert.Empty(rig.Requests);
    }

    [Theory]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.Unregistered, PushSendResult.Gone)]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.InvalidArgument, PushSendResult.Gone)]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.SenderIdMismatch, PushSendResult.Gone)]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.Unavailable, PushSendResult.Transient)]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.Internal, PushSendResult.Transient)]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.QuotaExceeded, PushSendResult.Transient)]
    [InlineData(FirebaseAdmin.Messaging.MessagingErrorCode.ThirdPartyAuthError, PushSendResult.Rejected)]
    public void An_fcm_error_becomes_an_outcome(FirebaseAdmin.Messaging.MessagingErrorCode code, PushSendResult expected) =>
        Assert.Equal(expected, FirebaseFcmSender.Classify(code, FirebaseAdmin.ErrorCode.Unknown));

    [Theory]
    [InlineData(FirebaseAdmin.ErrorCode.Unavailable, PushSendResult.Transient)]
    [InlineData(FirebaseAdmin.ErrorCode.DeadlineExceeded, PushSendResult.Transient)]
    [InlineData(FirebaseAdmin.ErrorCode.PermissionDenied, PushSendResult.Rejected)]
    [InlineData(FirebaseAdmin.ErrorCode.Unauthenticated, PushSendResult.Rejected)]
    public void An_fcm_error_without_a_messaging_code_falls_back_to_the_platform_code(FirebaseAdmin.ErrorCode code, PushSendResult expected) =>
        Assert.Equal(expected, FirebaseFcmSender.Classify(null, code));

    // --- per employee --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(new[] { PushSendResult.Delivered, PushSendResult.Transient }, PushDelivery.Delivered)]
    [InlineData(new[] { PushSendResult.Gone, PushSendResult.Delivered }, PushDelivery.Delivered)]
    [InlineData(new[] { PushSendResult.Transient, PushSendResult.Rejected }, PushDelivery.Transient)]
    [InlineData(new[] { PushSendResult.Rejected, PushSendResult.Gone }, PushDelivery.Rejected)]
    [InlineData(new[] { PushSendResult.Gone }, PushDelivery.NoDevice)]
    [InlineData(new[] { PushSendResult.NotConfigured }, PushDelivery.NoDevice)]
    [InlineData(new PushSendResult[0], PushDelivery.NoDevice)]
    public void An_employees_devices_add_up_to_one_outcome(PushSendResult[] devices, PushDelivery expected) =>
        Assert.Equal(expected, PushNotifier.Combine(devices));

    [Fact]
    public async Task Only_a_delivered_push_counts_and_a_timeout_is_worth_a_retry()
    {
        await using var db = NewDb();
        Guid delivered = Guid.NewGuid(), timedOut = Guid.NewGuid(), refused = Guid.NewGuid(),
             gone = Guid.NewGuid(), unavailable = Guid.NewGuid(), nobody = Guid.NewGuid();
        Subscribe(db, delivered, "https://push.example/ok");
        Subscribe(db, delivered, "https://push.example/hang");        // a second phone that hangs: still delivered
        Subscribe(db, timedOut, "https://push.example/hang");
        Subscribe(db, refused, "https://push.example/403");
        Subscribe(db, gone, "https://push.example/410");
        Person(db, unavailable);
        db.PushSubscriptions.Add(new PushSubscription { EmployeeId = unavailable, Endpoint = "fcm:t1", FcmToken = "t1", TokenVersion = 0 });
        await db.SaveChangesAsync();

        var web = new ScriptedSender(endpoint => endpoint switch
        {
            "https://push.example/ok" => PushSendResult.Delivered,
            "https://push.example/403" => PushSendResult.Rejected,
            "https://push.example/410" => PushSendResult.Gone,
            _ => null, // hang until the per-send timeout
        });
        var fcm = new ScriptedFcm(PushSendResult.Transient);
        var notifier = new PushNotifier(db, web, fcm, NullLogger<PushNotifier>.Instance);

        var outcome = await notifier.NotifyEachAsync([delivered, timedOut, refused, gone, unavailable, nobody, delivered], "t", "b", null);

        Assert.Equal(6, outcome.Count);
        Assert.Equal(PushDelivery.Delivered, outcome[delivered]);
        Assert.Equal(PushDelivery.Transient, outcome[timedOut]);
        Assert.Equal(PushDelivery.Rejected, outcome[refused]);
        Assert.Equal(PushDelivery.NoDevice, outcome[gone]);
        Assert.Equal(PushDelivery.Transient, outcome[unavailable]);
        Assert.Equal(PushDelivery.NoDevice, outcome[nobody]);

        // Only the gone subscription is pruned; a refused, timed-out or unavailable one is kept.
        var left = await db.PushSubscriptions.Select(s => s.Endpoint).ToListAsync();
        Assert.DoesNotContain(left, e => e.StartsWith("https://push.example/410"));
        Assert.Equal(5, left.Count);
    }

    [Fact]
    public async Task The_count_is_the_number_delivered_not_the_number_attempted()
    {
        await using var db = NewDb();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        Subscribe(db, a, "https://push.example/ok");
        Subscribe(db, b, "https://push.example/503");
        await db.SaveChangesAsync();
        var web = new ScriptedSender(e => e.EndsWith("/ok") ? PushSendResult.Delivered : PushSendResult.Transient);
        var notifier = new PushNotifier(db, web, new ScriptedFcm(PushSendResult.NotConfigured), NullLogger<PushNotifier>.Instance);

        Assert.Equal(1, await notifier.NotifyEmployeesAsync([a, b], "t", "b", null));
    }

    [Fact]
    public async Task A_double_that_can_only_count_still_answers_per_employee()
    {
        IPushNotifier counting = new CountingOnly(reachable: [_known]);
        var outcome = await counting.NotifyEachAsync([_known, Guid.Empty], "t", "b", null);
        Assert.Equal(PushDelivery.Delivered, outcome[_known]);
        Assert.Equal(PushDelivery.NoDevice, outcome[Guid.Empty]);
    }

    private static readonly Guid _known = Guid.NewGuid();

    // --- rig -----------------------------------------------------------------------------------------------

    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b7");

    private static AppDbContext NewDb()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"webpush-{Guid.NewGuid()}").Options, tenant);
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
        db.SaveChanges();
        return db;
    }

    /// <summary>A device of <paramref name="employee"/>'s current session (a push goes only to those — PushSessionBinding).</summary>
    private static void Subscribe(AppDbContext db, Guid employee, string endpoint)
    {
        Person(db, employee);
        db.PushSubscriptions.Add(new PushSubscription
        {
            EmployeeId = employee, Endpoint = endpoint + "#" + Guid.NewGuid(), P256dh = "k", Auth = "a", TokenVersion = 0,
        });
    }

    private static void Person(AppDbContext db, Guid employee)
    {
        if (db.Employees.Local.Any(e => e.Id == employee) || db.Employees.Any(e => e.Id == employee))
            return;
        db.Employees.Add(new Employee
        {
            Id = employee, FullName = "P", Email = $"{employee:N}@t.local", PasswordHash = "x", IsActive = true, TokenVersion = 0,
        });
    }

    private sealed class ScriptedSender(Func<string, PushSendResult?> script) : IPushSender
    {
        public async Task<PushSendResult> SendAsync(string endpoint, string p256dh, string auth, string title, string body, string? url, CancellationToken ct = default)
        {
            var result = script(endpoint.Split('#')[0]);
            if (result is { } r) return r;
            await Task.Delay(Timeout.Infinite, ct);   // hangs until the notifier's per-send timeout cancels it
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class ScriptedFcm(PushSendResult result) : IFcmSender
    {
        public bool IsConfigured => result != PushSendResult.NotConfigured;
        public Task<PushSendResult> SendAsync(string token, string title, string body, string? url, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class CountingOnly(Guid[] reachable) : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default) =>
            Task.FromResult(employeeIds.Count(reachable.Contains));
    }

    private sealed record Captured(HttpMethod Method, Uri Uri, string[] ContentEncoding, string? ContentType, string? Ttl, string? Authorization, byte[] Body);

    private sealed class SenderRig
    {
        public readonly ECDiffieHellman Browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        public readonly byte[] Auth = RandomNumberGenerator.GetBytes(16);
        public readonly Uri Endpoint = new("https://push.example/wpush/v2/abc");
        public readonly List<Captured> Requests = [];
        public readonly WebPushSender Sender;

        public SenderRig(Func<HttpRequestMessage, HttpResponseMessage> answer, TimeSpan? delay = null)
        {
            using var vapidKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var (pub, priv) = VapidKeys(vapidKey);
            var options = new PushOptions { PublicKey = pub, PrivateKey = priv, Subject = "mailto:ops@example.com" };
            Sender = new WebPushSender(options, NullLogger<WebPushSender>.Instance, new Handler(async (req, ct) =>
            {
                Requests.Add(new Captured(req.Method, req.RequestUri!, req.Content!.Headers.ContentEncoding.ToArray(),
                    req.Content.Headers.ContentType?.MediaType,
                    req.Headers.TryGetValues("TTL", out var ttl) ? ttl.Single() : null,
                    req.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
                    await req.Content.ReadAsByteArrayAsync(ct)));
                if (delay is { } d) await Task.Delay(d, ct);
                return answer(req);
            }), TimeProvider.System);
        }

        public Task<PushSendResult> SendAsync(string title = "t", string body = "b", string? url = null, CancellationToken ct = default) =>
            Sender.SendAsync(Endpoint.ToString(), WebPushCrypto.Base64UrlEncode(SpkiPoint(Browser)),
                WebPushCrypto.Base64UrlEncode(Auth), title, body, url, ct);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => answer(request, ct);
    }

    private sealed class StepClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static (string Public, string Private) VapidKeys(ECDsa key)
    {
        var p = key.ExportParameters(true);
        var point = new byte[65];
        point[0] = 4;
        Pad(p.Q.X!).CopyTo(point, 1);
        Pad(p.Q.Y!).CopyTo(point, 33);
        return (WebPushCrypto.Base64UrlEncode(point), WebPushCrypto.Base64UrlEncode(Pad(p.D!)));
    }

    private static ECDiffieHellman KeyPair(byte[] d, byte[] point) => ECDiffieHellman.Create(new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        D = d,
        Q = new ECPoint { X = point[1..33], Y = point[33..] },
    });

    /// <summary>The 65-byte public point taken from the DER SubjectPublicKeyInfo (its last 65 bytes) — a different
    /// export path from the encoder's, so a padding mistake there cannot hide behind the same mistake here.</summary>
    private static byte[] SpkiPoint(ECDiffieHellman key)
    {
        var spki = key.ExportSubjectPublicKeyInfo();
        return spki[^65..];
    }

    private static byte[] Pad(byte[] v) => v.Length >= 32 ? v[^32..] : [.. new byte[32 - v.Length], .. v];

    /// <summary>
    /// An independent RFC 8291 / RFC 8188 decryption: HKDF written out with HMAC-SHA256 rather than the platform's HKDF,
    /// the header parsed by position, the padding stripped by the RFC's rule. Shares no code with the encoder.
    /// </summary>
    private static byte[] Decrypt(byte[] body, ECDiffieHellman userAgent, byte[] authSecret)
    {
        var salt = body[..16];
        var rs = (body[16] << 24) | (body[17] << 16) | (body[18] << 8) | body[19];
        Assert.Equal(4096, rs);
        var idlen = body[20];
        var serverPoint = body[21..(21 + idlen)];
        var ciphertext = body[(21 + idlen)..];

        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPoint[1..33], Y = serverPoint[33..65] },
        });
        var ecdh = Pad(userAgent.DeriveRawSecretAgreement(server.PublicKey));
        var uaPoint = SpkiPoint(userAgent);

        var prkKey = HMACSHA256.HashData(authSecret, ecdh);
        var keyInfo = Encoding.ASCII.GetBytes("WebPush: info\0").Concat(uaPoint).Concat(serverPoint).Append((byte)1).ToArray();
        var ikm = HMACSHA256.HashData(prkKey, keyInfo);

        var prk = HMACSHA256.HashData(salt, ikm);
        var cek = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0\u0001"))[..16];
        var nonce = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0\u0001"))[..12];

        var plain = new byte[ciphertext.Length - 16];
        using (var aes = new AesGcm(cek, 16))
            aes.Decrypt(nonce, ciphertext[..^16], ciphertext[^16..], plain);

        var end = plain.Length - 1;
        while (end >= 0 && plain[end] == 0) end--;
        Assert.True(end >= 0 && plain[end] == 2, "last record must end in the 0x02 delimiter");
        return plain[..end];
    }
}
