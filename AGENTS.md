# Local Codex instructions

## AI development workflow

- GitHub issues are the durable product and implementation specification: what to build.
- Agent/Codex kickoff prompts describe how to execute the issue safely; do not duplicate full issue specifications in them.
- When review changes a product or architecture decision, update the issue first, then execute against it.
- Draft PRs stay unmerged until explicit architecture and hands-on acceptance.
- Materially separate issues normally begin in a clean agent conversation after the prior issue is merged.
- Follow the branch → implementation → validation → Draft PR → review → explicit acceptance → merge/cleanup rhythm.

## PR preparation and functional-test artifact

- Jeremy functionally tests every change by running `artifacts\release\LightflowStudio\LightflowStudio.exe`.
- Before reporting any PR as ready, always rebuild that exact local packaged executable from the PR branch with:
  `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1 -Mode PullRequest -SkipInstaller`
- Treat a successful `dotnet build`, `dotnet test`, or GitHub Actions artifact as additional validation, not as a substitute for refreshing the local executable above.
- Confirm the packaged startup smoke test and dependency validation pass, verify the executable timestamp is newer than the PR commit, and confirm no packaging smoke-test process remains running.
- If the executable or packaged FFmpeg files are locked, inspect for a hidden leftover `LightflowStudio.exe`; obtain approval before terminating a user-started process, then rebuild.
- Include the refreshed local executable path in the final PR handoff so it is immediately clear which binary Jeremy should test.
