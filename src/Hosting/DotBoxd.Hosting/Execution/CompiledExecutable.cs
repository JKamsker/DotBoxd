namespace DotBoxd.Hosting;

using DotBoxd.Kernels.Compiler;

internal readonly record struct CompiledExecutable(CompiledArtifact Artifact, string MaterializationStatus);
