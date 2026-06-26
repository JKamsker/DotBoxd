namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private sealed partial class RecordShape
    {
        private object ConstructFromArguments(object?[] arguments)
        {
            var assigned = new bool[Fields.Count];
            var instance = ConstructInstance(arguments, assigned);
            for (var i = 0; i < Fields.Count; i++)
            {
                if (!assigned[i] && Fields[i].IsSettable)
                {
                    Fields[i].SetValue(instance, arguments[i]);
                    assigned[i] = true;
                }
            }

            for (var i = 0; i < Fields.Count; i++)
            {
                if (!assigned[i])
                {
                    VerifyReadOnlyField(instance, Fields[i], arguments[i]);
                }
            }

            return instance;
        }

        private object ConstructInstance(object?[] arguments, bool[] assigned)
        {
            if (_constructor is null)
            {
                return Activator.CreateInstance(_type)
                    ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
            }

            var parameters = _constructor.GetParameters();
            var constructorArguments = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = _constructorMap[i];
                constructorArguments[i] = arguments[fieldIndex];
                assigned[fieldIndex] = true;
            }

            return _constructor.Invoke(constructorArguments)
                ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
        }

        private void VerifyReadOnlyField(object instance, RecordMember field, object? expected)
        {
            var actual = field.GetValue(instance);
            if (!Equals(actual, expected))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{_type}' field '{field.Name}' is private or read-only and could not be reconstructed.");
            }
        }
    }
}
