using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// A DTO/record field for marshalling: a public property, or (for a field-only value type) a public field.
/// <see cref="Symbol"/> is the underlying property/field symbol for callers that need more than name and type.
/// </summary>
internal readonly record struct RecordMember(string Name, ITypeSymbol Type, ISymbol Symbol);
