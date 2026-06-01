# Changelog

## Unreleased

- Server-side exceptions that are not `ShaRpcException` now return a sanitized
  `Internal error.` / `ShaRpcInternalError` error payload instead of exposing the raw
  exception message and CLR exception type to remote callers.
