#!/bin/bash

# Get local IP address (for Wi-Fi interface)
IP=$(ipconfig getifaddr en0)

# Set the port
PORT=8000

# Get current directory
PROJECT_DIR=$(pwd)

# Output server info
echo "✅ Starting local server at: http://$IP:$PORT"
echo "📂 Serving files from: $PROJECT_DIR"
echo "🌐 Open this on your phone or another device on the same Wi-Fi: http://$IP:$PORT"

# Start the Python 3 HTTP server
python3 -m http.server "$PORT" --bind 0.0.0.0

