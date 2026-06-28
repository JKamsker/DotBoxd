using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal sealed class BindingReturnCreditTracker
{
    private readonly List<ScopeCredits> _scopes = [];
    private int _nextScopeId;

    public Scope BeginScope()
    {
        var id = unchecked(++_nextScopeId);
        _scopes.Add(new ScopeCredits(id));
        return new Scope(this, id);
    }

    public void RecordString(string value)
    {
        var index = _scopes.Count - 1;
        if (index < 0)
        {
            return;
        }

        var credits = _scopes[index];
        credits.Record(value);
        _scopes[index] = credits;
    }

    public bool TryConsume(SandboxValue value)
    {
        if (value is not StringValue text ||
            _scopes.Count == 0)
        {
            return false;
        }

        var index = _scopes.Count - 1;
        var credits = _scopes[index];
        if (!credits.TryConsume(text.Value))
        {
            return false;
        }

        _scopes[index] = credits;
        return true;
    }

    private void EndScope(int id)
    {
        for (var index = _scopes.Count - 1; index >= 0; index--)
        {
            if (_scopes[index].Id == id)
            {
                _scopes.RemoveAt(index);
                break;
            }
        }
    }

    public readonly struct Scope(BindingReturnCreditTracker? owner, int id) : IDisposable
    {
        public void Dispose() => owner?.EndScope(id);
    }

    private struct ScopeCredits
    {
        private string? _singleValue;
        private int _singleCount;
        private Dictionary<string, int>? _additionalValues;

        public ScopeCredits(int id)
            => Id = id;

        public int Id { get; }

        public void Record(string value)
        {
            if (_singleCount == 0 && _additionalValues is null)
            {
                _singleValue = value;
                _singleCount = 1;
                return;
            }

            if (_singleCount > 0 && string.Equals(_singleValue, value, StringComparison.Ordinal))
            {
                _singleCount++;
                return;
            }

            _additionalValues ??= new Dictionary<string, int>(StringComparer.Ordinal);
            _additionalValues[value] = _additionalValues.TryGetValue(value, out var count) ? count + 1 : 1;
        }

        public bool TryConsume(string value)
        {
            if (_singleCount > 0 && string.Equals(_singleValue, value, StringComparison.Ordinal))
            {
                _singleCount--;
                if (_singleCount == 0)
                {
                    _singleValue = null;
                }

                return true;
            }

            if (_additionalValues is null ||
                !_additionalValues.TryGetValue(value, out var count))
            {
                return false;
            }

            if (count == 1)
            {
                _additionalValues.Remove(value);
            }
            else
            {
                _additionalValues[value] = count - 1;
            }

            return true;
        }
    }
}
