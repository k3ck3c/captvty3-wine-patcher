# Optimizing the Captvty 3 Docker Image Under Wine

**Status: October 9, 2026 — `no-netcache` image validated**
**Project:** `captvty3-wine-patcher`
**Platform:** Debian 13, Wine Staging 11.16, new WoW64, .NET Framework 4.8

## 1. Goals and guiding principles

Run Captvty 3 (a 32-bit Windows .NET application) inside a Linux Docker container while reducing image size **without compromising** the user interface, downloads, or the resulting media files.

Principles followed:

- Keep a known-good image at every stage.
- Change only one group of dependencies at a time.
- Measure actual image sizes using `docker image inspect`, rather than relying solely on `du` output.
- Test Captvty and a download after every significant change.
- Do not remove .NET DLLs or multimedia libraries merely because they are large.
- Avoid deleting files in a Docker layer *after* copying them: doing so hides the files but does not reclaim the space occupied by earlier layers.

## 2. Image architecture

The final image is based on `debian:13-slim`. An existing image, `captvty3-wine-patcher:dedup-python`, acts as the **source** for selectively copying Wine and the prefix already configured with .NET Framework 4.8.

```dockerfile
# syntax=docker/dockerfile:1
ARG USER_NAME=captvty
ARG USER_UID=1000
ARG USER_GID=1000

FROM captvty3-wine-patcher:dedup-python AS source
FROM debian:13-slim AS final
```

The WoW64 variant copies only the Wine directories required by this runtime configuration:

```dockerfile
COPY --from=source /opt/wine-staging/bin /opt/wine-staging/bin
COPY --from=source /opt/wine-staging/lib/wine/x86_64-unix /opt/wine-staging/lib/wine/x86_64-unix
COPY --from=source /opt/wine-staging/lib/wine/x86_64-windows /opt/wine-staging/lib/wine/x86_64-windows
COPY --from=source /opt/wine-staging/lib/wine/i386-windows /opt/wine-staging/lib/wine/i386-windows
COPY --from=source /opt/wine-staging/share /opt/wine-staging/share
```

**Key point:** `i386-windows` is retained, but `i386-unix` and the stack of 32-bit Linux libraries are not copied. This choice was tested with the new WoW64 implementation in Wine Staging 11.16; it should not be generalized to other Wine versions without testing.

The `.wine` prefix contains the .NET 4.8 installation. Duplicate Wine DLLs inside the prefix had already been replaced by symbolic links to the Wine installation during the `dedup-python` stage (1,699 files deduplicated). Therefore, the prefix must not be relocated independently of the paths targeted by those links.

## 3. Image size history

The figures below come from measurements taken during testing. The initial size and some intermediate sizes were rounded.

| Variant | Image size | Main change |
|---|---:|---|
| Initial | ~6.06 GB | Full image before optimization |
| `dedup-python` | 3,640,628,132 bytes | Wine prefix deduplication |
| `wow64` | 3,440,470,791 bytes | Selective Wine copy, without the Linux i386 stack |
| `no-llvm` | 3,283,046,047 bytes | Removal of LLVM and Z3 libraries unused in this configuration |
| `no-gallium` | 3,240,480,143 bytes | Removal of `libgallium-*.so` |
| `no-samba` | 3,164,657,957 bytes | Removal of `winbind` and transitive dependencies |
| **`no-netcache`** | **2,918,282,715 bytes** | Exclusion of the .NET 4.8 installation cache |

**Measured reduction from `no-samba` to `no-netcache`: 246,375,242 bytes, approximately 235 MiB.**
**Overall reduction: approximately 52% compared with the initial ~6.06 GB image.**

To measure an image:

```bash
docker image inspect \
  captvty3-wine-patcher:no-samba \
  captvty3-wine-patcher:no-netcache \
  --format '{{.RepoTags}} : {{.Size}} bytes'
```

## 4. Optimization steps in detail

### 4.1 Prefix deduplication (`dedup-python`)

The deduplication stage replaced 1,699 copies of Wine DLLs in the prefix with symbolic links pointing to the corresponding Wine files. This avoids keeping multiple copies of the same binaries.

**To be documented from the repository sources:** the exact Python script, file equality criteria, symlink checks, and initial build command. These details have not been reconstructed without the original files.

