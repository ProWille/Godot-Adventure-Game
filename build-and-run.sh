#!/bin/bash

# This project requires the mono/.NET edition of Godot.
# Set GODOT_BIN to override the binary name, e.g.: GODOT_BIN=godot ./build-and-run.sh
GODOT="${GODOT_BIN:-godot-mono}"

command -v "$GODOT" >/dev/null 2>&1 || {
    echo "Error: '$GODOT' was not found. Install the Godot mono/.NET edition (or set GODOT_BIN)." >&2
    exit 1
}

# Compile the code
"$GODOT" --headless --export-debug "Linux" builds/linux/Adventure-Game.x86_64

# Check if the compilation was successful
if [ $? -eq 0 ]; then
    echo "Build successful. Running the executable..."
    ./builds/linux/Adventure-Game.sh
else
    echo "Build failed. Not running the executable."
fi