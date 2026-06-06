# 21 - Extension System

## Status

The managed extension architecture is implemented. Liveolator can validate, install, enable,
disable, enumerate, and uninstall declarative `.liveolator-pack` files. UI themes and visual-effect
descriptors load from enabled packages. Audio effect racks are wired into Deck A, Deck B, and the
post-mix master DSP path.

Real VST3 processing and isolated GLSL compilation require distribution-time native helpers that are
not stored in this repository:

- `liveolator-vst3-scanner[.exe]`: Steinberg-SDK scanner that writes the JSON protocol consumed by
  `Vst3ScannerClient`.
- An `IVst3NativeBridge` implementation: owns VST3 module/processor handles and returns
  `IAudioEffectProcessor` instances.
- `liveolator-shader-probe[.exe]`: creates a disposable GL context and writes
  `VisualShaderProbeResult`.

When these are absent, startup remains safe: VST effects become missing pass-through placeholders,
the cached VST catalog remains readable, and extension shaders are not activated.

## Package Format

A `.liveolator-pack` is a ZIP archive containing `manifest.json`, optional `signature.json`, and
declared content such as `visual-effects.json`, `themes/*.json`, scenes, shaders, mappings, and media.

`manifest.json` declares package id, semantic version, required API major, publisher, content kinds,
dependencies, and every payload file's exact byte length and SHA-256. `signature.json` contains a
publisher key id and an ECDSA P-256/SHA-256 signature over the exact manifest bytes.

Trusted public keys are read from `<app-data>/Liveolator/trusted-publishers.json` as a JSON object
mapping key id to PEM SubjectPublicKeyInfo. A package cannot modify this file. Unsigned packages are
accepted only when Developer Mode was persisted before startup.

Validation rejects traversal paths, duplicate case-insensitive paths, symlinks, undeclared files,
hash/size mismatches, incompatible API majors, invalid dependencies, excessive entry/file/package
sizes, and shaders over 512 KiB. Installation copies the package into a private incoming file,
revalidates that immutable copy, extracts to staging, then atomically renames it to:

```text
<app-data>/Liveolator/extensions/<packageId>/<version>/
```

## Content Contracts

- `visual-effects.json` is an array of `VisualEffectDescriptor`. Effect ids are package-qualified;
  shader paths stay inside the package; parameter uniforms must exist in the isolated probe result.
- `EffectRef` persists effect id, version, and a stable instance id.
- `MacroTarget` addresses a layer, optional effect instance id, and parameter.
- UI themes contain only approved color, numeric layout, and font tokens. XAML, templates, bindings,
  and managed assemblies are never loaded. Missing tokens inherit Spartan resources.
- Theme selection is persisted and applied at the next application start. Visual registries reload
  immediately after package install, enable/disable, or uninstall.

## Audio Effects

The dispatcher owns load, unload, move, bypass, set-parameter, and load-preset actions.
`PerformanceAction.Target` carries the stable effect instance id.

Each rack uses copy-on-write snapshots. Control changes happen outside the audio callback; processing
reads one immutable entry array without locks or allocations. Missing plugins retain UID, parameters,
opaque state, ordering, and bypass state as pass-through placeholders.

```text
deck source -> gain/EQ/filter -> deck rack -> BASSmix/crossfader
            -> master rack -> output + master beat-analysis tap
```

The rack reports summed active plugin latency. Applying that latency to the shared beat timeline is
deferred until the native bridge supplies real processors and latency-change notifications.

Show packs never contain VST binaries or arbitrary .NET assemblies. VST3 modules remain installed in
platform-standard locations and are identified by VST3 UID.
