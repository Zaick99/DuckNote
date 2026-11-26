#!/usr/bin/env bash
# DuckNote per macOS: publish, bundle DuckNote.app, firma ad hoc, immagine disco.
#
#   packaging/macos/build-app.sh <osx-arm64|osx-x64> <versione> <cartella di uscita>
#
# Sul Mac (codesign e hdiutil) produce DuckNote-<versione>-macos-<arch>.dmg con l'app firmata
# ad hoc (nessun certificato Apple: la prima apertura passa da Impostazioni di Sistema >
# Privacy e sicurezza > Apri comunque). Altrove produce lo stesso bundle in uno .zip: l'eseguibile
# e' gia' firmato ad hoc da dotnet, il bundle no.
set -euo pipefail

rid="${1:?indica osx-arm64 oppure osx-x64}"
version="${2:?indica la versione}"
out="${3:?indica la cartella di uscita}"

case "$rid" in
  osx-arm64|osx-x64) ;;
  *) echo "runtime non previsto: $rid" >&2; exit 1 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
arch="${rid#osx-}"
name="DuckNote-$version-macos-$arch"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# un solo eseguibile con tutto il codice .NET dentro (l'impronta SHA-256 nelle Impostazioni
# lo copre per intero); le librerie native di Avalonia e Skia restano accanto, come vuole macOS
dotnet publish "$root/src/DuckNote.Desktop" -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:DebugType=none -p:Version="$version" -o "$work/publish"

app="$work/DuckNote.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$work/publish/." "$app/Contents/MacOS/"
sed "s/__VERSION__/$version/g" "$root/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
printf 'APPL????' > "$app/Contents/PkgInfo"
cp "$root/packaging/macos/DuckNote.icns" "$app/Contents/Resources/DuckNote.icns"
chmod 755 "$app/Contents/MacOS/DuckNote"

if command -v codesign >/dev/null && command -v hdiutil >/dev/null; then
  # firma ad hoc dall'interno all'esterno: le librerie, l'eseguibile, poi il bundle
  find "$app/Contents/MacOS" -name '*.dylib' -exec codesign --force --sign - {} \;
  codesign --force --sign - "$app/Contents/MacOS/DuckNote"
  codesign --force --sign - "$app"
  codesign --verify --deep --strict --verbose=1 "$app"

  stage="$work/dmg"
  mkdir -p "$stage"
  cp -R "$app" "$stage/"
  ln -s /Applications "$stage/Applications"
  rm -f "$out/$name.dmg"
  # hdiutil a volte trova il disco "occupato" (succede sulle macchine di CI): si riprova
  for attempt in 1 2 3; do
    if hdiutil create -volname "DuckNote $version" -srcfolder "$stage" -fs HFS+ -format UDZO -ov "$out/$name.dmg" >/dev/null; then
      break
    fi
    [ "$attempt" = 3 ] && { echo "hdiutil non riesce a creare $name.dmg" >&2; exit 1; }
    sleep 5
  done
  echo "$out/$name.dmg"
else
  rm -f "$out/$name.zip"
  (cd "$work" && zip -qry "$out/$name.zip" DuckNote.app)
  echo "$out/$name.zip"
fi
