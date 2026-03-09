#!/bin/bash

IMAGE_NAME="bartje2/tasktracker:v2.0"
DOCKERFILE_PATH="TaskTrackerSolution2/infrastructure/Dockerfile"

echo "Starting build for $IMAGE_NAME..."

# Build from parent directory (DotNetSolutions/) as context
cd ..

if docker build -t $IMAGE_NAME -f $DOCKERFILE_PATH .; then
    echo "Build successful! Pushing to Docker Hub..."
    docker push $IMAGE_NAME
else
    echo "Error: Docker build failed. Push aborted."
fi
