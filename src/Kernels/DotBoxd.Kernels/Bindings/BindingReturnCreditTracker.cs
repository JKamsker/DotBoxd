namespace DotBoxd.Kernels;

internal sealed class BindingReturnCreditTracker
{
    private readonly Stack<Dictionary<string, int>> _stringCredits = new();

    public IDisposable BeginScope()
    {
        _stringCredits.Push(new Dictionary<string, int>(StringComparer.Ordinal));
        return new Scope(this);
    }

    public void RecordString(string value)
    {
        if (_stringCredits.TryPeek(out var credits))
        {
            credits[value] = credits.TryGetValue(value, out var count) ? count + 1 : 1;
        }
    }

    public bool TryConsume(SandboxValue value)
    {
        if (value is not StringValue text ||
            !_stringCredits.TryPeek(out var credits) ||
            !credits.TryGetValue(text.Value, out var count))
        {
            return false;
        }

        if (count == 1)
        {
            credits.Remove(text.Value);
        }
        else
        {
            credits[text.Value] = count - 1;
        }

        return true;
    }

    private void EndScope()
    {
        if (_stringCredits.Count > 0)
        {
            _stringCredits.Pop();
        }
    }

    private sealed class Scope(BindingReturnCreditTracker owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                owner.EndScope();
                _disposed = true;
            }
        }
    }
}
