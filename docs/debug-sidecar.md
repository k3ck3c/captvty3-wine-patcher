# Debug sidecar container

The production Captvty3 image deliberately excludes debugging utilities. A temporary sidecar shares the application's process and network namespaces and volumes, providing diagnostics without enlarging the production image.

## Build

`docker/Dockerfile.debug` is based on `captvty3-wine-patcher:no-ngen` and installs `gdb`, `strace`, `procps`, `lsof`, `file`, `binutils`, and `iproute2`.

```bash
docker build -f docker/Dockerfile.debug -t captvty3-wine-patcher:debug docker/
```

During the initial experiments, additional packages were installed interactively:

```bash
apt-get update
apt-get install -y --no-install-recommends binutils
apt-get install -y --no-install-recommends iproute2
```

`binutils` provides `objdump`, `nm`, and `addr2line`; `iproute2` provides `ss`. They are now included in the debug Dockerfile. Packages installed interactively disappear when the temporary container is removed.

## Start

With the application container already running:

```bash
docker run --rm -it \
  --name captvty3-debug \
  --pid=container:captvty3-dedup-all \
  --network=container:captvty3-dedup-all \
  --volumes-from captvty3-dedup-all \
  --cap-add=SYS_PTRACE \
  --security-opt seccomp=unconfined \
  --entrypoint /bin/bash \
  captvty3-wine-patcher:debug
```

Replace `captvty3-dedup-all` with the actual application container name. The tracing permissions are intended for local diagnostics in a trusted environment. Process IDs below are examples from our test session, not fixed values.

## Investigations performed

### Processes and GDB

We identified `Captvty.exe` (PID 67 during testing), `wineserver`, and Captvty's threads. `/proc/67/fd`, `/proc/67/maps`, and `/proc/67/root` exposed file descriptors, memory mappings, and the application's filesystem.

```bash
ps aux
ls -l /proc/67/fd
cat /proc/67/maps

gdb -q -nx -batch \
  -iex 'set sysroot /proc/67/root' \
  -p 67 \
  -ex 'info threads' \
  -ex 'info sharedlibrary' \
  -ex 'detach'
```

GDB attached successfully. Using `objdump`, `nm`, and `addr2line`, we examined addresses in `libc.so.6`, including code immediately following a Linux system call. Native GDB has limitations interpreting Windows PE modules and managed .NET stacks under Wine's new WoW64 architecture; a thread at a syscall boundary alone does not establish a hang.

### System calls and X11

```bash
strace -f -p 67 \
  -e trace=futex,ppoll,poll,epoll_wait,epoll_pwait,clock_nanosleep \
  -e signal=none -tt -T \
  -o /tmp/captvty-waits.log
```

Rapid `poll()` activity on file descriptors 21 and 35 was traced to connected Unix-domain sockets associated with X11/Xwayland, not remote video transfers. `lsof` and `ss -xapne` helped identify the socket endpoints.

### HTTPS connections

```bash
ss -tnp | grep 'Captvty.exe'
ss -tnpi | grep -A2 'pid=67'
```

We observed multiple established IPv6 TCP/443 connections to Akamai addresses. Some sockets were visible through both `Captvty.exe` and `wineserver`. `ss -tnpie` exposes socket inodes and cumulative `bytes_received` counters.

### Throughput measurement

The repository's `tools/captvty-debit.sh` samples `ss -tnpie` and compares `bytes_received` for each socket inode. It prints an approximate incoming rate and the number of observed Captvty connections.

The script requires Bash, awk, and `ss`, and must be copied or mounted into the sidecar before use:

```bash
bash /path/to/captvty-debit.sh
```

Newly observed sockets have no previous sample, so their initial byte counts are deliberately not included. Sampling intervals are approximate.

During a quality-selection request for *Super Détectives S02E12*, without explicitly starting a download, we observed:

| Time | Approximate received rate |
| --- | ---: |
| 15:10:34 | 0.43 MiB/s |
| 15:10:35 | 2.21 MiB/s |
| 15:10:36 | 6.35 MiB/s |

The sum of the three one-second samples was approximately **8.99 MiB**, with a peak of **6.35 MiB/s (~53.3 Mbit/s)**. The number of established connections increased from six to twelve shortly beforehand. These counters do not identify the HTTPS resources involved, so the traffic's exact purpose remains undetermined.

We also examined connections while selecting *Misery* and *Renaud, 50 ans de chansons*.

## Outcome

The debug sidecar successfully provided process and thread inspection, GDB attachment, syscall tracing, Unix-domain socket identification, and live TCP statistics without adding these tools to the production Captvty3 image. It can be removed after troubleshooting without modifying the application container.
