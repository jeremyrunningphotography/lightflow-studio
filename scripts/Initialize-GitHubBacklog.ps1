param(
    [string]$Repo = "jeremysrunning/LightflowStudio"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is not installed. Install it with: winget install GitHub.cli"
}

gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI is not authenticated. Run: gh auth login"
}

$labels = @'
[
  {
    "name": "area:ai",
    "color": "a2eeef",
    "description": "Area Ai"
  },
  {
    "name": "area:files",
    "color": "c2e0c6",
    "description": "Area Files"
  },
  {
    "name": "area:images",
    "color": "fbca04",
    "description": "Area Images"
  },
  {
    "name": "area:integrity",
    "color": "f9d0c4",
    "description": "Area Integrity"
  },
  {
    "name": "area:metadata",
    "color": "bfdadc",
    "description": "Area Metadata"
  },
  {
    "name": "area:platform",
    "color": "0052cc",
    "description": "Area Platform"
  },
  {
    "name": "area:publishing",
    "color": "006b75",
    "description": "Area Publishing"
  },
  {
    "name": "area:ux",
    "color": "e99695",
    "description": "Area Ux"
  },
  {
    "name": "area:video",
    "color": "b60205",
    "description": "Area Video"
  },
  {
    "name": "area:workflow",
    "color": "7057ff",
    "description": "Area Workflow"
  },
  {
    "name": "effort:epic",
    "color": "d4c5f9",
    "description": "Effort Epic"
  },
  {
    "name": "effort:large",
    "color": "f9d0c4",
    "description": "Effort Large"
  },
  {
    "name": "effort:medium",
    "color": "fef2c0",
    "description": "Effort Medium"
  },
  {
    "name": "effort:small",
    "color": "c2e0c6",
    "description": "Effort Small"
  },
  {
    "name": "priority:p0",
    "color": "b60205",
    "description": "Priority P0"
  },
  {
    "name": "priority:p1",
    "color": "d93f0b",
    "description": "Priority P1"
  },
  {
    "name": "priority:p2",
    "color": "fbca04",
    "description": "Priority P2"
  },
  {
    "name": "priority:p3",
    "color": "c5def5",
    "description": "Priority P3"
  },
  {
    "name": "release:0.9",
    "color": "bfd4f2",
    "description": "Release 0.9"
  },
  {
    "name": "release:1.0",
    "color": "0e8a16",
    "description": "Release 1.0"
  },
  {
    "name": "release:1.1",
    "color": "1d76db",
    "description": "Release 1.1"
  },
  {
    "name": "release:1.2",
    "color": "5319e7",
    "description": "Release 1.2"
  },
  {
    "name": "release:1.3",
    "color": "006b75",
    "description": "Release 1.3"
  },
  {
    "name": "release:future",
    "color": "ededed",
    "description": "Release Future"
  },
  {
    "name": "type:architecture",
    "color": "1d76db",
    "description": "Type Architecture"
  },
  {
    "name": "type:bug",
    "color": "d73a4a",
    "description": "Type Bug"
  },
  {
    "name": "type:documentation",
    "color": "0075ca",
    "description": "Type Documentation"
  },
  {
    "name": "type:enhancement",
    "color": "84b6eb",
    "description": "Type Enhancement"
  },
  {
    "name": "type:epic",
    "color": "5319e7",
    "description": "Type Epic"
  },
  {
    "name": "type:feature",
    "color": "0e8a16",
    "description": "Type Feature"
  },
  {
    "name": "type:research",
    "color": "d4c5f9",
    "description": "Type Research"
  }
]
'@ | ConvertFrom-Json

foreach ($label in $labels) {
    Write-Host "Configuring label $($label.name)..."
    gh label create $label.name --repo $Repo --color $label.color --description $label.description --force | Out-Null
}

