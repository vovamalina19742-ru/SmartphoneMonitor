#!/usr/bin/env bash
# Deploy matching_service to /DATA/services/matching_service on Linux node (192.168.100.22)
set -e

DEST_DIR="/DATA/services/matching_service"
echo "[1/4] Preparing remote directory: $DEST_DIR"
sudo mkdir -p "$DEST_DIR"
sudo chown -R "$USER:$USER" "$DEST_DIR"

echo "[2/4] Copying files..."
cp main.py requirements.txt "$DEST_DIR/"

echo "[3/4] Installing Python dependencies..."
python3 -m pip install -q --no-cache-dir -r "$DEST_DIR/requirements.txt" || true

echo "[4/4] Installing and enabling systemd unit..."
sudo cp matching_service.service /etc/systemd/system/matching_service.service
sudo systemctl daemon-reload
sudo systemctl enable --now matching_service.service
sudo systemctl status matching_service.service --no-pager

echo "[OK] Matching Service deployed and active on port 8000!"
