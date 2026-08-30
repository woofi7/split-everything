#!/usr/bin/env python3
"""
Fills a development database with data that looks like a group in use.

Everything goes through the API rather than into the tables, so the result is
indistinguishable from a group people actually used: activity entries, sync log
rows, vector clocks, categories, exchange rates and balances all come out of the
same code paths the app runs. Inserting rows directly would produce a database
that looks right and a feed that is empty, which is the exact failure this
project already hit once.

Additive and never deletes anything. Re-running it as the same account leaves the
groups it already made alone; it can only see that account's groups, so a group of
the same name owned by someone else is invisible to it and will not stop it
creating another.

    python3 scripts/seed-demo-data.py --email you@example.com
"""

import argparse
import json
import random
import sys
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone

DEFAULT_API = "http://localhost:5080/api"

# Fixed, so a re-run produces the same figures and a screenshot stays comparable.
random.seed(20260901)


class Api:
    """The API as one signed-in person."""

    def __init__(self, base, email, name, device):
        self.base = base.rstrip("/")
        self.device = device
        self.token = None
        self.email = email
        self.name = name

    def request(self, method, path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(f"{self.base}{path}", data=data, method=method)
        request.add_header("Content-Type", "application/json")
        request.add_header("X-Device-Id", self.device)
        if self.token:
            request.add_header("Authorization", f"Bearer {self.token}")

        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")
            raise SystemExit(
                f"{method} {path} failed with {error.code}\n{detail}\n"
                "Is the API running, and is development sign-in enabled?"
            ) from error
        except urllib.error.URLError as error:
            raise SystemExit(f"Could not reach {self.base}: {error.reason}") from error

    def sign_in(self):
        result = self.request(
            "POST", "/auth/dev", {"email": self.email, "displayName": self.name, "deviceId": None}
        )
        self.token = result["tokens"]["accessToken"]
        return result["user"]


def month_start(months_ago):
    now = datetime.now(timezone.utc)
    year = now.year
    month = now.month - months_ago
    while month <= 0:
        month += 12
        year -= 1
    return datetime(year, month, 1, tzinfo=timezone.utc)


def spread(months_ago, day, hour=19):
    """
    A date inside a month, so the buckets in the chart are distinguishable.

    Never in the future: a bucket that has not happened yet reads as a bug in the
    chart rather than as seed data.
    """
    when = month_start(months_ago) + timedelta(days=day - 1, hours=hour)
    now = datetime.now(timezone.utc)
    return min(when, now - timedelta(hours=1)).isoformat()


# Enough variety that the by-category breakdown and the stacked chart both have
# something to show, with amounts that read as real money rather than round numbers.
FLAT_EXPENSES = [
    ("Rent", 1450.00, "housing", 3, 1),
    ("Hydro", 96.42, "utilities", 3, 4),
    ("Groceries at Metro", 184.19, "groceries", 3, 6),
    ("Internet", 79.99, "utilities", 3, 8),
    ("Groceries at IGA", 121.55, "groceries", 3, 15),
    ("Dinner at Schwartz", 68.40, "dining", 3, 21),
    ("Rent", 1450.00, "housing", 2, 1),
    ("Hydro", 88.13, "utilities", 2, 4),
    ("Groceries at Metro", 203.87, "groceries", 2, 5),
    ("Cleaning supplies", 43.28, "other", 2, 9),
    ("Cinema", 34.00, "entertainment", 2, 14),
    ("Groceries at Costco", 288.64, "groceries", 2, 18),
    ("Taxi home", 27.35, "transport", 2, 23),
    ("Rent", 1450.00, "housing", 1, 1),
    ("Hydro", 102.77, "utilities", 1, 4),
    ("Groceries at Metro", 167.02, "groceries", 1, 7),
    ("Internet", 79.99, "utilities", 1, 8),
    ("Brunch", 82.15, "dining", 1, 12),
    ("Pharmacy", 38.90, "health", 1, 16),
    ("Groceries at IGA", 145.33, "groceries", 1, 22),
    ("Rent", 1450.00, "housing", 0, 1),
    ("Groceries at Metro", 176.48, "groceries", 0, 5),
    ("Dinner out", 94.70, "dining", 0, 9),
]

TRIP_EXPENSES = [
    ("Chalet, three nights", 720.00, "travel", 1, 5),
    ("Lift passes", 456.00, "entertainment", 1, 5),
    ("Petrol", 88.25, "transport", 1, 5),
    ("Groceries for the chalet", 162.90, "groceries", 1, 6),
    ("Dinner in the village", 178.40, "dining", 1, 6),
    ("Ski hire", 240.00, "entertainment", 1, 7),
]


def find_group(api, name):
    for group in api.request("GET", "/groups?includeArchived=true") or []:
        if group["name"] == name:
            return group
    return None


def category_ids(api):
    return {c["key"]: c["id"] for c in api.request("GET", "/categories") or []}


def add_expense(api, group_id, payer_id, participants, description, amount, category_id, spent_at):
    api.request(
        "POST",
        "/expenses",
        {
            "groupId": group_id,
            "paidByMemberId": payer_id,
            "description": description,
            "amount": amount,
            "currency": "CAD",
            "spentAt": spent_at,
            "splitType": "Equal",
            "splits": [{"memberId": member, "value": None} for member in participants],
            "categoryId": category_id,
            "items": None,
            "receiptId": None,
            "notes": None,
            "clientId": None,
            "importFingerprint": None,
            "importBatchId": None,
        },
    )


def seed_group(api, name, currency, people, expenses, categories):
    existing = find_group(api, name)
    if existing:
        print(f"  {name}: already there, left alone")
        return None

    group = api.request(
        "POST",
        "/groups",
        {
            "name": name,
            "baseCurrency": currency,
            "iconName": None,
            "description": None,
            "colorHex": None,
            "placeholderMemberNames": people,
        },
    )

    members = [m["id"] for m in group["members"]]
    print(f"  {name}: created with {len(members)} people")

    for index, (description, amount, key, months_ago, day) in enumerate(expenses):
        # Rotated rather than random, so every person pays a fair share of the
        # months and the stacked chart has more than one colour in each bar.
        payer = members[index % len(members)]

        if key not in categories:
            print(f"    unknown category key {key!r}, leaving that expense uncategorised")

        add_expense(
            api, group["id"], payer, members, description, amount,
            categories.get(key), spread(months_ago, day),
        )

    print(f"  {name}: {len(expenses)} expenses added")
    return group


def settle_some(api, group, note, months_ago, day):
    members = [m["id"] for m in group["members"]]
    if len(members) < 2:
        return

    api.request(
        "POST",
        "/settlements",
        {
            "groupId": group["id"],
            "fromMemberId": members[1],
            "toMemberId": members[0],
            "amount": 250.00,
            "currency": group["baseCurrency"],
            "settledAt": spread(months_ago, day, hour=9),
            "note": note,
            "receiptId": None,
            "clientId": None,
        },
    )
    print(f"  {group['name']}: one settlement added")


def comment_on_first_expense(api, group, body):
    expenses = api.request("GET", f"/expenses?groupId={group['id']}&pageSize=1")
    items = expenses.get("items") if isinstance(expenses, dict) else expenses
    if not items:
        return

    expense_id = items[0]["id"]
    api.request("POST", f"/expenses/{expense_id}/comments", {"expenseId": expense_id, "body": body, "parentCommentId": None})
    print(f"  {group['name']}: one comment added")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--email", required=True, help="the account to own the seeded groups")
    parser.add_argument("--name", default="You", help="display name for that account")
    parser.add_argument("--api", default=DEFAULT_API, help=f"API base URL (default {DEFAULT_API})")
    args = parser.parse_args()

    api = Api(args.api, args.email, args.name, "seed-script")
    user = api.sign_in()
    print(f"Signed in as {user['email']}")

    categories = category_ids(api)
    if not categories:
        print("No categories came back; expenses will be uncategorised.", file=sys.stderr)

    flat = seed_group(
        api, "Colocation", "CAD", ["Emma", "Chloe"], FLAT_EXPENSES, categories,
    )
    if flat:
        settle_some(api, flat, "Rent catch-up", months_ago=2, day=26)
        comment_on_first_expense(api, flat, "Split three ways from the joint account.")

    trip = seed_group(
        api, "Ski trip", "CAD", ["Emma", "Luc", "Sarah"], TRIP_EXPENSES, categories,
    )
    if trip:
        comment_on_first_expense(api, trip, "Booked on my card, everyone owes a quarter.")

    print("\nDone. Open the app and pick a group from Change at the top right.")


if __name__ == "__main__":
    main()
