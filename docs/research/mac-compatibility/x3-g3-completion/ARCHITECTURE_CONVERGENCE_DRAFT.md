# Architecture convergence — draft for owner acceptance

G1 Player and G2 initial P2 Catalog are owner-accepted PASS. This final X3 evidence recommends G3 PASS; #370/#366 remain open pending owner acceptance. This is not authorization to start M4+.

Proposed product architecture combines neutral C# domain/application logic and shared Avalonia views with narrow native services. Mac Player follows the accepted controlled FFmpeg → Metal → IOSurface → Avalonia adapter → shared composition path, with CoreAudio, token/generation/Color identity, UIAccepted and independent release fencing. Windows supplies its own presentation/backend adapter behind the same semantic contract. Physical scanout is not a blanket semantic condition.

Catalog remains local SQLite with accepted P2 portability. Preview remains machine-local. Platform filesystem/storage/lifecycle adapters handle native differences. Actions, shortcut semantics, selection and Color intent remain shared. Common Skia decoding plus platform format-gap decoders return the same owned-pixel/metadata contract. SDR presentation must explicitly identify its source color space to the OS.

No new architecture blocker was found. Delivery risks are custom Details cost, free-control/AX polish, decoder parity corpus, color/package acceptance, and platform launch/lifecycle reliability. Shared feature implementation plus adapter contract tests and packaged Windows/Mac acceptance is the proposed future workflow. This draft changes no production project and starts no implementation milestone.
