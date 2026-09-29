namespace Tiel.Web.Services;

/// <summary>
/// A deliberately conservative token estimate, since phase 0 has no tokenizer:
/// <c>ceil(chars / 3.5) + 4</c> per message. Pure.
/// </summary>
public static class TokenEstimator
{
    public const int PerMessageOverhead = 4;

    public static int Estimate(string content) => (int)Math.Ceiling(content.Length / 3.5) + PerMessageOverhead;
}
