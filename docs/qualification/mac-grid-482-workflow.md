# LF-MAC-DEV-038 workflow reconciliation

2026-11-05; baseline 1b37d62fe1bb153e6988661ce65453648d937551.

Before: Draft-first workflow, Draft-specific Project checks, registry-only reconciliation exception, existing-PR history wording, repeated handoff rules and unqualified Windows packaging instructions conflicted with the owner assignment.

After: independent clone/assignment → local implementation/tests/package → ZIP owner acceptance/iterations → independent exact-source Windows qualification when applicable → final documentation/registry/history → one implementation PR → exact-head CI → explicit merge authorization → normal merge → post-merge CI → issue/Project cleanup. Post-merge provenance stays in metadata/comments. Routine administrative/qualification/history PRs require explicit owner exception.

Retained Catalog lifecycle, package preflight, supported creation, single-writer safety and release/branch protection remain operative. Mac and Windows packaging responsibilities are explicit. Documentation-only acceptance validity is distinguished from production/validation changes; root AGENTS requires full CI. Task-owned desktop verification is authorized by this assignment.

Validation: complete root policy reviewed; no operative Draft-first instruction remains; one coherent routine workflow, platform validation and ZIP handoff rules. Historical registry checkpoints are preserved as history, not current instructions. Whitespace checks pass. Full CI remains outstanding for the eventual single implementation PR; no CI bypass or acceptance claimed. This local policy commit precedes feature implementation.
