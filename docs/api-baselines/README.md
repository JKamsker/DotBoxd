# Public API baselines

These files are the release gate for public package API compatibility.

Run `./scripts/check-api-compat-baseline.ps1` to compare the current public/protected source declarations for each shipped package against the checked-in baseline.

Run `./scripts/check-api-compat-baseline.ps1 -Update` only when a public API change is intentional and the release/versioning decision is documented in the associated change.
