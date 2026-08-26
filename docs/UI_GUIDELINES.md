# UI Guidelines

## Design goal

Lightflow Studio should feel like a calm professional workbench: powerful, clear, and
deliberately less intimidating than the tools it wraps.

## Navigation

Organize the application by user capability rather than implementation technology.

Suggested top-level navigation:

- Video
- Images
- Organize
- Metadata
- Verify
- Workflows
- Settings

Keep experimental tools visibly marked. Do not place mature and experimental behavior
behind identical visual treatment.

## Standard tool layout

Every processing tool should follow a familiar sequence:

1. **Choose input**
2. **Choose operation settings**
3. **Choose destination and naming**
4. **Review**
5. **Run**
6. **Review results**

Users should not have to relearn the overall flow for each capability.

## Progressive disclosure

- Show recommended settings first
- Keep advanced controls collapsible
- Explain consequences beside risky options
- Preserve the ability to inspect the generated command in logs or diagnostics
- Never expose a raw command-line option without context

## Preflight review

Before processing, show:

- Number of included files
- Skipped files
- Output destination
- Naming pattern
- Potential collisions
- Warnings
- Estimated output characteristics
- Active preset
- Recovery or destructive behavior

## Status language

Use explicit states:

- Waiting
- Inspecting
- Ready
- Processing
- Finishing
- Completed
- Completed with warnings
- Skipped
- Cancelled
- Failed

Avoid ambiguous states such as merely “Done” when warnings occurred.

## Error presentation

The primary error message should answer:

1. What failed?
2. Which file was affected?
3. What is the likely cause?
4. What can the user do next?
5. Where can detailed diagnostics be found?

Raw FFmpeg or tool output belongs in the activity log, not as the only user-facing message.

## Accessibility

- Full keyboard navigation
- Visible focus indicators
- Logical tab order
- Text labels in addition to icons
- Sufficient contrast
- No status conveyed by color alone
- Screen-reader-friendly names for progress and controls
- Respect Windows text scaling

## Destructive operations

Deletion, overwrite, synchronization, and metadata removal must:

- Be opt-in
- Show a preview
- State whether the operation can be undone
- Require a deliberate final confirmation
- Produce a result report

## Presets

Presets should show:

- Name
- Purpose
- Key settings
- Whether built-in or user-created
- Whether modified from the saved version

Support restore-to-recommended behavior and eventual import/export.

## Tone

Use direct, reassuring language.

Prefer:

> The output folder is not writable. Choose another folder or update its permissions.

Avoid:

> UnauthorizedAccessException while initializing destination.

## Focused configuration modals

Use an owned modal for a bounded action that needs substantial configuration but should not become a navigation destination. Keep the originating workspace alive behind it, use a compact two-column layout with primary choices visible and advanced backend controls behind disclosure, and keep validation close to the final action. Cancel must have no durable side effects. After an immutable request is accepted by application services, close immediately; execution progress belongs to Jobs rather than the modal.

Conditional settings must visually follow their typed authority. Hide or disable bitrate fields outside their rate-control mode, audio details when audio is disabled, and explicit values when Same as Source owns materialization. Use concise product terms and keep technical diagnostics available in preflight detail.
## Modern Export scheduling

The focused Export modal configures output intent only. Do not show per-submission Parallel exports: simultaneous
Export count is global Jobs execution policy. A multi-file acceptance remains all-or-nothing and closes only after
every independently materialized Job is admitted to the global queue; synchronous admission errors remain in the
modal. Future Jobs surfaces consume one stable row per media export, not nested submission/batch cards.

The canonical Jobs entry lives in the global bottom status bar. With non-terminal work it toggles a compact
right-side drawer without navigating away from Browser or Player; with no non-terminal work #170 temporarily routes
to History until #171 replaces that compatibility destination. Rows expose filename, textual state, ETA, and a
shape-plus-color radial state at a glance. Expansion reveals the complete output path and materialized settings.
Waiting reorder always updates scheduler order and includes explicit keyboard-focusable earlier/later buttons.
