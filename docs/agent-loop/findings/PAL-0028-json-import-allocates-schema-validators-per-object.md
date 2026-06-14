---
id: PAL-0028
area: perf_alloc
status: open
priority: medium
title: JSON import allocates schema validators per object
dedup_key: alloc/json-import/property-schema-validation/per-object
created_at: 2026-06-12T22:33:57.8732015+00:00
created_by: performance-producer
created_commit: 
updated_at: 2026-06-12T22:33:57.8732015+00:00
claimed_by: 
claimed_at: 
claim_branch: 
fixed_by: 
fixed_at: 
fixed_commit: 
verified_by: 
verified_at: 
verified_commit: 
duplicate_of: 
---

# PAL-0028: JSON import allocates schema validators per object

## Claim

JSON module and plugin package import allocates schema metadata and duplicate-detection state for every object shape check. Each `RequireAllowedProperties` call receives a freshly allocated `string[]` from the collection expression at the call site, then scans that array linearly for every JSON property after allocating a new duplicate-property `HashSet` for the same object.

## Evidence

- `src/DotBoxd.Kernels.Serialization.Json/JsonImport.cs:116` declares `RequireAllowedProperties(JsonElement value, string name, params string[] allowed)`, so every call with `[...]` collection syntax materializes a new `string[]` of allowed property names.
- `src/DotBoxd.Kernels.Serialization.Json/JsonImport.cs:118` calls `RequireUniqueProperties` before the allowlist scan, and `src/DotBoxd.Kernels.Serialization.Json/JsonImport.cs:129` creates a new `HashSet<string>` for every JSON object being validated.
- `src/DotBoxd.Kernels.Serialization.Json/JsonImport.cs:119` through `src/DotBoxd.Kernels.Serialization.Json/JsonImport.cs:121` enumerate the object's properties and call `allowed.Contains(property.Name, StringComparer.Ordinal)`, which linearly scans the per-call allowed-name array for each property.
- `DotBoxdJsonImporter` calls this shape validator throughout the import recursion: module root at `src/DotBoxd.Kernels.Serialization.Json/DotBoxdJsonImporter.cs:34`, functions at `:93`, parameters at `:125`, statement shapes at `:168`, `:177`, `:183`, `:189`, `:199`, and `:208`, expression shapes at `:222`, `:228`, `:234`, `:240`, and `:249`, and generic type objects at `:302`.
- `PluginPackageJsonSerializer` repeats the same pattern for package, manifest, live setting, subscription, and entrypoint objects at `src/DotBoxd.Kernels.Serialization.Json/PluginPackageJsonSerializer.cs:166`, `:178`, `:238`, `:299`, and `:307`.
- This is distinct from `ALG-0001`, which tracks source-map raw-text searches during module import, and from `ALG-0006`, which tracks plugin package module subtree reparsing. Even with those fixed, every imported object still pays fresh allowed-name arrays, duplicate `HashSet` allocation, and per-property linear allowlist scans.

## Impact

Generated IR and plugin packages can contain thousands of small statement/expression/type objects. The importer currently allocates at least one schema array and one duplicate-property set for nearly every object node before constructing the model object. For large modules near the import budget, that creates avoidable Gen0 pressure and O(properties * allowed-property-count) string comparisons on top of the actual parsing/model construction work.

## Better target

Make allowed-property schemas static cached sets or specialized validators per shape. For small fixed shapes, use switch-based property validation or precomputed `FrozenSet<string>`/`HashSet<string>` instances instead of per-call `params` arrays. If duplicate detection remains required, combine duplicate and allowlist validation in one pass with pooled or stack-friendly state for small objects.

## Benchmark/allocation test idea

Extend JSON import benchmarks with generated modules containing 100, 1,000, and 10,000 simple expression/statement objects, plus plugin packages with live settings/subscriptions. Measure allocated bytes in `DotBoxdJsonImporter.Import` and `PluginPackageJsonSerializer.Import`, and assert schema validation does not allocate a new allowed-name array and duplicate `HashSet` per object node.
