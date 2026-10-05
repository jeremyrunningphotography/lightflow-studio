# Image pipeline proof — partial Windows checkpoint

SkiaSharp 3.119.4 (MIT package) is supplied by Avalonia.Skia 12.1.3. Native assets and
transitive hashes are in the dependency manifest. Package-level MIT metadata is not a
complete native third-party redistribution audit; production adoption requires notices
and build provenance for Skia/HarfBuzz/ANGLE and any format adapter.

## Existing WIC responsibilities

At baseline `e302a20d…`: `DerivedMediaMetadata.cs` reads dimensions, pixel/bit-depth
and EXIF/camera metadata through WIC; `ThumbnailGeneration.cs` decodes the first frame,
applies source orientation, resizes and encodes Preview; `PlayerViewerHost.xaml.cs`
uses the same orientation helper for full-resolution still viewing.
`CachedPreviewImageConverter.cs` loads detached pixels with no retained file stream;
`DetachedBitmap.cs` separates producer lifetime; `OrientedPreviewImage.cs` adds authored
Catalog video rotation after source orientation. Posters/Visual Index/marker captures
and Inspector also depend on UI bitmap ownership. None were changed.

WIC-vs-Skia source/display behavior has not been compared on a real image corpus.
Do not infer that current WIC ICC behavior is identical to the shared vector.

## Executed fixtures and vectors

See raw results, deterministic generator source and SHA-256 fixture manifest.

| Case | Windows result | Limit |
|---|---|---|
| PNG/JPEG/WebP | Decoded 3×2 encoded vectors | Tiny generated corpus; not camera-image parity |
| GIF | Decoded 1×1 fixture | Multiframe policy/animated frame handling not qualified |
| BMP | Decoded handcrafted 2×1 24-bit BMP | Other BMP variants not tested |
| TIFF | Handcrafted uncompressed RGB fixture rejected | Requires a supported decoder/adapter and independently validated broader TIFF corpus; no TIFF parity claim |
| EXIF 1–8 | JPEG tag read agrees with exact value; independent literal pixel-coordinate vectors pass | Discrete transform tested on lossless source pixels separately from JPEG's lossy colors |
| Orientation 8 | Output 2×3, vector `[2,5,1,4,0,3]` | Mac decode/display still pending |
| ICC/alpha | Linear-light RGBA128 → sRGB188, alpha128; PNG has `iCCP`; codec and canvas paths agree | One RGB profile; no wide-gamut/CMYK/malformed-profile/16-bit/HDR/display-profile coverage |
| Malformed bytes | Codec returns null | Full resource/security/error corpus not exercised |
| Source lifetime | File deleted while detached decoded pixels remain readable | Windows fixture only; UI Preview cache replacement still needs native qualification |
| UI thumbnail lifetime | Attach/decode/detach/dispose counts balanced after close | No long-duration/native memory qualification |
| WDP/JXR | Not tested | Required native/other decoder capability remains open |
| RAW | No decoder introduced | Preserve current placeholder promise |

An initial color probe used `SetPixel(SKColor)`, which performs color-aware conversion
itself; expecting its value to be literal linear bytes was a fixture error. Corrected
fixture writes raw RGBA buffer bytes. The preserved result is the corrected passing
vector, with an explicit expected numerical result and actual encoded ICC chunk.

## Proposed pixel boundary and unresolved copy cost

Proposal only: retain source dimensions/orientation/profile metadata; decode into an
explicit pixel format/alpha/color space; apply source orientation once; produce bounded
Preview pixels in an agreed color space; let a platform/UI adapter own the presentation
bitmap and disposal. Authored rotation remains separate and follows Catalog authority.

The proof's UI uses Avalonia `Bitmap(path)` directly and releases it on detach. The
Skia vector pipeline is separate. **No zero-copy Skia→Avalonia bridge was proved**;
decode/resize throughput, shared bitmap lease/refcount/cache replacement, stale-result
generation checks and memory-budgeted asynchronous thumbnail work are still required.
Rendering 320×200 sources into smaller UI rectangles demonstrates display scaling,
not optimized thumbnail decode/resample throughput.

Common formats can use shared Skia; Mac ImageIO/Windows WIC are candidate gap/metadata
adapters for TIFF, WDP/JXR and source EXIF/ICC responsibilities. No ImageIO adapter
or supported-format exception was selected. Verify actual format support on arm64;
do not claim ImageIO supports JPEG XR from platform documentation alone.

For later color parity: compare identical tagged sRGB/linear/wide-gamut/CMYK fixtures
through Windows WIC, shared Skia and Mac ImageIO; retain hashes, metadata and decoded
pixels; separately measure monitor-profile native presentation. Agree tolerances for
lossless/vector pixels versus lossy JPEG versus display capture. Player Color belongs
to M1; this proof does not approve it or expand Lightflow's RAW promise.

G3 image disposition: promising common-format/orientation/profile vector mechanism,
but **incomplete** due to native evidence, format gaps, metadata/corpus, ownership/copy
and actual Preview integration requirements.
