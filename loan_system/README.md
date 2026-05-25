# Loan System

A Flask and PostgreSQL loan management system for tracking clients, loan amounts, interest, repayments, and available capital.

## Features

- Admin login with bcrypt password support
- CSRF protection for all write actions
- Create, read, update, and delete loans
- Search and paginate loan records
- Dashboard totals for available capital, expected repayments, projected profit, and loan count
- Responsive HTML templates with inline loan editing

## Setup

1. Create and activate a virtual environment:

   ```bash
   python3 -m venv .venv
   source .venv/bin/activate
   ```

2. Install dependencies:

   ```bash
   pip install -r requirements.txt
   ```

3. Create the PostgreSQL database:

   ```bash
   createdb microfinance
   psql microfinance < schema.sql
   ```

4. Create an admin user. Replace the password before production:

   ```bash
   python3 - <<'PY'
   import bcrypt
   print(bcrypt.hashpw(b"admin123", bcrypt.gensalt()).decode())
   PY
   ```

   ```bash
   psql microfinance
   INSERT INTO admins (username, password) VALUES ('admin', 'PASTE_HASH_HERE');
   ```

5. Start the app:

   ```bash
   export SECRET_KEY="change-this-secret"
   flask --app app run --host 0.0.0.0 --port 5050
   ```

Open `http://localhost:5050` and sign in with the admin account.

## Configuration

The app reads these optional environment variables:

- `SECRET_KEY`: Flask session secret
- `DB_NAME`: PostgreSQL database name, defaults to `microfinance`
- `DB_USER`: PostgreSQL user, defaults to your current PostgreSQL user
- `DB_PASSWORD`: PostgreSQL password
- `DB_HOST`: PostgreSQL host
- `DB_PORT`: PostgreSQL port
- `INTEREST_RATE`: interest rate as a decimal, defaults to `0.50`
- `START_CAPITAL`: starting capital amount, defaults to `10000`
- `PAGE_SIZE`: loans per dashboard page, defaults to `10`

## Tests

Run the test suite from this directory:

```bash
pytest
```
