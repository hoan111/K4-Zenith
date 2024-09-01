#!/bin/bash

# Color definitions
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print custom messages
print_message() {
    local color=$1
    local message=$2
    echo -e "${color}[ INFO ] ${message}${NC}"
}

# Run dotnet publish and display output in real-time
print_message "${BLUE}" "Running dotnet publish..."
dotnet publish -f net8.0 -c Release
exit_code=$?

# Check if dotnet publish was successful
if [ $exit_code -ne 0 ]; then
    print_message "${RED}" "dotnet publish failed with exit code $exit_code"
    exit 1
else
    print_message "${GREEN}" "dotnet publish completed successfully"
fi

# Rest of the script remains the same...
# Delete deploy folder if exists
print_message "${BLUE}" "Deleting existing deploy folders..."
rm -rf ./Zenith/plugins ./Zenith/shared

# Create deploy folders
print_message "${BLUE}" "Creating deploy folders..."
mkdir -p ./Zenith/plugins ./Zenith/shared

# Copy main plugin folder to plugins folder, ignoring specific file names
print_message "${YELLOW}" "Copying main plugin files..."
rsync -a --quiet --exclude="K4-ZenithAPI.dll" --exclude="KitsuneMenu.dll" --exclude="KitsuneMenu.pdb" ./src/bin/K4-Zenith/plugins/K4-Zenith/ ./Zenith/plugins/K4-Zenith/

# Copy shared folder's dirs to shared folder
print_message "${YELLOW}" "Copying shared files..."
cp -R ./src/bin/K4-Zenith/shared/* ./Zenith/shared/ 2>/dev/null

# Copy modules to plugins folder
print_message "${YELLOW}" "Copying TimeStats module..."
rsync -a --quiet --exclude="KitsuneMenu.dll" --exclude="KitsuneMenu.pdb" ./modules/time-stats/bin/K4-Zenith-TimeStats/ ./Zenith/plugins/K4-Zenith-TimeStats/

print_message "${YELLOW}" "Copying Ranks module..."
cp -R ./modules/ranks/bin/K4-Zenith-Ranks ./Zenith/plugins/ 2>/dev/null

print_message "${YELLOW}" "Copying Statistics module..."
rsync -a --quiet --exclude="KitsuneMenu.dll" --exclude="KitsuneMenu.pdb" ./modules/statistics/bin/K4-Zenith-Stats/ ./Zenith/plugins/K4-Zenith-Stats/

print_message "${YELLOW}" "Copying Admin module..."
rsync -a --quiet --exclude="KitsuneMenu.dll" --exclude="KitsuneMenu.pdb" ./modules/zenith-bans/bin/K4-Zenith-Bans/ ./Zenith/plugins/K4-Zenith-Bans/

print_message "${YELLOW}" "Copying Extended Commands module..."
rsync -a --quiet ./modules/extended-commands/bin/K4-Zenith-ExtendedCommands/ ./Zenith/plugins/K4-Zenith-ExtendedCommands/

print_message "${YELLOW}" "Copying Custom Tags module..."
rsync -a --quiet ./modules/custom-tags/bin/K4-Zenith-CustomTags/ ./Zenith/plugins/K4-Zenith-CustomTags/

# Delete files with a specific extension from Zenith and sub-folders
print_message "${BLUE}" "Cleaning up unnecessary files..."
find ./Zenith -type f \( -name "*.pdb" -o -name "*.yaml" -o -name ".DS_Store" \) -delete 2>/dev/null

print_message "${GREEN}" "Deployment completed successfully!"
