using System.Security.Cryptography;
using System.Text;

namespace PicLoom.Api.Security;

public sealed class LocalSessionService(PicLoomOptions options, TimeProvider clock)
{
    public const string CookieName = "PicLoom.Session";
    private readonly object gate = new();
    private readonly Dictionary<string, DateTimeOffset> bootstrap = [];
    private readonly Dictionary<string, Session> sessions = [];
    private sealed record Session(string CsrfToken, DateTimeOffset ExpiresAt);
    private static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public Uri CreateLaunchUri(bool developmentUi = false)
    {
        if (developmentUi && !options.AllowDevProxy) throw new InvalidOperationException("Development UI is disabled.");
        lock (gate)
        {
            var token = RandomToken();
            foreach (var key in bootstrap.Where(pair => pair.Value <= clock.GetUtcNow()).Select(pair => pair.Key).ToArray())
                bootstrap.Remove(key);
            if (bootstrap.Count >= 32) bootstrap.Remove(bootstrap.Keys.First());
            bootstrap[Hash(token)] = clock.GetUtcNow().AddMinutes(2);
            return new Uri($"{(developmentUi ? PicLoomOptions.DevOrigin : options.Origin)}/#bootstrap={token}");
        }
    }

    public bool TryRedeem(string? token, out string cookie, out string csrf)
    {
        cookie = csrf = "";
        if (token is not { Length: 64 }) return false;
        lock (gate)
        {
            if (!bootstrap.Remove(Hash(token), out var expires) || expires <= clock.GetUtcNow()) return false;
            cookie = RandomToken();
            csrf = RandomToken();
            foreach (var key in sessions.Where(pair => pair.Value.ExpiresAt <= clock.GetUtcNow()).Select(pair => pair.Key).ToArray())
                sessions.Remove(key);
            if (sessions.Count >= 32) sessions.Remove(sessions.Keys.First());
            sessions[Hash(cookie)] = new Session(csrf, clock.GetUtcNow().AddHours(8));
            return true;
        }
    }

    public bool TryGetSession(string? cookie, out string csrf)
    {
        csrf = "";
        if (cookie is not { Length: 64 }) return false;
        lock (gate)
        {
            var key = Hash(cookie);
            if (!sessions.TryGetValue(key, out var session)) return false;
            if (session.ExpiresAt <= clock.GetUtcNow())
            {
                sessions.Remove(key);
                return false;
            }
            csrf = session.CsrfToken;
            return true;
        }
    }

    public static bool ValidCsrf(string actual, string expected) => actual.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
