// worker.js
function updateTime() {
    var currentTime = new Date().toLocaleTimeString();
    postMessage(currentTime);
}

setInterval(updateTime, 1000);

