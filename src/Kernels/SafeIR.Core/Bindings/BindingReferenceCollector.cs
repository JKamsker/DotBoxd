namespace SafeIR;

public static class BindingReferenceCollector
{
    public static IReadOnlySet<string> Collect(SandboxModule module, IBindingCatalog bindings)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var references in CollectByFunction(module, bindings).Values) {
            all.UnionWith(references);
        }

        return all;
    }

    public static IReadOnlySet<string> Collect(SandboxModule module, IBindingCatalog bindings, string? entrypoint)
    {
        if (entrypoint is null) {
            return Collect(module, bindings);
        }

        return CollectByFunction(module, bindings).TryGetValue(entrypoint, out var ids)
            ? ids
            : new HashSet<string>(StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> CollectByFunction(
        SandboxModule module,
        IBindingCatalog bindings)
    {
        var collector = new ModuleBindingReferenceCollector(module, bindings);
        return collector.Collect();
    }

    private static Dictionary<string, SandboxFunction> FunctionDictionary(IReadOnlyList<SandboxFunction> functions)
    {
        var dictionary = new Dictionary<string, SandboxFunction>(functions.Count, StringComparer.Ordinal);
        for (var i = 0; i < functions.Count; i++)
        {
            dictionary.Add(functions[i].Id, functions[i]);
        }

        return dictionary;
    }

    private static bool IsCollectionCall(string name)
        => name is "list.empty" or "list.of" or "list.count" or "list.get" or "list.add"
            or "map.empty" or "map.containsKey" or "map.get" or "map.set" or "map.remove";

    private sealed class ModuleBindingReferenceCollector
    {
        private readonly IBindingCatalog _bindings;
        private readonly Dictionary<string, SandboxFunction> _functions;
        private readonly Dictionary<string, HashSet<string>> _directBindings;
        private readonly Dictionary<string, List<string>> _callersByTarget = new(StringComparer.Ordinal);

        public ModuleBindingReferenceCollector(SandboxModule module, IBindingCatalog bindings)
        {
            _bindings = bindings;
            _functions = FunctionDictionary(module.Functions);
            _directBindings = new Dictionary<string, HashSet<string>>(_functions.Count, StringComparer.Ordinal);
        }

        public IReadOnlyDictionary<string, IReadOnlySet<string>> Collect()
        {
            foreach (var function in _functions.Values) {
                AnalyzeFunction(function);
            }

            var references = CopyDirectBindings();
            var pending = new Queue<string>(_functions.Keys);
            while (pending.Count > 0) {
                var target = pending.Dequeue();
                if (!references.TryGetValue(target, out var targetReferences) ||
                    targetReferences.Count == 0 ||
                    !_callersByTarget.TryGetValue(target, out var callers)) {
                    continue;
                }

                foreach (var caller in callers) {
                    if (references[caller].IsSupersetOf(targetReferences)) {
                        continue;
                    }

                    references[caller].UnionWith(targetReferences);
                    pending.Enqueue(caller);
                }
            }

            return CopyReferences(references);
        }

        private void AnalyzeFunction(SandboxFunction function)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            _directBindings.Add(function.Id, ids);
            CollectBlock(function, function.Body, ids);
        }

        private bool CollectStatement(SandboxFunction function, Statement statement, HashSet<string> ids)
        {
            switch (statement) {
                case AssignmentStatement assignment:
                    CollectExpression(function, assignment.Value, ids);
                    return false;
                case ReturnStatement ret:
                    CollectExpression(function, ret.Value, ids);
                    return true;
                case ExpressionStatement expression:
                    CollectExpression(function, expression.Value, ids);
                    return false;
                case IfStatement branch:
                    CollectExpression(function, branch.Condition, ids);
                    var thenReturns = CollectBlock(function, branch.Then, ids);
                    var elseReturns = CollectBlock(function, branch.Else, ids);
                    return thenReturns && elseReturns;
                case WhileStatement loop:
                    CollectExpression(function, loop.Condition, ids);
                    CollectBlock(function, loop.Body, ids);
                    return false;
                case ForRangeStatement range:
                    CollectExpression(function, range.Start, ids);
                    CollectExpression(function, range.End, ids);
                    CollectBlock(function, range.Body, ids);
                    return false;
                default:
                    return false;
            }
        }

        private bool CollectBlock(SandboxFunction function, IReadOnlyList<Statement> statements, HashSet<string> ids)
        {
            var alwaysReturns = false;
            foreach (var statement in statements) {
                if (alwaysReturns) {
                    continue;
                }

                alwaysReturns = CollectStatement(function, statement, ids);
            }

            return alwaysReturns;
        }

        private void CollectExpression(SandboxFunction function, Expression expression, HashSet<string> ids)
        {
            if (expression is CallExpression call) {
                foreach (var argument in call.Arguments) {
                    CollectExpression(function, argument, ids);
                }

                if (IsCollectionCall(call.Name)) {
                    return;
                }

                if (_functions.ContainsKey(call.Name)) {
                    RecordCall(function.Id, call.Name);
                }
                else if (_bindings.TryGet(call.Name, out _)) {
                    ids.Add(call.Name);
                }
            }
            else if (expression is UnaryExpression unary) {
                CollectExpression(function, unary.Operand, ids);
            }
            else if (expression is BinaryExpression binary) {
                CollectExpression(function, binary.Left, ids);
                CollectExpression(function, binary.Right, ids);
            }
        }

        private void RecordCall(string caller, string target)
        {
            if (!_callersByTarget.TryGetValue(target, out var callers)) {
                callers = [];
                _callersByTarget.Add(target, callers);
            }

            callers.Add(caller);
        }

        private Dictionary<string, HashSet<string>> CopyDirectBindings()
        {
            var copy = new Dictionary<string, HashSet<string>>(_directBindings.Count, StringComparer.Ordinal);
            foreach (var item in _directBindings) {
                copy.Add(item.Key, new HashSet<string>(item.Value, StringComparer.Ordinal));
            }

            return copy;
        }

        private static IReadOnlyDictionary<string, IReadOnlySet<string>> CopyReferences(
            IReadOnlyDictionary<string, HashSet<string>> references)
        {
            var copy = new Dictionary<string, IReadOnlySet<string>>(references.Count, StringComparer.Ordinal);
            foreach (var item in references) {
                copy.Add(item.Key, new HashSet<string>(item.Value, StringComparer.Ordinal));
            }

            return copy;
        }
    }
}
