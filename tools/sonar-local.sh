#!/usr/bin/env bash
# tools/sonar-local.sh - reproduce the SonarCloud C# analysis locally. Nothing is uploaded.
#
# Dependency note: SonarAnalyzer.CSharp is the Roslyn analyzer package that contains the very rules
# SonarCloud runs for C# (S1192, S3776, S107, ...). Running it as a plain analyzer during `dotnet build`
# needs no server, no token and no network after the package is restored, so sample code never leaves
# the machine (AGENTS.md, "Local only"). It is injected into a throwaway copy of the working tree and
# never added to the repository's own projects. The version is pinned (SONAR_ANALYZER_VERSION below).
#
# What it does:
#   1. copies the working tree (uncommitted changes included) to a temp dir under .index/sonar-local/;
#   2. writes Directory.Build.targets (PackageReference SonarAnalyzer.CSharp, AnalysisMode=All,
#      TreatWarningsAsErrors=false, SARIF 2.1 error log per project) and a .globalconfig that turns on the
#      rules that are off by default in the package but active in the SonarCloud profile (S107, S1192, S3776);
#   3. builds the solution, parses the SARIF and prints rule, file, line and message;
#      (Sonar S-rules other than S2699 are skipped in *.Tests/, as SonarCloud does for test scope);
#   4. exits 1 if any finding of the tracked rule set remains (generated code and obj/ are ignored).
#
# Usage: tools/sonar-local.sh [--keep]     (--keep leaves the temp copy for inspection)
# Env:   SONAR_LOCAL_RULES  comma separated rule ids, replaces the default tracked set.
set -euo pipefail

SONAR_ANALYZER_VERSION="10.35.0.4138"
DEFAULT_RULES="S1066,S107,S1075,S108,S1144,S1192,S127,S2583,S2699,S3776,S3928,S6966,CA1068,CA2208,CA1816,CA1822,CA1859,CA1861,CA1869,CA1875,SYSLIB1045,xUnit1045"
RULES="${SONAR_LOCAL_RULES:-$DEFAULT_RULES}"
KEEP=0
[[ "${1:-}" == "--keep" ]] && KEEP=1

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mkdir -p "$ROOT/.index/sonar-local"
WORK="$(mktemp -d "$ROOT/.index/sonar-local/run.XXXXXX")"
cleanup() { [[ $KEEP -eq 1 ]] && echo "kept: $WORK" || rm -rf "$WORK"; }
trap cleanup EXIT

# 1. copy of the working tree, build outputs and heavy/ignored folders excluded
rsync -a \
  --exclude '.git' --exclude 'bin' --exclude 'obj' --exclude '.index' --exclude 'samples' \
  --exclude 'testrepo' --exclude 'TestResults' --exclude 'coverage' \
  "$ROOT/" "$WORK/"

# 2. injected analyzer configuration
cat > "$WORK/Directory.Build.targets" <<TARGETS
<Project>
  <PropertyGroup>
    <AnalysisMode>All</AnalysisMode>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>false</EnforceCodeStyleInBuild>
    <ErrorLog>\$(SonarSarifDir)/\$(MSBuildProjectName).sarif,version=2.1</ErrorLog>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SonarAnalyzer.CSharp" Version="$SONAR_ANALYZER_VERSION" PrivateAssets="all" />
  </ItemGroup>
</Project>
TARGETS

cat > "$WORK/.globalconfig" <<'CFG'
is_global = true
global_level = 1000
dotnet_diagnostic.S107.severity = warning
dotnet_diagnostic.S1192.severity = warning
dotnet_diagnostic.S3776.severity = warning
dotnet_diagnostic.S1066.severity = warning
dotnet_diagnostic.S1075.severity = warning
dotnet_diagnostic.S108.severity = warning
dotnet_diagnostic.S1144.severity = warning
dotnet_diagnostic.S127.severity = warning
dotnet_diagnostic.S2583.severity = warning
dotnet_diagnostic.S2699.severity = warning
dotnet_diagnostic.S3928.severity = warning
dotnet_diagnostic.S6966.severity = warning
CFG

# 3. build
SARIF_DIR="$WORK/.sarif"
mkdir -p "$SARIF_DIR"
echo "Building analysis copy (SonarAnalyzer.CSharp $SONAR_ANALYZER_VERSION)..."
if ! dotnet build "$WORK/Polifemo.slnx" -nologo -v q -m \
     -p:SonarSarifDir="$SARIF_DIR" -p:TreatWarningsAsErrors=false > "$WORK/build.log" 2>&1; then
  grep -E " error " "$WORK/build.log" | sort -u | cut -c1-300 | head -40
  echo "sonar-local: build failed" >&2
  exit 2
fi

# 4. report
python3 -I - "$WORK" "$SARIF_DIR" "$RULES" <<'PY'
import glob, json, os, re, sys
from urllib.parse import unquote, urlparse
work, sarif_dir, rules = sys.argv[1], sys.argv[2], set(sys.argv[3].split(","))
found = set()
for path in glob.glob(os.path.join(sarif_dir, "*.sarif")):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    for run in data.get("runs", []):
        for r in run.get("results", []):
            if r.get("ruleId") not in rules or r.get("suppressions"):
                continue
            for loc in r.get("locations", []):
                phys = loc.get("physicalLocation", {})
                uri = phys.get("artifactLocation", {}).get("uri", "")
                file = unquote(urlparse(uri).path) if uri.startswith("file:") else uri
                rel = os.path.relpath(file, os.path.realpath(work)) if os.path.isabs(file) else file
                if rel.endswith(".g.cs") or ".generated." in rel or rel.startswith("obj/") or "/obj/" in rel:
                    continue
                # SonarCloud analyses test projects in test scope: only the test-specific Sonar rule applies there.
                if ".Tests/" in rel and re.fullmatch(r"S\d+", r["ruleId"]) and r["ruleId"] != "S2699":
                    continue
                line = phys.get("region", {}).get("startLine", 0)
                found.add((r["ruleId"], rel, line, r.get("message", {}).get("text", "").replace("\n", " ")))
for rule, rel, line, msg in sorted(found):
    print(f"{rule}\t{rel}:{line}\t{msg}")
print(f"sonar-local: {len(found)} finding(s)")
sys.exit(1 if found else 0)
PY
