# Lightflow Studio Documentation

This directory supports Lightflow Studio's product and technical authorities.
GitHub issues define current requirements and acceptance criteria; accepted `main`
defines delivered behavior. Native issue relationships and the
[Roadmap Project](https://github.com/users/jeremyrunningphotography/projects/4) define
current planning and disposition. Follow [AGENTS.md](../AGENTS.md) for the workflow.
Documentation records shared contracts, design context and evidence; it does not
supersede current issue decisions or maintain a competing live roadmap.

## Core documents

- [Product Vision](PRODUCT_VISION.md)
- [Historical planning snapshot](ROADMAP.md)
- [Current system map and architecture authority index](ARCHITECTURE.md)
- [UI Guidelines](UI_GUIDELINES.md)
- [Release Planning](RELEASE_PLAN.md)
- [Backlog and GitHub workflow](BACKLOG_WORKFLOW.md)
- [Mac compatibility research and accepted planning](research/mac-compatibility/README.md)

## Shared Windows/Mac architecture

The target direction is owner-selected; production remains Windows WPF until qualified migration slices are accepted. Proposed implementation and estimates are not execution approval.

- [Architecture Decision Records](decisions/README.md)
- [Shared boundaries, dependency map and governance](architecture/shared-product-boundaries.md)
- [Proposed migration, R1/WQ and qualification gates](architecture/migration-and-qualification.md)
- [Immutable convergence evidence and acceptance chronology](architecture/convergence-evidence.md)

## Capability specifications

- [Video Processing](capabilities/VIDEO_PROCESSING.md)
- [Image Processing](capabilities/IMAGE_PROCESSING.md)
- [File Organization](capabilities/FILE_ORGANIZATION.md)
- [Metadata](capabilities/METADATA.md)
- [Workflow Automation](capabilities/WORKFLOW_AUTOMATION.md)
- [Gallery and Publishing](capabilities/GALLERY_PUBLISHING.md)
- [AI-Assisted Tools](capabilities/AI_ASSISTED_TOOLS.md)

## Feature specifications

Controller setup: [TourBox Elite Plus Console mapping and physical acceptance](tourbox/README.md).
This proposed supported-preset path consumes the accepted semantic shortcuts; hardware acceptance
and a real Console export remain pending under #354.

Feature specifications live under `features/` and describe behavior, acceptance criteria,
dependencies, and future expansion. Issues may link to reusable specifications, but
current issue decisions and acceptance criteria remain authoritative. Check disposition
before treating older future scope as a commitment.
