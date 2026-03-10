#!/bin/bash

IMAGE_NAME="bartje2/tasktracker2:v1.0"
DOCKERFILE_PATH="infrastructure/Dockerfile"

echo "Starting build for $IMAGE_NAME..."

# Build from repo root as context
if docker build -t $IMAGE_NAME -f $DOCKERFILE_PATH .; then
    echo "Build successful! Pushing to Docker Hub..."
    docker push $IMAGE_NAME
else
    echo "Error: Docker build failed. Push aborted."
fi
