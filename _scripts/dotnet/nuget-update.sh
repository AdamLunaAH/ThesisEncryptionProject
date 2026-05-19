#!/bin/bash
#To make the .sh file executable
#sudo chmod +x ./nuget-update.sh

#Check if target folder parameter is provided
if [ "$1" == "" ]
then
    printf "\n\nError: Target folder path is required\n"
    printf "Usage: ./nuget-update.sh <folder-path-containing-slnx>\n"
    exit 1
fi

#Check if folder exists
if [ ! -d "$1" ]
then
    printf "\n\nError: Folder does not exist: $1\n"
    exit 1
fi

#Check if folder contains a .slnx file
if ! ls "$1"/*.slnx 1> /dev/null 2>&1; then
    printf "\n\nError: No .slnx file found in folder: $1\n"
    exit 1
fi

cd "$1"
dotnet nuget locals all --clear
dotnet list package --outdated

#clear the local nuget cache
#dotnet nuget locals all --clear

#update efc
#dotnet tool update --global dotnet-ef