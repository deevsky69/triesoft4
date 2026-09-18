namespace Triesoft.Core.Audit;

public sealed record ChainVerificationResult(bool IsIntact, int? FailureIndex, string? FailureReason);
