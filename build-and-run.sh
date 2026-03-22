#!/bin/bash

# Compile the code
godot-mono --headless --export-debug "Linux" builds/linux/Adventure-Game.x86_64

# Check if the compilation was successful
if [ $? -eq 0 ]; then
    echo "Build successful. Running the executable..."
    ./builds/linux/Adventure-Game.sh
else
    echo "Build failed. Not running the executable."
fi
