#!/bin/bash

# Install a third-party skill into the Tnzi application's skill data directory.
# Skills are stored per-tenant or per-user under {DataPath}/skills/{tenantId}/{userId}/.
#
# Usage: ./install-skill.sh <owner/repo@skill-name> <target-dir>
# Example: ./install-skill.sh anthropics/agent-skills@react-best-practices /app/App_Data/AI/skills/tenant-guid/user-guid

set -e

if [[ -z "$1" || -z "$2" ]]; then
  echo "Usage: $0 <owner/repo@skill-name> <target-dir>"
  echo ""
  echo "Arguments:"
  echo "  owner/repo@skill-name  Skill identifier from the open-skills ecosystem"
  echo "  target-dir             Target directory (e.g., {DataPath}/skills/{tenantId}/{userId})"
  exit 1
fi

FULL_SKILL_NAME="$1"
TARGET_DIR="$2"

# Extract parts
SKILL_NAME="${FULL_SKILL_NAME##*@}"
REPO_PART="${FULL_SKILL_NAME%%@*}"

if [[ -z "$SKILL_NAME" || "$SKILL_NAME" == "$FULL_SKILL_NAME" ]]; then
  echo "Error: Invalid skill format. Expected: owner/repo@skill-name"
  exit 1
fi

# Path traversal protection
if [[ "$SKILL_NAME" == *".."* || "$SKILL_NAME" == *"/"* || "$SKILL_NAME" == *"\\"* ]]; then
  echo "Error: Invalid skill name (path traversal detected)"
  exit 1
fi

SKILL_TARGET="$TARGET_DIR/$SKILL_NAME"

# Check if already installed
if [[ -d "$SKILL_TARGET" && -f "$SKILL_TARGET/SKILL.md" ]]; then
  echo "Skill '$SKILL_NAME' is already installed at $SKILL_TARGET"
  echo "To reinstall, remove it first: rm -rf $SKILL_TARGET"
  exit 0
fi

mkdir -p "$TARGET_DIR"

# Strategy 1: Use npx skills CLI if available
if command -v npx &> /dev/null; then
  echo "Installing '$SKILL_NAME' via skills CLI..."
  GLOBAL_CACHE="$HOME/.agents/skills/$SKILL_NAME"

  if ! npx skills add "$FULL_SKILL_NAME" -g -y > /dev/null 2>&1; then
    echo "npx skills CLI failed, trying git clone fallback..."
  fi

  if [[ -d "$GLOBAL_CACHE" && -f "$GLOBAL_CACHE/SKILL.md" ]]; then
    cp -r "$GLOBAL_CACHE" "$SKILL_TARGET"
    echo "Installed '$SKILL_NAME' → $SKILL_TARGET"
    exit 0
  fi
fi

# Strategy 2: Git clone and extract
if command -v git &> /dev/null; then
  echo "Falling back to git clone..."
  TEMP_DIR=$(mktemp -d)
  trap "rm -rf \"$TEMP_DIR\"" EXIT

  if git clone --depth 1 "https://github.com/$REPO_PART.git" "$TEMP_DIR/repo" 2>/dev/null; then
    SKILL_DIR=$(find "$TEMP_DIR/repo" -type f -name "SKILL.md" -path "*/$SKILL_NAME/*" 2>/dev/null | head -1)
    SKILL_DIR=$(dirname "$SKILL_DIR" 2>/dev/null)

    if [[ -n "$SKILL_DIR" && -f "$SKILL_DIR/SKILL.md" ]]; then
      cp -r "$SKILL_DIR" "$SKILL_TARGET"
      echo "Installed '$SKILL_NAME' → $SKILL_TARGET"
      exit 0
    fi
  fi
fi

echo "Error: Could not install skill '$SKILL_NAME'."
echo "Manual install: download the skill directory into $SKILL_TARGET/"
exit 1
