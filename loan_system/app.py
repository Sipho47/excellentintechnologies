import hmac
import os
import secrets
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from functools import wraps

import bcrypt
import psycopg2
import psycopg2.extras
from flask import (
    Flask,
    abort,
    flash,
    g,
    redirect,
    render_template,
    request,
    session,
    url_for,
)


app = Flask(__name__)
app.config.update(
    SECRET_KEY=os.environ.get("SECRET_KEY", "change-me-before-production"),
    SESSION_COOKIE_HTTPONLY=True,
    SESSION_COOKIE_SAMESITE="Lax",
    PERMANENT_SESSION_LIFETIME=60 * 60 * 8,
)

DB_CONFIG = {
    "dbname": os.environ.get("DB_NAME", "microfinance"),
    "user": os.environ.get("DB_USER") or None,
    "password": os.environ.get("DB_PASSWORD") or None,
    "host": os.environ.get("DB_HOST") or None,
    "port": os.environ.get("DB_PORT") or None,
}

INTEREST_RATE = Decimal(os.environ.get("INTEREST_RATE", "0.50"))
START_CAPITAL = Decimal(os.environ.get("START_CAPITAL", "10000"))
PAGE_SIZE = int(os.environ.get("PAGE_SIZE", "10"))
MONEY = Decimal("0.01")


def money(value):
    value = Decimal(value or 0).quantize(MONEY, rounding=ROUND_HALF_UP)
    return f"{value:,.2f}"


app.jinja_env.filters["money"] = money


def short_date(value):
    if not value:
        return ""
    if hasattr(value, "strftime"):
        return value.strftime("%Y-%m-%d %H:%M")
    return str(value)


app.jinja_env.filters["short_date"] = short_date


def get_db():
    if "db" not in g:
        config = {key: value for key, value in DB_CONFIG.items() if value}
        g.db = psycopg2.connect(**config)
    return g.db


@app.teardown_appcontext
def close_db(error=None):
    db = g.pop("db", None)
    if db is None:
        return
    if error:
        db.rollback()
    db.close()


def query_one(sql, params=()):
    with get_db().cursor(cursor_factory=psycopg2.extras.RealDictCursor) as cur:
        cur.execute(sql, params)
        return cur.fetchone()


def query_all(sql, params=()):
    with get_db().cursor(cursor_factory=psycopg2.extras.RealDictCursor) as cur:
        cur.execute(sql, params)
        return cur.fetchall()


def execute(sql, params=()):
    with get_db().cursor() as cur:
        cur.execute(sql, params)
        rowcount = cur.rowcount
    get_db().commit()
    return rowcount


def login_required(view):
    @wraps(view)
    def wrapped_view(*args, **kwargs):
        if "admin" not in session:
            flash("Please sign in to continue.", "warning")
            return redirect(url_for("login"))
        return view(*args, **kwargs)

    return wrapped_view


def csrf_token():
    token = session.get("csrf_token")
    if not token:
        token = secrets.token_urlsafe(32)
        session["csrf_token"] = token
    return token


@app.context_processor
def inject_template_globals():
    return {"csrf_token": csrf_token}


@app.before_request
def protect_forms():
    if request.method != "POST":
        return

    token = session.get("csrf_token", "")
    submitted = request.form.get("csrf_token", "")
    if not token or not hmac.compare_digest(token, submitted):
        abort(400, "Invalid form token.")


@app.after_request
def add_security_headers(response):
    response.headers.setdefault("X-Content-Type-Options", "nosniff")
    response.headers.setdefault("X-Frame-Options", "DENY")
    response.headers.setdefault("Referrer-Policy", "strict-origin-when-cross-origin")
    response.headers.setdefault("Cache-Control", "no-store")
    return response


def parse_money(raw_value):
    try:
        value = Decimal(str(raw_value).replace(",", "").strip())
    except (InvalidOperation, AttributeError):
        raise ValueError("Enter a valid loan amount.")

    if value <= 0:
        raise ValueError("Loan amount must be greater than zero.")

    return value.quantize(MONEY, rounding=ROUND_HALF_UP)


def clean_loan_form(form):
    name = form.get("name", "").strip()
    phone = form.get("phone", "").strip()

    if len(name) < 2:
        raise ValueError("Client name must be at least 2 characters.")
    if len(phone) < 7:
        raise ValueError("Telephone number must be at least 7 characters.")

    loan_amount = parse_money(form.get("loan"))
    interest = (loan_amount * INTEREST_RATE).quantize(MONEY, rounding=ROUND_HALF_UP)
    repayment = (loan_amount + interest).quantize(MONEY, rounding=ROUND_HALF_UP)

    return {
        "name": name,
        "phone": phone,
        "loan_amount": loan_amount,
        "interest": interest,
        "repayment": repayment,
    }


def get_page_number():
    try:
        page = int(request.args.get("page", "1"))
    except ValueError:
        page = 1
    return max(page, 1)


def loan_search_clause(search):
    if not search:
        return "", []

    pattern = f"%{search}%"
    return " WHERE client_name ILIKE %s OR phone ILIKE %s", [pattern, pattern]


def get_dashboard_totals():
    totals = query_one(
        """
        SELECT
            COALESCE(SUM(loan_amount), 0) AS lent_out,
            COALESCE(SUM(interest), 0) AS projected_profit,
            COALESCE(SUM(repayment), 0) AS expected_repayments,
            COUNT(*) AS loan_count
        FROM loans
        """
    )

    lent_out = Decimal(totals["lent_out"] or 0)
    projected_profit = Decimal(totals["projected_profit"] or 0)

    totals["start_capital"] = START_CAPITAL
    totals["available_capital"] = START_CAPITAL - lent_out
    totals["projected_capital"] = START_CAPITAL + projected_profit
    return totals