$issues = @'
[
  {
    "title": "Epic: Application framework and shared job system",
    "body": "Create the shared architecture needed for all Lightflow Studio capabilities.\n\nSee `docs/ARCHITECTURE.md` and `docs/ROADMAP.md`.\n\nIncludes shared job planning, execution, progress, cancellation, result summaries, history, presets, validation, and output safety.",
    "labels": [
      "type:epic",
      "area:platform",
      "priority:p0",
      "effort:epic",
      "release:0.9"
    ]
  },
  {
    "title": "Epic: Video processing",
    "body": "Coordinate video inspection, conversion, recovery, proxy, verification, thumbnail, audio, and export work.\n\nSee `docs/capabilities/VIDEO_PROCESSING.md`.",
    "labels": [
      "type:epic",
      "area:video",
      "priority:p1",
      "effort:epic",
      "release:1.0"
    ]
  },
  {
    "title": "Epic: Image processing",
    "body": "Introduce RAW conversion, resize, optimization, watermarking, and image contact sheets.\n\nSee `docs/capabilities/IMAGE_PROCESSING.md`.",
    "labels": [
      "type:epic",
      "area:images",
      "priority:p2",
      "effort:epic",
      "release:1.1"
    ]
  },
  {
    "title": "Epic: File organization and integrity",
    "body": "Coordinate rename, compare, sync, duplicate detection, checksums, and copy verification.\n\nSee `docs/capabilities/FILE_ORGANIZATION.md`.",
    "labels": [
      "type:epic",
      "area:files",
      "area:integrity",
      "priority:p2",
      "effort:epic",
      "release:1.2"
    ]
  },
  {
    "title": "Epic: Metadata",
    "body": "Coordinate metadata browsing, export, editing, normalization, and derivative copying.\n\nSee `docs/capabilities/METADATA.md`.",
    "labels": [
      "type:epic",
      "area:metadata",
      "priority:p2",
      "effort:epic",
      "release:1.2"
    ]
  },
  {
    "title": "Epic: Workflow pipelines",
    "body": "Create reusable multi-step workflows using stable Lightflow Studio capabilities.\n\nSee `docs/capabilities/WORKFLOW_AUTOMATION.md`.",
    "labels": [
      "type:epic",
      "area:workflow",
      "priority:p2",
      "effort:epic",
      "release:1.3"
    ]
  },
  {
    "title": "Epic: Gallery and publishing",
    "body": "Create local galleries, derivatives, manifests, QR codes, and optional publishing adapters.\n\nSee `docs/capabilities/GALLERY_PUBLISHING.md`.",
    "labels": [
      "type:epic",
      "area:publishing",
      "priority:p3",
      "effort:epic",
      "release:future"
    ]
  },
  {
    "title": "Epic: AI-assisted tools",
    "body": "Research optional culling, focus, similarity, keywording, face, and caption tools.\n\nSee `docs/capabilities/AI_ASSISTED_TOOLS.md`.",
    "labels": [
      "type:epic",
      "area:ai",
      "priority:p3",
      "effort:epic",
      "release:future"
    ]
  },
  {
    "title": "Formalize the shared job model",
    "body": "Define typed job definitions, items, plans, executions, progress, results, cancellation, retry, resume, and history behavior.\n\nSee `docs/ARCHITECTURE.md`.",
    "labels": [
      "type:architecture",
      "area:platform",
      "priority:p0",
      "effort:large",
      "release:0.9"
    ]
  },
  {
    "title": "Add persistent job history",
    "body": "Store prior job plans and outcomes so users can review completed, warned, skipped, cancelled, and failed work and quickly rerun appropriate jobs.",
    "labels": [
      "type:feature",
      "area:platform",
      "priority:p1",
      "effort:medium",
      "release:0.9"
    ]
  },
  {
    "title": "Add preset import and export",
    "body": "Allow user-created processing presets to be backed up, shared, and restored with format versioning.",
    "labels": [
      "type:feature",
      "area:platform",
      "priority:p1",
      "effort:medium",
      "release:0.9"
    ]
  },
  {
    "title": "Refine LUT batch processing",
    "body": "Bring the existing implementation into full alignment with the specification.\n\nSee `docs/features/video/lut-batch-processing.md`.",
    "labels": [
      "type:enhancement",
      "area:video",
      "priority:p1",
      "effort:medium",
      "release:1.0"
    ]
  },
  {
    "title": "Improve video salvage diagnostics and stream handling",
    "body": "Improve recovery reporting, multi-audio-stream selection, partial-success language, and retained-stream diagnostics.\n\nSee `docs/features/video/video-salvage.md`.",
    "labels": [
      "type:enhancement",
      "area:video",
      "priority:p1",
      "effort:large",
      "release:1.0"
    ]
  },
  {
    "title": "Expand proxy generation",
    "body": "Add configurable proxy resolutions, codecs, audio policies, folder mapping, and relink-friendly reports.\n\nSee `docs/features/video/proxy-generation.md`.",
    "labels": [
      "type:enhancement",
      "area:video",
      "priority:p2",
      "effort:medium",
      "release:1.0"
    ]
  },
  {
    "title": "Expand media verification",
    "body": "Add fast inspection, severity classification, optional hashes, rerun behavior, and richer reports.\n\nSee `docs/features/video/media-verification.md`.",
    "labels": [
      "type:enhancement",
      "area:integrity",
      "area:video",
      "priority:p1",
      "effort:medium",
      "release:1.0"
    ]
  },
  {
    "title": "Research RAW decoding engine",
    "body": "Evaluate supported camera formats, quality, metadata handling, licensing, packaging, performance, and long-term maintenance. Produce an ADR selecting the engine.",
    "labels": [
      "type:research",
      "area:images",
      "priority:p1",
      "effort:medium",
      "release:1.1"
    ]
  },
  {
    "title": "Implement RAW converter",
    "body": "Implement the RAW conversion workflow after the decoding engine ADR.\n\nSee `docs/features/images/raw-converter.md`.",
    "labels": [
      "type:feature",
      "area:images",
      "priority:p1",
      "effort:large",
      "release:1.1"
    ]
  },
  {
    "title": "Implement image resize",
    "body": "Add batch image resize with presets, sharpening, metadata handling, naming, and preflight dimensions.\n\nSee `docs/features/images/image-resize.md`.",
    "labels": [
      "type:feature",
      "area:images",
      "priority:p1",
      "effort:medium",
      "release:1.1"
    ]
  },
  {
    "title": "Implement bulk rename",
    "body": "Add metadata-aware naming templates with live preview, collision protection, and an undo manifest.\n\nSee `docs/features/files/bulk-rename.md`.",
    "labels": [
      "type:feature",
      "area:files",
      "priority:p1",
      "effort:large",
      "release:1.2"
    ]
  },
  {
    "title": "Implement copy verification",
    "body": "Compare source and destination trees using size checks and optional SHA-256 verification.\n\nSee `docs/features/files/copy-verification.md`.",
    "labels": [
      "type:feature",
      "area:integrity",
      "priority:p1",
      "effort:medium",
      "release:1.2"
    ]
  },
  {
    "title": "Implement metadata browser",
    "body": "Add friendly and raw metadata views, multi-file comparison, searching, and CSV/JSON export.\n\nSee `docs/features/metadata/metadata-browser.md`.",
    "labels": [
      "type:feature",
      "area:metadata",
      "priority:p1",
      "effort:large",
      "release:1.2"
    ]
  },
  {
    "title": "Implement workflow pipeline engine",
    "body": "Create a versioned, validated, resumable pipeline system for stable capability steps.\n\nSee `docs/features/workflow/pipeline-engine.md`.",
    "labels": [
      "type:feature",
      "area:workflow",
      "priority:p1",
      "effort:epic",
      "release:1.3"
    ]
  }
]
'@ | ConvertFrom-Json

$existingTitles = @(
    gh issue list --repo $Repo --state all --limit 500 --json title |
        ConvertFrom-Json |
        ForEach-Object { $_.title }
)

foreach ($issue in $issues) {
    if ($existingTitles -contains $issue.title) {
        Write-Host "Skipping existing issue: $($issue.title)"
        continue
    }

    $args = @(
        "issue", "create",
        "--repo", $Repo,
        "--title", $issue.title,
        "--body", $issue.body
    )

    foreach ($label in $issue.labels) {
        $args += @("--label", $label)
    }

    Write-Host "Creating issue: $($issue.title)"
    & gh @args
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create issue: $($issue.title)"
    }
}

Write-Host ""
Write-Host "Backlog initialization complete."
