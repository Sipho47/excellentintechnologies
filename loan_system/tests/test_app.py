from decimal import Decimal

import bcrypt
import pytest

import app as loan_app


@pytest.fixture()
def client():
    loan_app.app.config.update(TESTING=True, SECRET_KEY="test-secret")
    with loan_app.app.test_client() as test_client:
        yield test_client


def sign_in(client):
    with client.session_transaction() as session:
        session["admin"] = "admin"
        session["csrf_token"] = "known-token"
    return "known-token"


def dashboard_totals():
    return {
        "available_capital": Decimal("9000.00"),
        "expected_repayments": Decimal("1500.00"),
        "projected_profit": Decimal("500.00"),
        "loan_count": 1,
    }


def test_dashboard_requires_login(client):
    response = client.get("/dashboard")

    assert response.status_code == 302
    assert response.headers["Location"].endswith("/")


def test_post_requires_csrf_token(client):
    with client.session_transaction() as session:
        session["admin"] = "admin"

    response = client.post("/dashboard", data={"name": "Test", "phone": "0712345678", "loan": "100"})

    assert response.status_code == 400


def test_login_accepts_bcrypt_admin(client, monkeypatch):
    password_hash = bcrypt.hashpw(b"pass123", bcrypt.gensalt()).decode()

    monkeypatch.setattr(
        loan_app,
        "query_one",
        lambda sql, params=(): {"username": "admin", "password": password_hash},
    )

    client.get("/")
    with client.session_transaction() as session:
        csrf_token = session["csrf_token"]

    response = client.post(
        "/",
        data={"username": "admin", "password": "pass123", "csrf_token": csrf_token},
    )

    assert response.status_code == 302
    assert response.headers["Location"].endswith("/dashboard")
    with client.session_transaction() as session:
        assert session["admin"] == "admin"


def test_add_loan_calculates_amounts_and_redirects(client, monkeypatch):
    csrf_token = sign_in(client)
    calls = []
    monkeypatch.setattr(loan_app, "execute", lambda sql, params=(): calls.append(params))

    response = client.post(
        "/dashboard",
        data={
            "name": "Nandi Dlamini",
            "phone": "0712345678",
            "loan": "1000",
            "csrf_token": csrf_token,
        },
    )

    assert response.status_code == 302
    assert response.headers["Location"].endswith("/dashboard")
    assert calls == [
        (
            "Nandi Dlamini",
            Decimal("1000.00"),
            Decimal("500.00"),
            Decimal("1500.00"),
            "0712345678",
        )
    ]


def test_invalid_loan_amount_does_not_write(client, monkeypatch):
    csrf_token = sign_in(client)
    calls = []
    monkeypatch.setattr(loan_app, "execute", lambda sql, params=(): calls.append(params))
    monkeypatch.setattr(loan_app, "fetch_loans", lambda search="", page=1, per_page=10: [])
    monkeypatch.setattr(loan_app, "count_loans", lambda search="": 0)
    monkeypatch.setattr(loan_app, "get_dashboard_totals", dashboard_totals)

    response = client.post(
        "/dashboard",
        data={
            "name": "Nandi Dlamini",
            "phone": "0712345678",
            "loan": "-100",
            "csrf_token": csrf_token,
        },
    )

    assert response.status_code == 200
    assert b"Loan amount must be greater than zero." in response.data
    assert calls == []


def test_search_filters_loans(client, monkeypatch):
    sign_in(client)
    captured = {}

    def fake_query_all(sql, params=()):
        captured["sql"] = sql
        captured["params"] = params
        return []

    monkeypatch.setattr(loan_app, "query_all", fake_query_all)
    monkeypatch.setattr(loan_app, "count_loans", lambda search="": 0)
    monkeypatch.setattr(loan_app, "get_dashboard_totals", dashboard_totals)

    response = client.get("/dashboard?q=nandi")

    assert response.status_code == 200
    assert "ILIKE" in captured["sql"]
    assert captured["params"] == ["%nandi%", "%nandi%", 10, 0]