### 4.2 Selective WoW64 copy (`wow64`)

The final build starts again from `debian:13-slim` and copies the required Wine components from `dedup-python`. Omitting `i386-unix` reduces image size while retaining `i386-windows` for 32-bit Windows applications.

**Validation:** Captvty starts; downloading and media playback were checked.

### 4.3 Removing LLVM and Z3 (`no-llvm`)

The `libLLVM.so.19.1` (~129.7 MB) and `libz3.so.4` (~27.75 MB) libraries were removed in the **same `RUN` instruction as the APT installation**, so their contents would not remain in an earlier image layer.

Tests that forced `LIBGL_ALWAYS_SOFTWARE=1` and `GALLIUM_DRIVER=softpipe` made the interface sluggish. These environment variables were **not** retained as a solution.

**Validation:** responsive interface, successful download and playback with `mpv`.

### 4.4 Removing Gallium (`no-gallium`)

The `/usr/lib/x86_64-linux-gnu/libgallium-*.so` files were removed in the APT installation layer. Measured reduction compared with `no-llvm`: **42,565,904 bytes**.

**Validation:** responsive Captvty interface, download, and video/audio playback checked at the beginning, middle, and end.

### 4.5 Removing `winbind` (`no-samba`)

Instead of running a broad and potentially risky `apt autoremove`, the explicit `winbind` dependency was removed from the Dockerfile's `apt-get install` list. Rebuilding also avoided installing several transitive Samba dependencies, including the ICU library previously present.

Measured reduction compared with `no-gallium`: **75,822,186 bytes**.

**Validation:** download and playback of a 53 min 18 s program (~390 MiB, MPEG-TS, H.264, AAC), with video and audio checked throughout.

### 4.6 Excluding the .NET installation cache (`no-netcache`)

Analysis of the prefix revealed:

```text
~1007 MiB  /home/captvty/.wine
 ~998 MiB  /home/captvty/.wine/drive_c/windows
 ~685 MiB  .../windows/Microsoft.NET
 ~400 MiB  .../Microsoft.NET/Framework64
 ~236 MiB  .../Framework64/v4.0.30319/SetupCache
 ~218 MiB  .../SetupCache/v4.8.03761/NetFx_Full.mzz
```

`NetFx_Full.mzz` belongs to the .NET Framework 4.8 installation cache. It can be useful for maintenance or repair but is not normally required to run Captvty.

The `docker/Dockerfile.no-netcache` file was derived from `docker/Dockerfile.no-samba` by changing the prefix copy operation:

```dockerfile
# .NET 4.8 prefix, already installed and deduplicated.
COPY --from=source --chown=${USER_UID}:${USER_GID} \
     --exclude=drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/SetupCache/** \
     /home/${USER_NAME}/.wine \
     /home/${USER_NAME}/.wine
```

The pattern excludes the **files inside** `SetupCache`. The directory itself remains, empty (4 KiB). This is expected and harmless:

```bash
docker run --rm --entrypoint /bin/sh \
  captvty3-wine-patcher:no-netcache \
  -c 'du -sh /home/captvty/.wine/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/SetupCache; \
      ls -la /home/captvty/.wine/drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/SetupCache'
```

Observed result: empty directory, **4.0K**.

> **Verification caveat:** `test ! -e .../SetupCache` fails because the directory still exists. Check its **contents**, not whether the directory itself exists.

## 5. Building and running

Dockerfiles are located in `docker/`. The **build context must be `docker/`**, because the Dockerfile contains:

```dockerfile
COPY --chmod=755 entrypoint.sh /usr/local/bin/captvty3-entrypoint
```

From the project root:

```bash
docker build \
  -f docker/Dockerfile.no-netcache \
  -t captvty3-wine-patcher:no-netcache \
  docker/
```

**Error encountered:** using `.` as the build context caused `"/entrypoint.sh": not found`. Switching to `docker/` fixed the issue.

Create a Compose file for the new variant without modifying the known-good configuration:

```bash
sed 's/captvty3-wine-patcher:no-samba/captvty3-wine-patcher:no-netcache/' \
  compose.no-samba.yaml > compose.no-netcache.yaml
```

Run:

```bash
docker compose -f compose.no-netcache.yaml \
  -p captvty3-netcache-test run --rm --no-deps captvty3
```

