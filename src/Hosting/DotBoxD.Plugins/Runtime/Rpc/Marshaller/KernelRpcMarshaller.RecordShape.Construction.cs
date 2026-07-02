namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private sealed partial class RecordShape
    {
        private object ConstructFromArguments(object?[] arguments)
        {
            var instance = ConstructInstance(arguments);
            for (var i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].IsSettable)
                {
                    Fields[i].SetValue(instance, arguments[i]);
                }
            }

            for (var i = 0; i < Fields.Count; i++)
            {
                if (!Fields[i].IsSettable)
                {
                    VerifyReadOnlyField(instance, Fields[i], arguments[i]);
                }
            }

            return instance;
        }

        private object ConstructInstance(object?[] arguments)
        {
            if (_constructor is null)
            {
                if (!HasPublicParameterlessConstructor())
                {
                    throw new NotSupportedException(
                        $"Server extension DTO '{_type}' does not expose a public parameterless constructor " +
                        "or a public constructor matching its public fields.");
                }

                return Activator.CreateInstance(_type)
                    ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
            }

            var parameters = _constructor.GetParameters();
            var constructorArguments = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = _constructorMap[i];
                if (fieldIndex < 0)
                {
                    constructorArguments[i] = DefaultParameterValue(parameters[i]);
                    continue;
                }

                constructorArguments[i] = arguments[fieldIndex];
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
