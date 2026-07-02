namespace AttendanceQR.Infrastructure.Security;

public interface INonceStore
{
    /// <summary>
    /// Atomically records a nonce as used. Returns <c>true</c> if the nonce was not
    /// seen before (and is now stored), or <c>false</c> if it was already consumed.
    /// </summary>
    bool TryConsume(string nonce);
}
