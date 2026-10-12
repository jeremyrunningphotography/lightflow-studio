# Open evidence requests after the historical-source supplement

Prepared by LF-ANY-RES-017 for [#428](https://github.com/jeremyrunningphotography/lightflow-studio/issues/428), revised 2026-10-12. The [issue supplement](https://github.com/jeremyrunningphotography/lightflow-studio/issues/428) supplies dated **conversation summaries** for July 20–22. These questions seek original provenance and resolve only claims the summaries or GitHub record cannot establish. [#429](https://github.com/jeremyrunningphotography/lightflow-studio/issues/429) remains a separate Backlog assignment pending owner review.

## Targeted original-conversation retrieval

Use ChatGPT conversation search or an owner-designated export for the July 20 Kyno export discussion, July 21 DJI LUT and AI diagnostic discussion, and July 22 Premiere/recovery, separate-clip export, Toolkit, v0.1, and WPF v0.2 discussion. The #428 supplement gives exact UTC timestamps for ten entries. For each, return a conversation ID or stable reference, original message timestamp and role, and either a small owner-reviewed redacted excerpt or a fact table keyed to the message. Confirm whether these events were in one thread or several, and whether any earlier related discussion survives. Do not invent IDs or paste full private transcripts into the repository. Remove media names, identifying paths, credentials, and unrelated private material before citation.

The current account distinguishes user observations and decisions from the assistant's suspected Kyno diagnosis and ZIP delivery claims. Original messages may clarify wording and chronology; inspect source separately to determine whether and how the requested --salvage/PowerShell switch was implemented. If messages cannot be retrieved, cite the dated #428 summary as indirect evidence and preserve that limit.

## Artifact and technical checks

1. Search owner-designated ChatGPT downloads or backups for the assistant-reported July 22 Python/FFmpeg v0.1 ZIP and WPF v0.2 ZIP. For any candidate, record original filename, hash, internal version, timestamps, and custody privately. Inspect source/build evidence only within an approved safe workspace. The current record does not prove download, compilation, successful run, or feature behavior.
2. Treat the July 21 unsupported INSERT protocol/threading explanation as an assistant diagnostic hypothesis. Establish a Kyno root cause only if original diagnostic evidence or an independent reproduction supports it; otherwise describe the observed LUT export behavior and leave cause unresolved.
3. Find original July Codex/agent session IDs or exports, if any. Verify the model/tool, first prompts, and division of human direction versus AI implementation. July 22 agent-prefixed PR branches do not prove these details.
4. If the repository rename date matters to the Chronicle, inspect repository rename events or contemporary owner records. [PR #5](https://github.com/jeremyrunningphotography/lightflow-studio/pull/5) proposes a post-merge rename but does not date its completion.
5. For July validation and distribution claims, locate surviving GitHub Actions runs, local build/package records, and owner acceptance notes. Early PR descriptions report tests and publishing; they do not establish owner hands-on review. The first surviving release tag is [v0.15.0](https://github.com/jeremyrunningphotography/lightflow-studio/releases/tag/v0.15.0) on August 10.

## Owner questions only if primary sources cannot answer

- Was a v0.1 ZIP actually downloaded, opened, built, or run, and is the original file available for private inspection?
- Did the July 20–22 discussions constitute the first impetus, or were there earlier related conversations? The supplement identifies the earliest *recovered* workflow trouble, not necessarily the first inspiration.
- What may be cited publicly from private conversations, and what should remain summarized without quotations?
- What feedback or decision led from the first Toolkit source to the Lightflow name? The rename PR documents the change, but not all discussion behind it.

Record any owner answer as dated recollection unless supported by a contemporary source. The Git root verifies WPF v0.2 source at July 22, 5:39 p.m. PDT. It does not verify the earlier reported ZIPs, a successful run, the Kyno root cause, or exact AI/human division of labor.
