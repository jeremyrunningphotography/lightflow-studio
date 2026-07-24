# Install the planning package

This package is designed to be copied into the root of your local
`LightflowStudio` repository.

## 1. Extract and copy

Copy these folders and files into the repository root:

- `docs/`
- `.github/`
- `scripts/Initialize-GitHubBacklog.ps1`
- `INSTALL.md` may be kept temporarily or removed after setup

The package does not replace the existing application README or source files.

## 2. Review changes

From the repository root:

```powershell
git status
git diff -- docs .github scripts
```

## 3. Commit the documentation

```powershell
git add docs .github scripts/Initialize-GitHubBacklog.ps1
git commit -m "Add product planning documentation and backlog tooling"
git push
```

## 4. Install and authenticate GitHub CLI

```powershell
winget install GitHub.cli
gh auth login
```

Choose GitHub.com, HTTPS, and browser authentication.

## 5. Create labels and issues

From the repository root:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\Initialize-GitHubBacklog.ps1
```

The script is safe to rerun. It updates labels and skips issues whose titles already exist.

## 6. Optional GitHub Project

Create a Project manually and add the generated issues. Suggested fields and values are
documented in `docs/BACKLOG_WORKFLOW.md`.
