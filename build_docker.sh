#!/bin/bash

# Define variables for easier updates
IMAGE_NAME="bartje2/tasktracker:v2.0"
DOCKERFILE_PATH="infrastructure/Dockerfile"

echo "Starting build for $IMAGE_NAME..."

# Build the image
# The -f flag points to the Dockerfile, the '.' sets the build context
if docker build -t $IMAGE_NAME --no-cache -f $DOCKERFILE_PATH .; then
    echo "Build successful! Pushing to Docker Hub..."
    docker push $IMAGE_NAME
else
    echo "Error: Docker build failed. Push aborted."
fi
