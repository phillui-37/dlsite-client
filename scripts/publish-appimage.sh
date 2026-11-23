#!/usr/bin/env bash
# Publish a linux-x64 folder and optionally wrap it as an AppImage.
# Build on Ubuntu 24.04 amd64 with WebKitGTK 4.1 installed.
# Usage: ./scripts/publish-appimage.sh [--aot] [--skip-publish]
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${ROOT}/artifacts/linux-x64"
APPDIR="${ROOT}/artifacts/appimage/DlsiteClient.AppDir"
TFM="net11.0"
RID="linux-x64"
AOT=false
SKIP_PUBLISH=false
for arg in "$@"; do
  case "$arg" in --aot) AOT=true;; --skip-publish) SKIP_PUBLISH=true;; *) echo "Unknown argument: $arg" >&2; exit 2;; esac
done

echo "==> Publish ${RID} (${TFM}, AOT=${AOT})"
rm -rf "${APPDIR}"
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

if [[ "$SKIP_PUBLISH" != true ]]; then
  (cd "$ROOT" && dotnet publish "${ROOT}/src/DlsiteClient.App/DlsiteClient.App.fsproj" "${PUB_ARGS[@]}")
fi
test -f "${OUT}/DlsiteClient.App"

echo "==> Stage AppDir"
mkdir -p "${APPDIR}/usr/bin" "${APPDIR}/usr/share/applications" "${APPDIR}/usr/share/icons/hicolor/256x256/apps"
cp -a "${OUT}/." "${APPDIR}/usr/bin/"
chmod +x "${APPDIR}/usr/bin/DlsiteClient.App"
python3 "${ROOT}/scripts/bundle-webkit.py" "${APPDIR}"
cp "${ROOT}/packaging/linux/dlsite-secret-tool" "${APPDIR}/usr/bin/dlsite-secret-tool"
chmod +x "${APPDIR}/usr/bin/dlsite-secret-tool"
# Bundle the official static Linux decoder; SteamOS needs no package-manager changes.
SEVEN_VERSION="26.06"
SEVEN_DIR="${ROOT}/artifacts/7zip-linux/${SEVEN_VERSION}"
mkdir -p "${SEVEN_DIR}"
SEVEN_ARCHIVE="${SEVEN_DIR}/7z${SEVEN_VERSION/./}-linux-x64.tar.xz"
if [[ ! -f "${SEVEN_ARCHIVE}" ]]; then
  curl --fail --location --retry 2 "https://github.com/ip7z/7zip/releases/download/${SEVEN_VERSION}/${SEVEN_ARCHIVE##*/}" -o "${SEVEN_ARCHIVE}"
fi
echo "2b539cf067b2ad48e1baa5093dbaa8818b73542d1222a0ffde9dc5f2922ea62e  ${SEVEN_ARCHIVE}" | sha256sum --check --status
tar -xf "${SEVEN_ARCHIVE}" -C "${SEVEN_DIR}"
cp "${SEVEN_DIR}/7zzs" "${APPDIR}/usr/bin/7zz"
chmod +x "${APPDIR}/usr/bin/7zz"
mkdir -p "${APPDIR}/usr/share/licenses/7zip"
cp "${SEVEN_DIR}/License.txt" "${APPDIR}/usr/share/licenses/7zip/License.txt"
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

if ! command -v appimagetool >/dev/null 2>&1; then
  APPIMAGETOOL_VERSION="1.9.1"
  TOOL_DIR="${ROOT}/artifacts/build-tools/appimagetool/${APPIMAGETOOL_VERSION}"
  mkdir -p "$TOOL_DIR"
  TOOL="$TOOL_DIR/appimagetool.AppImage"
  if [[ ! -f "$TOOL" ]]; then
    curl --fail --location --retry 2 "https://github.com/AppImage/appimagetool/releases/download/${APPIMAGETOOL_VERSION}/appimagetool-x86_64.AppImage" -o "$TOOL"
  fi
  echo "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0  $TOOL" | sha256sum --check --status
  chmod +x "$TOOL"
  (cd "$TOOL_DIR" && "$TOOL" --appimage-extract >/dev/null)
  APPIMAGE_TOOL="$TOOL_DIR/squashfs-root/AppRun"
else
  APPIMAGE_TOOL="$(command -v appimagetool)"
fi
if [[ -x "$APPIMAGE_TOOL" ]]; then
  mkdir -p "${ROOT}/artifacts/appimage"
  TEMP_IMAGE="${ROOT}/artifacts/appimage/DlsiteClient-linux-x64.AppImage.new"
  trap 'rm -f "$TEMP_IMAGE"' EXIT
  ARCH=x86_64 "$APPIMAGE_TOOL" "${APPDIR}" "$TEMP_IMAGE"
  mv -f "$TEMP_IMAGE" "${ROOT}/artifacts/appimage/DlsiteClient-linux-x64.AppImage"
  echo "==> AppImage: ${ROOT}/artifacts/appimage/DlsiteClient-linux-x64.AppImage"
else
  echo "appimagetool is required; AppDir is available for diagnosis." >&2
  exit 1
fi
