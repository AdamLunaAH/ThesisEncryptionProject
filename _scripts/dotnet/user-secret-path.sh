#!/bin/bash
# get-user-secrets-path.sh

# Cross-platform script to get user secrets base folder

# sudo chmod +x ./user-secret-path.sh

get_user_secrets_folder() {
    if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "win32" || "$OSTYPE" == "cygwin" ]]; then
        # Windows (Git Bash, MSYS2, Cygwin)
        if [[ -n "$APPDATA" ]]; then
            echo "$APPDATA/Microsoft/UserSecrets"
        else
            echo "$HOME/AppData/Roaming/Microsoft/UserSecrets"
        fi
    else
        # macOS/Linux
        echo "$HOME/.microsoft/usersecrets"
    fi
}

# Usage
USER_SECRETS_BASE=$(get_user_secrets_folder)
echo "$USER_SECRETS_BASE"