def test_dashboard_renders_inline_edit_controls(client, monkeypatch):
    sign_in(client)
    monkeypatch.setattr(loan_app, "count_loans", lambda search="": 1)
    monkeypatch.setattr(loan_app, "get_dashboard_totals", dashboard_totals)
    monkeypatch.setattr(
        loan_app,
        "fetch_loans",
        lambda search="", page=1, per_page=10: [
            {
                "id": 7,
                "client_name": "Inline Client",
                "loan_amount": Decimal("300.00"),
                "interest": Decimal("150.00"),
                "repayment": Decimal("450.00"),
                "loan_date": "2026-05-19",
                "phone": "0712345678",
            }
        ],
    )

    response = client.get("/dashboard")

    assert response.status_code == 200
    assert b'id="update-loan-7"' in response.data
    assert b"name=\"name\"" in response.data
    assert b"Update</button>" in response.data
    assert b"Read" in response.data
    assert b"Delete" in response.data


def test_pagination_offsets_loans(monkeypatch):
    captured = {}

    def fake_query_all(sql, params=()):
        captured["sql"] = sql
        captured["params"] = params
        return []

    monkeypatch.setattr(loan_app, "query_all", fake_query_all)

    loan_app.fetch_loans("", page=3, per_page=10)

    assert "LIMIT %s OFFSET %s" in captured["sql"]
    assert captured["params"] == [10, 20]


def test_edit_loan_updates_existing_record(client, monkeypatch):
    csrf_token = sign_in(client)
    monkeypatch.setattr(
        loan_app,
        "query_one",
        lambda sql, params=(): {
            "id": 7,
            "client_name": "Old Name",
            "loan_amount": Decimal("300.00"),
            "interest": Decimal("150.00"),
            "repayment": Decimal("450.00"),
            "loan_date": "2026-05-19",
            "phone": "0712345678",
        },
    )
    calls = []
    monkeypatch.setattr(loan_app, "execute", lambda sql, params=(): calls.append(params))

    response = client.post(
        "/edit/7",
        data={
            "name": "New Name",
            "phone": "0798765432",
            "loan": "200",
            "csrf_token": csrf_token,
        },
    )

    assert response.status_code == 302
    assert calls == [
        (
            "New Name",
            Decimal("200.00"),
            Decimal("100.00"),
            Decimal("300.00"),
            "0798765432",
            7,
        )
    ]


def test_read_loan_detail_page(client, monkeypatch):
    sign_in(client)
    monkeypatch.setattr(
        loan_app,
        "fetch_loan",
        lambda loan_id: {
            "id": loan_id,
            "client_name": "Read Client",
            "loan_amount": Decimal("300.00"),
            "interest": Decimal("150.00"),
            "repayment": Decimal("450.00"),
            "loan_date": "2026-05-19",
            "phone": "0712345678",
        },
    )

    response = client.get("/loan/7")

    assert response.status_code == 200
    assert b"Read Loan #7" in response.data
    assert b"Read Client" in response.data


def test_delete_loan_is_post_only(client, monkeypatch):
    csrf_token = sign_in(client)
    calls = []
    monkeypatch.setattr(loan_app, "execute", lambda sql, params=(): calls.append((sql, params)))

    get_response = client.get("/delete/9")
    post_response = client.post("/delete/9", data={"csrf_token": csrf_token})

    assert get_response.status_code == 405
    assert post_response.status_code == 302
    assert calls == [("DELETE FROM loans WHERE id=%s", (9,))]


def test_dashboard_totals_calculation(monkeypatch):
    monkeypatch.setattr(
        loan_app,
        "query_one",
        lambda sql, params=(): {
            "lent_out": Decimal("2500.00"),
            "projected_profit": Decimal("1250.00"),
            "expected_repayments": Decimal("3750.00"),
            "loan_count": 3,
        },
    )

    totals = loan_app.get_dashboard_totals()

    assert totals["available_capital"] == Decimal("7500.00")
    assert totals["projected_capital"] == Decimal("11250.00")
    assert totals["expected_repayments"] == Decimal("3750.00")
    assert totals["loan_count"] == 3
