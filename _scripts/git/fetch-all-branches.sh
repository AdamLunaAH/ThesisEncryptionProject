#!/bin/bash

# Bash script to fetch all remote branches and create local tracking branches
# Run this script with: ./fetch-all-branches.sh
# Make sure the script is executable with: chmod +x fetch-all-branches.sh


echo -e "Fetching all remote branches..."

# Fetch all remote branches
git fetch --all

echo -e "\nCreating local tracking branches for all remote branches..."

# Get all remote branches (excluding HEAD pointer)
remote_branches=$(git branch -r | grep -v 'HEAD')

for remote_branch in $remote_branches; do
    # Clean up the branch name (remove whitespace and 'origin/' prefix)
    branch_name=$(echo "$remote_branch" | sed 's/^[[:space:]]*//' | sed 's/origin\///')
    
    # Check if local branch already exists
    if git branch --list "$branch_name" | grep -q "$branch_name"; then
        echo -e "Local branch '$branch_name' already exists, skipping..."
    else
        echo -e "Creating local branch '$branch_name' tracking 'origin/$branch_name'..."
        git checkout -b "$branch_name" "origin/$branch_name"
    fi
done

echo -e "\nSwitching back to main branch..."
git checkout main

echo -e "\nAll local branches:"
git branch

echo -e "\nScript complete! All remote branches now have local tracking branches."