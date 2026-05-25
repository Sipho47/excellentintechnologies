# HackOps Security Console

A Flask-powered security assessment console with a static web UI for authorized scanning, risk review, and hardening recommendations.

## Requirements

- Python 3.14+
- `nmap` installed and available on your `PATH`

## Setup

```bash
python -m venv venv
source venv/bin/activate
pip install -r requirements.txt
python server.py
```

Open `index.html` in a browser, or serve the folder with your preferred static file server. The backend runs on `http://localhost:5000`.

Only scan systems you own or have explicit permission to assess.
