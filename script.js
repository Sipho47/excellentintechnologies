const lunchBtn = document.getElementById('lunchBtn');
const teatimeBtn = document.getElementById('teatimeBtn');
const bothBtn = document.getElementById('bothBtn');
const lunchNumbersEl = document.getElementById('lunchNumbers');
const teatimeNumbersEl = document.getElementById('teatimeNumbers');
const lunchStatus = document.getElementById('lunchStatus');
const teatimeStatus = document.getElementById('teatimeStatus');

const DRAW_SIZE = 6;
const HIGHEST_NUMBER = 49;

function getRandomInt(maxExclusive) {
  const randomValues = new Uint32Array(1);
  const maxUnbiasedValue = Math.floor(0x100000000 / maxExclusive) * maxExclusive;

  do {
    crypto.getRandomValues(randomValues);
  } while (randomValues[0] >= maxUnbiasedValue);

  return randomValues[0] % maxExclusive;
}

function generateRandomNumbers(count = DRAW_SIZE, max = HIGHEST_NUMBER) {
  const pool = Array.from({ length: max }, (_, i) => i + 1);
  for (let i = pool.length - 1; i > 0; i -= 1) {
    const j = getRandomInt(i + 1);
    [pool[i], pool[j]] = [pool[j], pool[i]];
  }
  return pool.slice(0, count);
}

function formatDrawTime() {
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date());
}

function renderNumbers(numbersEl, statusEl) {
  const numbers = generateRandomNumbers();
  numbersEl.innerHTML = '';

  numbers.forEach((number) => {
    const pill = document.createElement('span');
    pill.className = 'number-pill';
    pill.textContent = number;
    numbersEl.appendChild(pill);
  });

  statusEl.textContent = `Generated ${formatDrawTime()}`;
}

lunchBtn.addEventListener('click', () => renderNumbers(lunchNumbersEl, lunchStatus));
teatimeBtn.addEventListener('click', () => renderNumbers(teatimeNumbersEl, teatimeStatus));
bothBtn.addEventListener('click', () => {
  renderNumbers(lunchNumbersEl, lunchStatus);
  renderNumbers(teatimeNumbersEl, teatimeStatus);
});
