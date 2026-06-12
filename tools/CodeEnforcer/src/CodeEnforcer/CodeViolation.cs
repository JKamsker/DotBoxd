namespace CodeEnforcer;

internal sealed record CodeViolation(string Rule, string Path, string Message);
