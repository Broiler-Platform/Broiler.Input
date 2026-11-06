# ADR 0009 - Scanner Acquisition Boundary And Camera Reuse

**Status:** Approved for Phase S0  
**Date:** 2026-11-06

## Context

`Broiler.Input` needs platform-neutral support for document and image scanners
(flatbed, sheetfed, and simplex/duplex Automatic Document Feeders) across
Windows (WIA 2.0), Linux (SANE), and Android. Because `Broiler.Input.Camera`
already captures 2D raster frames into disposable leases over background capture
threads, we must define what `Broiler.Input.Scanner` shares with camera capture
and where their contracts and boundaries diverge.

## Decision

1. **Separate typed abstraction (`Broiler.Input.Scanner`):**
   `ScannerInputDevice` extends `InputDevice` directly and requires
   `InputKind.Scanner`. Consistent with [ADR 0001](0001-assembly-names-repository-topology.md)
   and [ADR 0002](0002-abstract-device-hierarchy.md), `Broiler.Input.Scanner`
   does not reference `Broiler.Input.Camera`, and no intermediate 2D imaging
   base device class is introduced.
2. **Job/page acquisition vs. continuous video stream:**
   Camera capture is a continuous frame-rate stream where dropping older frames
   (`KeepLatest`) is the default preview behavior. Scanner acquisition is a
   finite, job-driven session (a single flatbed scan or a multi-page ADF batch)
   configured by spatial resolution (`DpiX`, `DpiY`), physical crop geometry
   (`ScannerRegion` in microns), and paper source (`Flatbed`, `FeederSimplex`,
   `FeederDuplex`, `Auto`), delivering owned disposable `ScannerPageLease`
   instances with bounded loss-sensitive delivery by default and a
   `ScannerPreviewAdapter` for low-DPI prescan workflows.
3. **Shared Windows capture and COM infrastructure (`Broiler.Input.Windows`):**
   `Broiler.Input.Scanner.Windows` reuses `WindowsCaptureWorker<T>`,
   `WindowsInputClock`, `WindowsComInterop`, and `WindowsComApartmentScope`
   from `Broiler.Input.Windows` rather than duplicating thread management or COM
   pointer lifetime logic from `Broiler.Input.Camera.Windows` or
   `Broiler.Input.Microphone.Windows`.
4. **Codec, OCR, and document boundary ([ADR 0008](0008-media-interop-boundary.md)):**
   `Broiler.Input.Scanner` and its platform providers own live scanner
   enumeration, capability queries, session lifecycle, page/band buffer leases,
   and physical fault reporting (paper jam, feeder empty, cover open, device
   busy/removed). PDF assembly, image file encoding, OCR, deskewing, and
   camera-frame barcode decoding remain in `Broiler.Media`,
   `Broiler.Documents`, or application layers.

## Consequences

- `WindowsComInterop` and `WindowsComApartmentScope` live in
  `Broiler.Input.Windows` and are shared across Camera, Microphone, and Scanner
  Windows providers.
- `Broiler.Input.Scanner` can evolve page- and feeder-specific metadata (duplex
  sheet side, end-of-batch flags, physical micron geometry, 1-bit monochrome and
  16-bit grayscale formats) without coupling to video frame rates.
- Downstream consumers (`Broiler.Plate`, `Broiler.Documents`) can consume
  scanned pages without pulling WIA or SANE dependencies into neutral layers.
