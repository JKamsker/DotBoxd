# References

These references informed the .NET-specific parts of the spec.

## .NET assembly loading

- Microsoft Docs — `AssemblyLoadContext.LoadFromStream` loads a COFF-based managed assembly image, not arbitrary raw IL bytes.
  - https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext.loadfromstream

## Persisting Reflection.Emit assemblies

- Microsoft Docs — `System.Reflection.Emit.PersistedAssemblyBuilder` can save generated assemblies to a stream/file.
  - https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-reflection-emit-persistedassemblybuilder

- Microsoft Docs — `PersistedAssemblyBuilder.Save`.
  - https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.persistedassemblybuilder.save

## AssemblyLoadContext and security boundary

- Microsoft Docs — `AppDomain` on .NET Core/.NET 5+ is limited; security boundaries should be provided by process boundaries and appropriate remoting techniques.
  - https://learn.microsoft.com/en-us/dotnet/api/system.appdomain

- Microsoft Docs — Plugin loading with `AssemblyLoadContext` is about isolating loaded assemblies into groups to avoid dependency conflicts/version conflicts, not about sandbox security.
  - https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support

## Code Access Security / partial trust

- Microsoft Docs — Code Access Security is not supported by modern .NET and is a .NET Framework-only concept.
  - https://learn.microsoft.com/en-us/dotnet/desktop/wpf/wpf-security-strategy-platform-security

## Metadata tooling

- Microsoft Docs — `System.Reflection.Metadata.Ecma335.MetadataBuilder` for assembly-generation/compiler tooling.
  - https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.ecma335.metadatabuilder
