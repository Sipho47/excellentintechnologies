// worker.js

// Function to calculate the sum of numbers in a given range
function calculateSum(start, end) {
    let sum = 0;
    for (let i = start; i <= end; i++) {
        sum += i;
    }
    return sum;
}

// Listen for messages from the main thread
onmessage = function (event) {
    const { start, end } = event.data;
    const result = calculateSum(start, end);
    postMessage(result); // Send the result back to the main thread
};

