#!/bin/sh
# Copy HTML files to the host directory specified by HTML_DEST_DIR
if [ -n "$HTML_DEST_DIR" ]; then
    echo "Copying HTML files to $HTML_DEST_DIR..."
    cp -r ../src/client/* "$HTML_DEST_DIR"
    chmod -R a+r "$HTML_DEST_DIR"  # Ensure files are readable
else
    echo "HTML_DEST_DIR not set. Skipping copy."
fi

dotnet TaskTrackerServer2.dll

