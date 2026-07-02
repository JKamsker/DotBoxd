using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private sealed partial class RecordShape
    {
        public void RejectUnmatchedRequiredConstructor()
        {
            if (_constructor is not null ||
                HasPublicParameterlessConstructor() ||
                HasConstructorParameterMappedToField())
            {
                return;
            }

            throw new NotSupportedException(
                $"Server extension DTO '{_type}' does not expose a public parameterless constructor " +
                "or a public constructor matching its public fields.");
        }

        private bool HasPublicParameterlessConstructor()
            => _type.IsValueType || _type.GetConstructor(Type.EmptyTypes) is not null;

        private bool HasConstructorParameterMappedToField()
            => _type.GetConstructors().Any(ConstructorHasMappedParameter);

        private bool ConstructorHasMappedParameter(ConstructorInfo constructor)
        {
            foreach (var parameter in constructor.GetParameters())
            {
                var fieldIndex = FieldIndex(Fields, parameter.Name);
                if (fieldIndex >= 0 && parameter.ParameterType == Fields[fieldIndex].Type)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
