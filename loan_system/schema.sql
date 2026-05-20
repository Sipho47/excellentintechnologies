CREATE TABLE IF NOT EXISTS admins (
    id SERIAL PRIMARY KEY,
    username TEXT UNIQUE NOT NULL,
    password TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS loans (
    id SERIAL PRIMARY KEY,
    client_name TEXT NOT NULL,
    loan_amount NUMERIC(12, 2) NOT NULL CHECK (loan_amount > 0),
    interest NUMERIC(12, 2) NOT NULL CHECK (interest >= 0),
    repayment NUMERIC(12, 2) NOT NULL CHECK (repayment >= loan_amount),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    phone TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_loans_client_name ON loans (client_name);
CREATE INDEX IF NOT EXISTS idx_loans_phone ON loans (phone);
CREATE INDEX IF NOT EXISTS idx_loans_created_at ON loans (created_at DESC);
