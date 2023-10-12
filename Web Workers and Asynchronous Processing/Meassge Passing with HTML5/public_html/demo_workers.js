// demo_workers.js

self.onmessage = function(event) {
    var input = event.data;

    // Do some processing here, for example, calculate the current time after the given input milliseconds
    var currentTime = new Date(Date.now() + input).toLocaleTimeString();

    // Send the result back to the main thread
    self.postMessage(currentTime);
};

