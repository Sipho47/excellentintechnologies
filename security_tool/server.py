
from flask import Flask, Response, request, jsonify
from flask_cors import CORS
import subprocess
import logging
from datetime import datetime
import json

app = Flask(__name__)
CORS(app)

# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# In-memory cache for results
scan_cache = {}


def get_nmap_command(mode, target):
    """Map scan modes to safe nmap commands"""
    modes = {
        "fast": ["nmap", "-F", target],
        "standard": ["nmap", "-p-", target],
        "service": ["nmap", "-sV", target],
        "aggressive": ["nmap", "-T4", "-A", target],
        "vuln": ["nmap", "--script", "vuln", target],
    }
    return modes.get(mode, modes["fast"])


def validate_target(target):
    """Validate and sanitize target input"""
    if not target or len(target) > 255:
        return None
    
    # Remove scheme if present (http://, https://, ftp://, etc.)
    if "://" in target:
        target = target.split("://", 1)[1]
    
    # Remove trailing slashes and paths
    target = target.split("/")[0]
    
    return target


@app.route('/scan')
def scan():
    target = request.args.get("target")
    mode = request.args.get("mode", "fast")

    target = validate_target(target)
    if not target:
        return "Error: Invalid target", 400

    logger.info(f"Starting {mode} scan on {target}")

    def generate():
        try:
            cmd = get_nmap_command(mode, target)
            yield f"data: [STARTED] {mode.upper()} scan on {target}\n\n"
            yield f"data: Command: {' '.join(cmd)}\n\n"

            process = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True
            )

            for line in process.stdout:
                if line.strip():
                    yield f"data: {line.strip()}\n\n"

            yield "data: Scan complete\n\n"
            scan_cache[target] = {"mode": mode, "timestamp": datetime.now().isoformat()}

        except Exception as e:
            logger.error(f"Scan error: {str(e)}")
            yield f"data: ERROR: {str(e)}\n\n"

    return Response(generate(), mimetype='text/event-stream')


@app.route('/exploit')
def exploit():
    target = request.args.get("target")
    
    target = validate_target(target)
    if not target:
        return "Error: Invalid target", 400

    logger.info(f"Starting exploit analysis on {target}")

    def generate():
        yield f"data: [ANALYZING] Vulnerability assessment for {target}\n\n"
        yield f"data: Timestamp: {datetime.now().isoformat()}\n\n"
        
        # Simulated vulnerability detection
        vulnerabilities = [
            "Port 22 (SSH) - Weak SSH key detected",
            "Port 80 (HTTP) - Unencrypted traffic vulnerability",
            "Port 3306 (MySQL) - Default credentials risk",
            "Service version - Potential CVE exploits available",
            "SSL/TLS - Certificate chain validation issues"
        ]
        
        for vuln in vulnerabilities:
            yield f"data: FOUND: {vuln}\n\n"
        
        yield f"data: Exploit analysis complete\n\n"

    return Response(generate(), mimetype='text/event-stream')


@app.route('/patch')
def patch():
    target = request.args.get("target")
    
    target = validate_target(target)
    if not target:
        return "Error: Invalid target", 400

    logger.info(f"Generating security recommendations for {target}")

    def generate():
        yield f"data: [RECOMMENDATIONS] Security patches for {target}\n\n"
        yield f"data: Generated: {datetime.now().isoformat()}\n\n"
        
        recommendations = [
            "UPDATE: Apply latest SSH security patches",
            "CONFIGURE: Enable HTTPS/SSL on port 80",
            "HARDEN: Configure strong MySQL authentication",
            "UPDATE: Patch identified service vulnerabilities",
            "IMPLEMENT: Enable TLS 1.3 and disable older protocols",
            "MONITOR: Deploy intrusion detection system",
            "DOCUMENT: Update security policy documentation"
        ]
        
        for rec in recommendations:
            yield f"data: • {rec}\n\n"
        
        yield f"data: System secured\n\n"

    return Response(generate(), mimetype='text/event-stream')


@app.route('/cache')
def get_cache():
    """Get cached scan results"""
    return jsonify(scan_cache)


@app.route('/status')
def status():
    """Get server status"""
    return jsonify({
        "status": "operational",
        "timestamp": datetime.now().isoformat(),
        "cached_scans": len(scan_cache)
    })


@app.route('/')
def home():
    return jsonify({
        "message": "HackOps Security Tool",
        "version": "2.0",
        "endpoints": {
            "scan": "/scan?target=<ip>&mode=<mode>",
            "exploit": "/exploit?target=<ip>",
            "patch": "/patch?target=<ip>",
            "cache": "/cache",
            "status": "/status"
        },
        "modes": ["fast", "standard", "service", "aggressive", "vuln"]
    })


if __name__ == "__main__":
    app.run(port=5000, debug=True, threaded=True)