#!/usr/bin/env bash
# Run on the prepared SteamOS build VM as root, outside the Ubuntu chroot.
set -euo pipefail
BASE=/mnt/dlsite-test
ROOT="$BASE/ubuntu"
mkdir -p "$BASE"
mountpoint -q "$BASE" || mount /dev/nvme0n1 "$BASE"
exec 9>"$BASE/actions-runner.lock"
flock -n 9 || exit 0
for path in dev proc sys; do
  mountpoint -q "$ROOT/$path" || mount --rbind "/$path" "$ROOT/$path"
  mount --make-rslave "$ROOT/$path"
done
cp --remove-destination /etc/resolv.conf "$ROOT/etc/resolv.conf"
exec chroot "$ROOT" /bin/bash -c 'cd /opt/actions-runner; export RUNNER_ALLOW_RUNASROOT=1 DOTNET_ROOT=/opt/dotnet; export PATH=/opt/dotnet:$PATH; exec ./run.sh'
