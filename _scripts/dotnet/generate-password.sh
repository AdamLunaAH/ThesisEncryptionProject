#!/bin/bash
#To make the .sh file executable
#sudo chmod +x ./generate-password.sh

# Generates a strong random string passwords in the format: Rx0aKu-OkDdDf-qXDvSL
# using openssl for cryptographically strong random number generation

#usage
password=$(openssl rand -base64 64  | tr -d '/+=' | head -c 18)
#password=$(openssl rand -base64 64 | tr -d '/+=' | tr '[:upper:]' '[:lower:]' | head -c 18)

# Break password into three groups of 6 characters with hyphens
part1=$(echo "$password" | cut -c1-6)
part2=$(echo "$password" | cut -c7-12)
part3=$(echo "$password" | cut -c13-18)

formatted_password="${part1}-${part2}-${part3}"
echo "$formatted_password"
