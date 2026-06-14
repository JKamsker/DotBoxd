namespace SafeIR.Hosting;

using SafeIR.Compiler;

internal readonly record struct CompiledExecutable(CompiledArtifact Artifact, string MaterializationStatus);