def count_loans(search=""):
    where_sql, params = loan_search_clause(search)
    row = query_one(f"SELECT COUNT(*) AS total FROM loans{where_sql}", params)
    return int(row["total"] or 0)


def fetch_loans(search="", page=1, per_page=PAGE_SIZE):
    where_sql, params = loan_search_clause(search)
    offset = (page - 1) * per_page
    sql = """
        SELECT
            id,
            client_name,
            loan_amount,
            interest,
            repayment,
            created_at AS loan_date,
            phone
        FROM loans
        {where_sql}
        ORDER BY id DESC
        LIMIT %s OFFSET %s
    """
    return query_all(sql.format(where_sql=where_sql), params + [per_page, offset])


def fetch_loan(loan_id):
    return query_one(
        """
        SELECT id, client_name, loan_amount, interest, repayment, created_at AS loan_date, phone
        FROM loans
        WHERE id=%s
        """,
        (loan_id,),
    )


def build_pagination(total, page, per_page=PAGE_SIZE):
    total_pages = max((total + per_page - 1) // per_page, 1)
    page = min(page, total_pages)
    start = 0 if total == 0 else ((page - 1) * per_page) + 1
    end = min(page * per_page, total)

    return {
        "page": page,
        "per_page": per_page,
        "total": total,
        "total_pages": total_pages,
        "start": start,
        "end": end,
        "has_prev": page > 1,
        "has_next": page < total_pages,
        "prev_page": page - 1,
        "next_page": page + 1,
    }


@app.route("/", methods=["GET", "POST"])
def login():
    if "admin" in session:
        return redirect(url_for("dashboard"))

    if request.method == "POST":
        username = request.form.get("username", "").strip()
        password = request.form.get("password", "")

        admin = query_one(
            "SELECT username, password FROM admins WHERE username=%s",
            (username,),
        )

        if admin and password_matches(username, password, admin["password"]):
            session.clear()
            session.permanent = True
            session["admin"] = admin["username"]
            csrf_token()
            flash("Welcome back.", "success")
            return redirect(url_for("dashboard"))

        flash("Invalid username or password.", "danger")

    return render_template("login.html")


def password_matches(username, password, stored_password):
    if not stored_password:
        return False

    try:
        if stored_password.startswith("$2"):
            return bcrypt.checkpw(password.encode(), stored_password.encode())

        if hmac.compare_digest(password, stored_password):
            new_hash = bcrypt.hashpw(password.encode(), bcrypt.gensalt()).decode()
            execute(
                "UPDATE admins SET password=%s WHERE username=%s",
                (new_hash, username),
            )
            return True
    except ValueError:
        return False

    return False


@app.route("/dashboard", methods=["GET", "POST"])
@login_required
def dashboard():
    form_data = {}

    if request.method == "POST":
        try:
            loan = clean_loan_form(request.form)
            execute(
                """
                INSERT INTO loans
                    (client_name, loan_amount, interest, repayment, phone)
                VALUES (%s, %s, %s, %s, %s)
                """,
                (
                    loan["name"],
                    loan["loan_amount"],
                    loan["interest"],
                    loan["repayment"],
                    loan["phone"],
                ),
            )
            flash(f"Loan added for {loan['name']}.", "success")
            return redirect(url_for("dashboard"))
        except ValueError as exc:
            flash(str(exc), "danger")
            form_data = request.form

    search = request.args.get("q", "").strip()
    page = get_page_number()
    total_loans = count_loans(search)
    pagination = build_pagination(total_loans, page)
    return render_template(
        "dashboard.html",
        loans=fetch_loans(search, pagination["page"], pagination["per_page"]),
        totals=get_dashboard_totals(),
        search=search,
        interest_rate=INTEREST_RATE,
        pagination=pagination,
        form_data=form_data,
    )


@app.route("/loan/<int:loan_id>")
@login_required
def view_loan(loan_id):
    loan = fetch_loan(loan_id)
    if not loan:
        abort(404)

    return render_template("view.html", loan=loan)


@app.route("/edit/<int:loan_id>", methods=["GET", "POST"])
@login_required
def edit(loan_id):
    loan = fetch_loan(loan_id)

    if not loan:
        abort(404)

    if request.method == "POST":
        try:
            form_loan = clean_loan_form(request.form)
            execute(
                """
                UPDATE loans
                SET client_name=%s,
                    loan_amount=%s,
                    interest=%s,
                    repayment=%s,
                    phone=%s
                WHERE id=%s
                """,
                (
                    form_loan["name"],
                    form_loan["loan_amount"],
                    form_loan["interest"],
                    form_loan["repayment"],
                    form_loan["phone"],
                    loan_id,
                ),
            )
            flash("Loan updated.", "success")
            return redirect(url_for("dashboard"))
        except ValueError as exc:
            flash(str(exc), "danger")
            loan["client_name"] = request.form.get("name", loan["client_name"])
            loan["phone"] = request.form.get("phone", loan["phone"])
            loan["loan_amount"] = request.form.get("loan", loan["loan_amount"])

    return render_template("edit.html", loan=loan, interest_rate=INTEREST_RATE)


@app.route("/delete/<int:loan_id>", methods=["POST"])
@login_required
def delete(loan_id):
    deleted = execute("DELETE FROM loans WHERE id=%s", (loan_id,))
    if deleted:
        flash("Loan deleted.", "success")
    else:
        flash("Loan was already deleted or could not be found.", "warning")
    return redirect(url_for("dashboard"))


@app.route("/logout", methods=["POST"])
@login_required
def logout():
    session.clear()
    flash("You have been signed out.", "success")
    return redirect(url_for("login"))


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=int(os.environ.get("PORT", "5050")), debug=True)
