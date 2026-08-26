#!/usr/bin/env bash
# THE ESTATE KIT — fetch the newest proving template + manifest, verified.
#
#   get-template.sh --from <dir> [--out <dir>]
#   get-template.sh --feed <org-url> --project <p> --feed-name <f> --package <n> [--out <dir>]
#
# Two channels. `--from` is the share-path / local-directory source — the
# day-one fallback (a network share the bake copies to, or a directory the
# pipeline artifact was downloaded into). `--feed` pulls the latest version
# from the Azure Artifacts Universal Packages feed the nightly publishes to
# (requires the az CLI with the azure-devops extension, signed in). Either
# way the pair is verified — the manifest's sha256 and byte count against
# the artifact — before it lands in --out (default ./templates), and the
# template's identity is printed from the manifest.
set -euo pipefail
. "$(dirname "$0")/kit-common.sh"

FROM="" ORG="" PROJECT="" FEED="" PACKAGE="" OUT="./templates"
while [ $# -gt 0 ]; do
    case "$1" in
        --from)      FROM="$2"; shift 2 ;;
        --feed)      ORG="$2"; shift 2 ;;
        --project)   PROJECT="$2"; shift 2 ;;
        --feed-name) FEED="$2"; shift 2 ;;
        --package)   PACKAGE="$2"; shift 2 ;;
        --out)       OUT="$2"; shift 2 ;;
        *) kit_die "unknown argument: $1 (see the header for usage)" ;;
    esac
done

mkdir -p "$OUT"

if [ -n "$FROM" ]; then
    kit_newest_template "$FROM"
    cp "$KIT_TEMPLATE_BAK" "$KIT_TEMPLATE_MANIFEST" "$OUT/"
elif [ -n "$ORG" ]; then
    [ -n "$PROJECT" ] && [ -n "$FEED" ] && [ -n "$PACKAGE" ] || kit_die "--feed needs --project, --feed-name, and --package"
    command -v az >/dev/null 2>&1 || kit_die "the az CLI is not installed; use --from <dir> (the share-path fallback) until it is"
    kit_log "downloading the latest $PACKAGE from $FEED..."
    az artifacts universal download \
        --organization "$ORG" --project "$PROJECT" --scope project \
        --feed "$FEED" --name "$PACKAGE" --version '*' --path "$OUT" >/dev/null
else
    kit_die "name a source: --from <dir> or --feed <org-url> ..."
fi

kit_newest_template "$OUT"
kit_verify_template "$KIT_TEMPLATE_BAK" "$KIT_TEMPLATE_MANIFEST"
IDENTITY="$(node -e '
  const m = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
  process.stdout.write(`${m.template} · commit ${m.commit.slice(0, 8)} · data ${m.fingerprints.data.slice(0, 8)} · baked ${m.bakedAtUtc} · lane ${m.lane}`);
' "$KIT_TEMPLATE_MANIFEST")"
kit_log "verified: $IDENTITY"
kit_log "template: $KIT_TEMPLATE_BAK"
