addEventListener("message", function (evt) {
    var date = new Date();
    var currentDate = null;
    var targetDate = new Date(evt.data); // Extract the target date from the 'evt' object

    function sendMessage1() {
        if (w1) {
            var seconds = document.getElementById("secondsInput").value;
            w1.postMessage(seconds * 1000); // Convert seconds to milliseconds before sending as the message
        }
    }

    function sleep(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    (async function () {
        do {
            currentDate = new Date();
            postMessage(currentDate);
            await sleep(1000); // Use 'await' to pause execution for 1000 milliseconds
        } while (currentDate.getTime() < targetDate.getTime()); // Compare the time values of currentDate and targetDate
    })();
}, false);
