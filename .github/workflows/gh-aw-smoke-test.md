---
description: Minimal gh-aw Codex smoke test.

on:
  workflow_dispatch:

permissions:
  contents: read

network:
  allowed:
    - defaults
    - github
    - threat-detection

engine: codex

safe-outputs:
  scripts:
    record-smoke-result:
      description: Record that the gh-aw smoke test completed.
      inputs:
        status:
          description: Short smoke-test status.
          required: true
          type: string
      script: |
        const statusText = String(item.status || "").trim();
        core.info(`gh-aw smoke result: ${statusText}`);
        return { success: statusText === "ok", status: statusText };
  noop:
    report-as-issue: false
  missing-tool: false
  missing-data: false
  report-incomplete:
    create-issue: false
---

# gh-aw Smoke Test

Run a minimal smoke test for the gh-aw Codex setup.

Do not inspect, print, or summarize any secrets or environment variable values.
Do not modify repository files, create issues, create pull requests, add comments,
or make any other repository changes.

Complete the run by calling the `record_smoke_result` safe-output tool with
`status` set to `ok`, then call the `noop` safe-output tool with a concise
success message.
