namespace web_api.Interfaces;

public interface IOneTimeCodeStore
{
    OtpIssueResult TryIssue(string destination, string purpose, string codeHash);
    OtpVerificationStatus Verify(string destination, string purpose, string codeHash);
    void Remove(string destination, string purpose);
}

public readonly record struct OtpIssueResult(bool Succeeded, TimeSpan? RetryAfter)
{
    public static OtpIssueResult Success() => new(true, null);
    public static OtpIssueResult Cooldown(TimeSpan retryAfter) => new(false, retryAfter);
}

public enum OtpVerificationStatus
{
    Succeeded,
    InvalidOrExpired
}
