#!/usr/bin/env bash
# DuckNote per macOS: publish, bundle DuckNote.app, firma, immagine disco.
#
#   packaging/macos/build-app.sh <osx-arm64|osx-x64> <versione> <cartella di uscita>
#
# Sul Mac (codesign e hdiutil) produce DuckNote-<versione>-macos-<arch>.dmg con la finestra
# disegnata: lo sfondo di make_dmg_background.py, le icone al loro posto (dmg_settings.py).
#
# La firma:
# - con DUCKNOTE_SIGN_IDENTITY ("Developer ID Application: Nome (TEAMID)", nel portachiavi)
#   l'app si firma col runtime rinforzato e si notarizza da Apple (APPLE_ID, APPLE_TEAM_ID,
#   APPLE_APP_PASSWORD: una password per app di account.apple.com), poi si "pinza" il biglietto
#   all'app e all'immagine disco: si apre al primo doppio clic, senza passare dalle Impostazioni;
# - senza, firma ad hoc: la prima apertura passa da Impostazioni di Sistema > Privacy e
#   sicurezza > Apri comunque (gli aggiornamenti installati da DuckNote no: non sono in quarantena).
#
# Altrove produce lo stesso bundle in uno .zip: l'eseguibile e' gia' firmato ad hoc da dotnet.
set -euo pipefail

# le statistiche di compilazione di Avalonia (avaloniaui.net) restano spente
export AVALONIA_TELEMETRY_OPTOUT=1

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

identity="${DUCKNOTE_SIGN_IDENTITY:-}"

# la notarizzazione: si manda ad Apple, si aspetta il verdetto, e se non e' "Accepted" si mostra
# il registro di Apple e ci si ferma (un'immagine non notarizzata non deve uscire come tale)
notarize() {
  local file="$1" result id status
  result="$(xcrun notarytool submit "$file" --apple-id "${APPLE_ID:?}" --team-id "${APPLE_TEAM_ID:?}" \
    --password "${APPLE_APP_PASSWORD:?}" --wait --output-format json)"
  id="$(printf '%s' "$result" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("id",""))')"
  status="$(printf '%s' "$result" | python3 -c 'import json,sys; print(json.load(sys.stdin).get("status",""))')"
  if [ "$status" != "Accepted" ]; then
    echo "::error::notarizzazione di $(basename "$file"): $status" >&2
    [ -n "$id" ] && xcrun notarytool log "$id" --apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD" >&2 || true
    exit 1
  fi
}

if command -v codesign >/dev/null && command -v hdiutil >/dev/null; then
  if [ -n "$identity" ]; then
    # Developer ID: dall'interno all'esterno, runtime rinforzato, marca temporale di Apple
    sign=(codesign --force --timestamp --options runtime --sign "$identity")
    entitlements="$root/packaging/macos/DuckNote.entitlements"
    find "$app/Contents/MacOS" -name '*.dylib' -exec "${sign[@]}" {} \;
    "${sign[@]}" --entitlements "$entitlements" "$app/Contents/MacOS/DuckNote"
    "${sign[@]}" --entitlements "$entitlements" "$app"
    codesign --verify --deep --strict --verbose=1 "$app"
    # l'app si notarizza da sola (in uno zip) e le si pinza il biglietto: copiata fuori
    # dall'immagine disco si apre anche senza rete
    ditto -c -k --keepParent "$app" "$work/DuckNote.zip"
    notarize "$work/DuckNote.zip"
    xcrun stapler staple "$app"
  else
    # firma ad hoc dall'interno all'esterno: le librerie, l'eseguibile, poi il bundle
    find "$app/Contents/MacOS" -name '*.dylib' -exec codesign --force --sign - {} \;
    codesign --force --sign - "$app/Contents/MacOS/DuckNote"
    codesign --force --sign - "$app"
    codesign --verify --deep --strict --verbose=1 "$app"
  fi

  rm -f "$out/$name.dmg"

  # la finestra del .dmg (lo sfondo con papere e teschi, le icone al loro posto) la scrive
  # dmgbuild, senza pilotare il Finder; se non si riesce a installarlo resta l'immagine semplice
  if ! { python3 -m venv "$work/venv" >/dev/null 2>&1 \
    && "$work/venv/bin/pip" install --quiet "dmgbuild==1.6.7" >/dev/null 2>&1 \
    && "$work/venv/bin/dmgbuild" -s "$root/packaging/macos/dmg_settings.py" \
         -D app="$app" -D background="$root/packaging/macos/dmg-background.png" \
         -D icon="$root/packaging/macos/DuckNote.icns" "DuckNote $version" "$out/$name.dmg" >/dev/null; }; then
    echo "::warning::dmgbuild non e' andato: $name.dmg esce senza lo sfondo" >&2
    rm -f "$out/$name.dmg"
    stage="$work/dmg"
    mkdir -p "$stage"
    cp -R "$app" "$stage/"
    ln -s /Applications "$stage/Applicazioni"
    # hdiutil a volte trova il disco "occupato" (succede sulle macchine di CI): si riprova
    for attempt in 1 2 3; do
      if hdiutil create -volname "DuckNote $version" -srcfolder "$stage" -fs HFS+ -format UDZO -ov "$out/$name.dmg" >/dev/null; then
        break
      fi
      [ "$attempt" = 3 ] && { echo "hdiutil non riesce a creare $name.dmg" >&2; exit 1; }
      sleep 5
    done
  fi

  if [ -n "$identity" ]; then
    # anche l'immagine disco: firmata, notarizzata, col biglietto pinzato
    codesign --force --timestamp --sign "$identity" "$out/$name.dmg"
    notarize "$out/$name.dmg"
    xcrun stapler staple "$out/$name.dmg"
  fi
  echo "$out/$name.dmg"
else
  rm -f "$out/$name.zip"
  (cd "$work" && zip -qry "$out/$name.zip" DuckNote.app)
  echo "$out/$name.zip"
fi
