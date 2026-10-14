#!/usr/bin/env bash
# Publish a linux-x64 folder and optionally wrap it as an AppImage.
# Usage: ./scripts/publish-appimage.sh [--aot]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${ROOT}/artifacts/linux-x64"
APPDIR="${ROOT}/artifacts/appimage/DlsiteClient.AppDir"
TFM="net10.0"
RID="linux-x64"
AOT=false
if [[ "${1:-}" == "--aot" ]]; then AOT=true; fi

echo "==> Publish ${RID} (${TFM}, AOT=${AOT})"
rm -rf "${OUT}" "${APPDIR}"
mkdir -p "${OUT}"

PUB_ARGS=(
  -c Release
  -f "${TFM}"
  -r "${RID}"
  --self-contained true
  -o "${OUT}"
  -p:PublishSingleFile=false
)
if [[ "${AOT}" == "true" ]]; then
  PUB_ARGS+=(-p:PublishAot=true)
else
  PUB_ARGS+=(-p:PublishAot=false -p:PublishTrimmed=true)
fi

dotnet publish "${ROOT}/src/DlsiteClient.App/DlsiteClient.App.fsproj" "${PUB_ARGS[@]}"

echo "==> Stage AppDir"
mkdir -p "${APPDIR}/usr/bin" "${APPDIR}/usr/share/applications" "${APPDIR}/usr/share/icons/hicolor/256x256/apps"
cp -a "${OUT}/." "${APPDIR}/usr/bin/"
if [[ ! -x "${APPDIR}/usr/bin/DlsiteClient.App" ]]; then
  EXE="$(find "${APPDIR}/usr/bin" -maxdepth 1 -type f -executable | head -n1 || true)"
  if [[ -n "${EXE}" ]]; then
    ln -sf "$(basename "${EXE}")" "${APPDIR}/usr/bin/DlsiteClient.App"
  fi
fi
cp "${ROOT}/packaging/linux/AppRun" "${APPDIR}/AppRun"
chmod +x "${APPDIR}/AppRun"
cp "${ROOT}/packaging/linux/dlsite-client.desktop" "${APPDIR}/dlsite-client.desktop"
cp "${ROOT}/packaging/linux/dlsite-client.desktop" "${APPDIR}/usr/share/applications/"
# 1x1 PNG placeholder icon
echo 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO5W+9kAAAAASUVORK5CYII=' \
  | base64 -d > "${APPDIR}/dlsite-client.png"
cp "${APPDIR}/dlsite-client.png" "${APPDIR}/usr/share/icons/hicolor/256x256/apps/dlsite-client.png"

echo "==> AppDir ready: ${APPDIR}"
echo "    To wrap with appimagetool (if installed):"
echo "    appimagetool \"${APPDIR}\" \"${ROOT}/artifacts/appimage/DlsiteClient-linux-x64.AppImage\""

if command -v appimagetool >/dev/null 2>&1; then
  mkdir -p "${ROOT}/artifacts/appimage"
  appimagetool "${APPDIR}" "${ROOT}/artifacts/appimage/DlsiteClient-linux-x64.AppImage"
  echo "==> AppImage: ${ROOT}/artifacts/appimage/DlsiteClient-linux-x64.AppImage"
else
  echo "==> appimagetool not found; left AppDir for manual packaging."
fi
