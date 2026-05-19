# Script to remove all bin and obj folders from the solution

# Cleanup commands:
# 1. To preview only (default): ./cleanup.ps1
# 2. To actually delete folders: ./cleanup.ps1 -NoDryRun
# 3. To skip confirmation: ./cleanup.ps1 -NoDryRun -Force
# 4. To not wait before exit: ./cleanup.ps1 -NoDryRun -NoPause
# 5. To set max search depth: ./cleanup.ps1 -NoDryRun -Depth 5
# Depth means how many levels to search upwards for the solution root.
# 6. To show help: ./cleanup.ps1 -Help

# Note: It might auto-generate the files again or not able to delete the files if you have the solution open in Visual Studio or VS Code. To fix this, run the script from a terminal in the cleanup folder before running this script.

#!/usr/bin/env bash

# =========================
# DEFAULT CONFIGURATION
# =========================

DRY_RUN=true
REQUIRE_CONFIRMATION=true
PAUSE_ON_EXIT=true
MAX_SEARCH_DEPTH=4

FOLDERS=("bin" "obj")

# =========================
# CLI ARGUMENTS
# =========================

for arg in "$@"; do
    case $arg in
        --no-dry-run)
            DRY_RUN=false
            ;;
        --dry-run)
            DRY_RUN=true
            ;;
        --force)
            REQUIRE_CONFIRMATION=false
            ;;
        --no-pause)
            PAUSE_ON_EXIT=false
            ;;
        --depth=*)
            MAX_SEARCH_DEPTH="${arg#*=}"
            ;;
        --help)
            echo "Usage: ./clean.sh [options]"
            echo ""
            echo "Options:"
            echo "  --dry-run        Preview only (default)"
            echo "  --no-dry-run     Actually delete folders"
            echo "  --force          Skip confirmation prompt"
            echo "  --no-pause       Do not wait before exit"
            echo "  --depth=N        Max levels to search upwards (default: 4)"
            echo "  --help           Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $arg"
            ;;
    esac
done

# =========================
# FIND SOLUTION ROOT
# =========================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

find_solution_root() {
    local dir="$SCRIPT_DIR"
    local depth=0

    while [ "$dir" != "/" ] && [ $depth -le $MAX_SEARCH_DEPTH ]; do

        if ls "$dir"/*.sln >/dev/null 2>&1 || \
           ls "$dir"/*.slnx >/dev/null 2>&1 || \
           [ -d "$dir/.git" ]; then
            echo "$dir"
            return 0
        fi

        dir="$(dirname "$dir")"
        ((depth++))
    done

    # Fallback warning
    echo "WARNING: Solution root not found within depth ($MAX_SEARCH_DEPTH). Using script directory." >&2
    echo "$SCRIPT_DIR"
}

ROOT_DIR="$(find_solution_root)"

# =========================
# START
# =========================

echo "=== Solution Cleaner ==="
echo "Root directory: $ROOT_DIR"
echo "Max search depth: $MAX_SEARCH_DEPTH"

if [ "$DRY_RUN" = true ]; then
    echo "Mode: DRY RUN (no files will be deleted)"
else
    echo "Mode: LIVE (files will be deleted)"
fi

# Confirmation
if [ "$REQUIRE_CONFIRMATION" = true ]; then
    read -p "Proceed with cleaning? (y/n): " response
    if [[ "$response" != "y" ]]; then
        echo "Operation cancelled."
        if [ "$PAUSE_ON_EXIT" = true ]; then
            read -p "Press Enter to exit..."
        fi
        exit 0
    fi
fi

deletedCount=0

for folder in "${FOLDERS[@]}"; do
    while IFS= read -r dir; do

        if [ "$DRY_RUN" = true ]; then
            echo "[DRY RUN] Would remove: $dir"
        else
            echo "Removing: $dir"
            if rm -rf "$dir"; then
                ((deletedCount++))
            else
                echo "Failed to remove: $dir"
            fi
        fi

    done < <(
        find "$ROOT_DIR" -type d -name "$folder" \
        -not -path "*/.git/*"
    )
done

if [ "$DRY_RUN" = false ]; then
    echo "Deleted $deletedCount folders."
else
    echo "Dry run complete. No folders were deleted."
fi

echo "=== Done ==="

# Prevent terminal from closing
if [ "$PAUSE_ON_EXIT" = true ]; then
    read -p "Press Enter to exit..."
fi