**Note:** the Compose files define directory mounts, including downloads. Preserve these settings during tests so that an image regression is not confused with a configuration difference.

## 6. Regression testing procedure

For each new variant:

1. **Build:** Docker build completes successfully.
2. **Measurement:** obtain the exact image size using `docker image inspect`.
3. **Component checks:** verify the retained libraries and the files targeted for removal.
4. **User interface:** launch Captvty, browse channels, check program listings, thumbnails, descriptions, and responsiveness.
5. **Download:** download a complete program.
6. **Playback:** open the file using `mpv` on the Debian host; check **video and audio at the beginning, middle, and end**.
7. **Media inspection:** use `mediainfo` to check duration, codecs, resolution, and audio tracks.

Latest `no-netcache` test, **October 9, 2026**: downloaded the France 2 segment *Journal 20h00 – Une PME dans le viseur de Trump*, an MPEG-TS file of **16.5 MiB**, lasting **3 min 33 s**:

- H.264/AVC video, 768 × 432, 25 fps, progressive.
- AAC LC audio, two channels, 48 kHz, French.
- `mpv` playback: video and audio checked throughout, including beginning, middle, and end.

**Conclusion: `captvty3-wine-patcher:no-netcache` is the new validated, known-good image.**

## 7. What was retained, and why

### FFmpeg libraries

`/opt/wine-staging/lib/wine/x86_64-unix/winedmo.so` depends on FFmpeg libraries, including `libavutil.so.59`, `libavformat.so.61`, and `libavcodec.so.61`, as well as indirect dependencies such as `libcodec2.so.1.2` and `libx265.so.215`.

Even though final playback uses `mpv` on the Debian host, removing these libraries might break Wine multimedia functionality. They were therefore **retained**.

### .NET components

The prefix contains, among other things:

| Path under `drive_c/windows` | Observed size |
|---|---:|
| `Microsoft.NET/Framework64` | ~400 MiB before excluding the cache |
| `Microsoft.NET/Framework` | ~153 MiB |
| `Microsoft.NET/assembly` | ~132 MiB |
| `assembly/NativeImages_v4.0.30319_32` | ~177 MiB |
| `assembly/NativeImages_v4.0.30319_64` | ~35 MiB |
| `winsxs` | ~87 MiB |

These directories contain runtime components, assemblies, or native images. Their size alone does not establish that they are unnecessary.

The `assembly/NativeImages_v4.0.30319_64/Temp` directory occupies only **8 KiB** and is not a meaningful optimization target.

## 8. Possible next steps

- Examine **NGen native images**: identify items that may be regenerated, then test removal in a **new** image, never in `no-netcache`.
- Examine caches and 64-bit .NET components without assuming that the entire `Framework64` directory is unnecessary under the new WoW64.
- Measure other large directories before removing anything.
- Document the `dedup-python` script precisely using its source code.
- Automate startup, download, and media inspection tests when appropriate.

**Safety rule:** do not remove `Microsoft.NET/assembly`, `Framework`, `Framework64`, or NGen images wholesale without targeted testing. The easiest savings (clearly unnecessary libraries and the installation cache) have already been achieved.

## 9. Useful commands

```bash
# Compare the last two image variants
docker image inspect \
  captvty3-wine-patcher:no-samba \
  captvty3-wine-patcher:no-netcache \
  --format '{{.RepoTags}} : {{.Size}} bytes'

# Find large directories inside the prefix
docker run --rm --entrypoint /bin/sh \
  captvty3-wine-patcher:no-netcache \
  -c 'du -h -d 2 /home/captvty/.wine/drive_c/windows 2>/dev/null | sort -h | tail -25'

# Find the largest files within a subtree
docker run --rm --entrypoint /bin/sh \
  captvty3-wine-patcher:no-netcache \
  -c 'du -ah /home/captvty/.wine/drive_c/windows/Microsoft.NET 2>/dev/null | sort -h | tail -25'

# Inspect the codecs and duration of a downloaded file
mediainfo 'downloads/example-file.ts'

# Play a downloaded file
mpv 'downloads/example-file.ts'
```

---

*This document describes the steps and results actually observed. To make the entire process reproducible from scratch, complete the instructions for creating the initial image and `dedup-python` using the scripts in the repository.*
