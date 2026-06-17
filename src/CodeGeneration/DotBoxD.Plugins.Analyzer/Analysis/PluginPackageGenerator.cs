using System.Collections.Immutable;
using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.PluginServer;
using DotBoxD.Plugins.Analyzer.Analysis.Registration;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

[Generator(LanguageNames.CSharp)]
public sealed class PluginPackageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pluginAttributeResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDGenerationNames.Metadata.PluginAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        var eventKernelAttributeResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDGenerationNames.Metadata.EventKernelAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        var modelResults = pluginAttributeResults.Collect()
            .Combine(eventKernelAttributeResults.Collect())
            .SelectMany(static (pair, _) => pair.Left.AddRange(pair.Right));

        var diagnostics = modelResults
            .Where(static result => result.Diagnostic is not null)
            .Select(static (result, _) => result.Diagnostic!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.DiagnosticResult);
        context.RegisterSourceOutput(diagnostics, static (context, diagnostic) =>
            context.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var models = modelResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.ModelResult);

        // Phase C: lower inline On<TEvent>().Where?(lambda).Select?(lambda).Run(lambda) chains
        // to the same PluginKernelModel a kernel class produces. Unsupported shapes fail safe (null).
        var chainResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Run" }
                },
                static (syntaxContext, ct) => HookChainModelFactory.Create(syntaxContext, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        // Server extensions: lower a [ServerExtension] class's batch method to a verified-IR package
        // whose Create() imports the JSON. Unsupported shapes emit a diagnostic and no package.
        var rpcResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDGenerationNames.Metadata.ServerExtensionAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => RpcKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            rpcResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var rpcPackages = rpcResults
            .Where(static result => result.Package is not null)
            .Select(static (result, _) => result.Package!);

        var invokeAsyncResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }
                },
                static (syntaxContext, ct) => InvokeAsyncModelFactory.Create(syntaxContext, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        context.RegisterSourceOutput(
            invokeAsyncResults.Select(static (result, _) => result.Package),
            static (sourceContext, package) => sourceContext.AddSource(package.HintName, package.Source));

        var pluginServerResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDGenerationNames.Metadata.GeneratePluginServerAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginServerFacadeModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        context.RegisterSourceOutput(
            pluginServerResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        context.RegisterSourceOutput(
            pluginServerResults.Where(static result => result.Source is not null).Select(static (result, _) => result.Source!),
            static (sourceContext, source) => sourceContext.AddSource(source.HintName, source.Source));

        var packages = models
            .Collect()
            .Combine(chainResults.Select(static (result, _) => result.Model).Collect())
            .Combine(rpcPackages.Collect())
            .Select(static (pair, _) => CreatePackageBatch(pair.Left.Left, pair.Left.Right, pair.Right))
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);

        // Emit a C# interceptor per lowered chain so the Run call site installs + wires its
        // generated package (UseGeneratedChain) instead of throwing DBXK062.
        var interceptions = chainResults
            .Where(static result => result.Interception is not null)
            .Select(static (result, _) => result.Interception!)
            .Collect();
        var invokeAsyncInterceptions = invokeAsyncResults
            .Where(static result => result.Interception is not null)
            .Select(static (result, _) => result.Interception!)
            .Collect();
        var needsInterceptorAttribute = interceptions
            .Select(static (items, _) => !items.IsDefaultOrEmpty)
            .Combine(invokeAsyncInterceptions.Select(static (items, _) => !items.IsDefaultOrEmpty))
            .Select(static (pair, _) => pair.Left || pair.Right);
        var needsInterceptsLocationAttribute = needsInterceptorAttribute
            .Combine(context.CompilationProvider.Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.InterceptsLocationAttribute") is { } type &&
                SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)))
            .Select(static (pair, _) => pair.Left && !pair.Right);
        context.RegisterSourceOutput(
            needsInterceptsLocationAttribute,
            static (sourceContext, needsAttribute) =>
            {
                if (needsAttribute)
                {
                    InterceptsLocationAttributeEmitter.Emit(sourceContext);
                }
            });
        context.RegisterSourceOutput(
            interceptions,
            static (sourceContext, items) => DotBoxDHookChainInterceptorEmitter.Emit(sourceContext, items));
        context.RegisterSourceOutput(
            invokeAsyncInterceptions,
            static (sourceContext, items) => InvokeAsyncInterceptorEmitter.Emit(sourceContext, items));

        RegistrationAccumulatorGenerator.Register(context);

        context.RegisterSourceOutput(packages, static (context, batch) => {
            foreach (var diagnostic in batch.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                    Location.None,
                    diagnostic.Message));
            }

            foreach (var package in batch.Packages)
            {
                context.AddSource(package.HintName, package.Source);
            }
        });
    }

    private static GeneratedPluginPackageBatch CreatePackageBatch(
        ImmutableArray<PluginKernelModel> models,
        ImmutableArray<PluginKernelModel> chainModels,
        ImmutableArray<GeneratedPluginPackage> rpcPackages)
    {
        var sourcePackages = BuildSourcePackageArray(models, chainModels, rpcPackages);
        var duplicateIdentities = DuplicateIdentities(sourcePackages, out var duplicatePackageCount);
        var diagnostics = new GeneratedPluginPackageDiagnostic[duplicateIdentities.Count];
        var diagnosticIndex = 0;
        foreach (var duplicate in duplicateIdentities.Values)
        {
            var package = duplicate.FirstPackage;
            diagnostics[diagnosticIndex] = new GeneratedPluginPackageDiagnostic(
                $"Plugin package name '{package.PackageName}' is generated more than once in namespace '{NamespaceDisplay(package)}'.");
            diagnosticIndex++;
        }

        var packages = new GeneratedPluginPackage[sourcePackages.Length - duplicatePackageCount];
        var packageIndex = 0;
        foreach (var package in sourcePackages)
        {
            if (!duplicateIdentities.ContainsKey(PackageIdentity(package)))
            {
                packages[packageIndex] = package;
                packageIndex++;
            }
        }

        return new GeneratedPluginPackageBatch(
            EquatableArray<GeneratedPluginPackage>.FromOwned(packages),
            EquatableArray<GeneratedPluginPackageDiagnostic>.FromOwned(diagnostics));
    }

    private static GeneratedPluginPackage[] BuildSourcePackageArray(
        ImmutableArray<PluginKernelModel> models,
        ImmutableArray<PluginKernelModel> chainModels,
        ImmutableArray<GeneratedPluginPackage> rpcPackages)
    {
        var packages = new GeneratedPluginPackage[models.Length + chainModels.Length + rpcPackages.Length];
        var index = 0;
        foreach (var model in models)
        {
            packages[index] = DotBoxDPackageSourceEmitter.Emit(model);
            index++;
        }

        foreach (var model in chainModels)
        {
            packages[index] = DotBoxDPackageSourceEmitter.Emit(model);
            index++;
        }

        foreach (var package in rpcPackages)
        {
            packages[index] = package;
            index++;
        }

        return packages;
    }

    private static Dictionary<PackageIdentityKey, DuplicatePackageIdentity> DuplicateIdentities(
        IReadOnlyList<GeneratedPluginPackage> packages,
        out int duplicatePackageCount)
    {
        var counts = new Dictionary<PackageIdentityKey, DuplicatePackageIdentity>();
        foreach (var package in packages)
        {
            var identity = PackageIdentity(package);
            if (counts.TryGetValue(identity, out var duplicate))
            {
                counts[identity] = duplicate.Increment();
            }
            else
            {
                counts.Add(identity, new DuplicatePackageIdentity(package));
            }
        }

        duplicatePackageCount = 0;
        var duplicates = new Dictionary<PackageIdentityKey, DuplicatePackageIdentity>();
        foreach (var pair in counts)
        {
            if (pair.Value.Count > 1)
            {
                duplicatePackageCount += pair.Value.Count;
                duplicates.Add(pair.Key, pair.Value);
            }
        }

        return duplicates;
    }

    private static PackageIdentityKey PackageIdentity(GeneratedPluginPackage package)
        => new(package.Namespace, package.PackageName);

    private static string NamespaceDisplay(GeneratedPluginPackage package)
        => string.IsNullOrWhiteSpace(package.Namespace) ? "<global>" : package.Namespace;

    private readonly struct PackageIdentityKey : IEquatable<PackageIdentityKey>
    {
        public PackageIdentityKey(string @namespace, string packageName)
        {
            Namespace = @namespace;
            PackageName = packageName;
        }

        private string Namespace { get; }

        private string PackageName { get; }

        public bool Equals(PackageIdentityKey other)
            => string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
               string.Equals(PackageName, other.PackageName, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is PackageIdentityKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked {
                return ((Namespace?.GetHashCode() ?? 0) * 397) ^ (PackageName?.GetHashCode() ?? 0);
            }
        }
    }

    private readonly struct DuplicatePackageIdentity
    {
        public DuplicatePackageIdentity(GeneratedPluginPackage firstPackage)
        {
            FirstPackage = firstPackage;
            Count = 1;
        }

        private DuplicatePackageIdentity(GeneratedPluginPackage firstPackage, int count)
        {
            FirstPackage = firstPackage;
            Count = count;
        }

        public GeneratedPluginPackage FirstPackage { get; }

        public int Count { get; }

        public DuplicatePackageIdentity Increment()
            => new(FirstPackage, Count + 1);
    }
}
