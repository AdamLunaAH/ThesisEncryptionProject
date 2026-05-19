#!/bin/bash

# Bash script to make all .sh files in a specified directory executable
# Run this script with: ./make-scripts-executable.sh <startDirectory>
# Make sure this script is executable first with: chmod +x make-scripts-executable.sh

if [[ -z "$1" ]]; then
    echo "Missing parameters:"
    echo "  ./make-scripts-executable.sh <startDirectory>"
    exit 1
fi

echo "Making all .sh files in the specified directory executable..."

# Resolve absolute path for start directory
START_DIR="$(realpath "$1")"
echo "Start directory: $START_DIR"

# Find all .sh files recursively in the start directory
sh_files=$(find "$START_DIR" -name "*.sh" -type f)
total_count=$(echo "$sh_files" | wc -l)

echo "Found $total_count .sh files"

# Make all .sh files executable
find "$START_DIR" -name "*.sh" -type f -exec chmod +x {} \;

echo ""
echo "List of .sh files found:"
while IFS= read -r file; do
    relative_path="${file#$START_DIR}"
    relative_path="${relative_path#/}"
    echo "  $relative_path"
done <<< "$sh_files"

echo ""
echo "Total .sh files processed: $total_count"
echo "All .sh files in the specified directory are now executable!"