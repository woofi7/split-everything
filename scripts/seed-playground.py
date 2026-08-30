#!/usr/bin/env python3
"""
Fills a development database with enough to test against: several groups, real
accounts for the other people, and expenses spread over months.

Everything goes through the API rather than into the tables, so the result is
indistinguishable from groups people actually used: activity entries, sync log
rows, vector clocks, balances and exchange rates all come out of the code paths
the app runs. Inserting rows directly produces a database that looks right and a
feed that is empty, which is a failure this project already hit once.

The other people are real accounts rather than names with nobody behind them, so
you can sign in as any of them and see the same group from their side. Each gets
its own device id: the server refuses to move one device between accounts, which
is the right rule and would otherwise stop this script on its second person.

Additive. Groups are matched by name, so re-running leaves what it already made
alone and only fills in what is missing.

    python3 scripts/seed-playground.py --email you@example.com --name You
"""

import argparse
import json
import random
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
        self.email = email
        self.name = name
        self.token = None
        self.user = None

    def request(self, method, path, body=None, tolerate=False):
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(f"{self.base}{path}", data=data, method=method)
        request.add_header("Content-Type", "application/json")
        request.add_header("X-Device-Id", self.device)
        if self.token:
            request.add_header("Authorization", f"Bearer {self.token}")

        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            detail = error.read().decode(errors="replace")[:400]
            if tolerate:
                print(f"    skipped: {method} {path} -> {error.code} {detail[:120]}")
                return None
            raise SystemExit(
                f"{method} {path} failed with {error.code}\n{detail}\n"
                "Is the API running, and is development sign-in enabled?"
            ) from error
        except urllib.error.URLError as error:
            raise SystemExit(f"Could not reach {self.base}: {error.reason}") from error

    def sign_in(self):
        result = self.request(
            "POST", "/auth/dev",
            {"email": self.email, "displayName": self.name, "deviceId": None},
        )
        self.token = result["tokens"]["accessToken"]
        self.user = result["user"]
        return self.user


def month_start(months_ago):
    now = datetime.now(timezone.utc)
    year, month = now.year, now.month - months_ago
    while month <= 0:
        month += 12
        year -= 1
    return datetime(year, month, 1, tzinfo=timezone.utc)


def spread(months_ago, day, hour=19):
    """
    A date inside a month, never in the future: a bucket that has not happened yet
    reads as a bug in the chart rather than as seed data.
    """
    when = month_start(months_ago) + timedelta(days=day - 1, hours=hour)
    return min(when, datetime.now(timezone.utc) - timedelta(hours=1)).isoformat()


# (description, amount, months_ago, day, split) where split is None for an equal
# share, or a list of weights in member order for the type named alongside.
FLAT = [
    ("Rent", 1520.00, 3, 1, ("Shares", [2, 1, 1])),
    ("Hydro Quebec", 96.42, 3, 3, None),
    ("Internet", 79.99, 3, 4, None),
    ("Groceries at Metro", 184.19, 3, 6, None),
    ("Dish soap and paper towels", 28.74, 3, 9, None),
    ("Groceries at IGA", 121.55, 3, 14, None),
    ("Dinner at Schwartz", 68.40, 3, 20, None),
    ("Bus pass", 94.00, 3, 25, ("ExactAmount", [94.00, 0, 0])),
    ("Rent", 1520.00, 2, 1, ("Shares", [2, 1, 1])),
    ("Hydro Quebec", 88.13, 2, 3, None),
    ("Internet", 79.99, 2, 4, None),
    ("Groceries at Metro", 203.87, 2, 5, None),
    ("Cleaning supplies", 43.28, 2, 8, None),
    ("Cinema", 34.00, 2, 13, None),
    ("Groceries at Costco", 288.64, 2, 17, ("Percentage", [50, 25, 25])),
    ("Taxi home", 27.35, 2, 22, None),
    ("Plumber", 210.00, 2, 27, None),
    ("Rent", 1520.00, 1, 1, ("Shares", [2, 1, 1])),
    ("Hydro Quebec", 102.77, 1, 3, None),
    ("Internet", 79.99, 1, 4, None),
    ("Groceries at Metro", 167.02, 1, 7, None),
    ("Brunch", 82.15, 1, 11, None),
    ("Pharmacy", 38.90, 1, 15, None),
    ("Groceries at IGA", 145.33, 1, 21, None),
    ("New kettle", 64.99, 1, 24, None),
    ("Window cleaning", 120.00, 1, 28, None),
    ("Rent", 1520.00, 0, 1, ("Shares", [2, 1, 1])),
    ("Hydro Quebec", 91.66, 0, 3, None),
    ("Internet", 79.99, 0, 4, None),
    ("Groceries at Metro", 176.48, 0, 5, None),
    ("Dinner out", 94.70, 0, 8, None),
    ("Light bulbs", 22.15, 0, 10, None),
    ("Groceries at IGA", 132.80, 0, 12, None),
    ("Laundry", 18.50, 0, 14, None),
]

ROAD_TRIP = [
    ("Petrol, Riviere du Loup", 88.25, 1, 8, None),
    ("Motel, two nights", 412.00, 1, 8, None),
    ("Lobster rolls", 96.40, 1, 9, None),
    ("Parc national entry", 68.00, 1, 9, None),
    ("Petrol, Gaspe", 94.10, 1, 10, None),
    ("Groceries for the road", 143.72, 1, 10, None),
    ("Whale watching", 320.00, 1, 11, None),
    ("Cabin, three nights", 675.00, 1, 11, None),
    ("Dinner in Perce", 214.85, 1, 12, None),
    ("Coffee and pastries", 38.60, 1, 12, None),
    ("Petrol home", 91.35, 1, 13, None),
    ("Car wash", 24.00, 1, 13, None),
    ("Souvenirs", 72.40, 1, 14, None),
    ("Tolls", 31.50, 1, 14, None),
]

LUNCHES = [
    ("Lunch at the bagel place", 46.80, 1, 2, None),
    ("Sushi", 82.40, 1, 4, None),
    ("Coffee round", 22.75, 1, 5, None),
    ("Pizza", 68.00, 1, 8, None),
    ("Poke bowls", 74.20, 1, 10, None),
    ("Coffee round", 19.50, 1, 11, None),
    ("Thai", 91.60, 1, 15, None),
    ("Sandwiches", 54.30, 1, 17, None),
    ("Coffee round", 24.00, 1, 18, None),
    ("Burgers", 88.90, 1, 22, None),
    ("Salads", 61.40, 1, 24, None),
    ("Coffee round", 21.25, 1, 25, None),
    ("Ramen", 79.80, 0, 2, None),
    ("Coffee round", 23.50, 0, 3, None),
    ("Tacos", 66.20, 0, 5, None),
    ("Pho", 71.00, 0, 8, None),
    ("Coffee round", 20.75, 0, 9, None),
    ("Shawarma", 58.40, 0, 11, None),
    ("Pizza", 72.00, 0, 12, None),
    ("Coffee round", 25.00, 0, 15, None),
    ("Indian", 96.30, 0, 16, None),
    ("Bagels again", 44.60, 0, 18, None),
    ("Coffee round", 22.00, 0, 19, None),
    ("Korean fried chicken", 104.50, 0, 22, None),
    ("Smoothies", 36.80, 0, 23, None),
    ("Farewell lunch", 168.00, 0, 25, ("Percentage", [40, 15, 15, 15, 15])),
]

SPAIN = [
    ("Flights", 1284.00, 4, 3, ("ExactAmount", [642.00, 642.00])),
    ("Airbnb Madrid, four nights", 486.00, 4, 12, None),
    ("Tapas in La Latina", 74.50, 4, 13, None),
    ("Prado tickets", 32.00, 4, 14, None),
    ("Train to Seville", 118.40, 4, 16, None),
    ("Hotel Seville", 392.00, 4, 16, None),
    ("Flamenco tickets", 68.00, 4, 17, None),
    ("Dinner in Triana", 96.20, 4, 18, None),
    ("Bus to Granada", 44.60, 4, 19, None),
    ("Alhambra tickets", 56.00, 4, 20, None),
    ("Groceries", 38.90, 4, 21, None),
    ("Taxi to the airport", 34.00, 4, 22, None),
]

CHALET = [
    ("Chalet, three nights", 780.00, 2, 14, None),
    ("Lift passes", 456.00, 2, 14, None),
    ("Petrol", 88.25, 2, 14, None),
    ("Groceries for the chalet", 186.90, 2, 15, None),
    ("Firewood", 45.00, 2, 15, None),
    ("Dinner in the village", 178.40, 2, 15, None),
    ("Ski hire", 240.00, 2, 16, ("Shares", [1, 1, 2])),
    ("Raclette and wine", 112.60, 2, 16, None),
    ("Spa", 195.00, 2, 17, None),
]

CAMPING = [
    ("Campsite, two nights", 96.00, 8, 12, None),
    ("Firewood", 28.00, 8, 12, None),
    ("Groceries", 84.30, 8, 12, None),
    ("Ice and drinks", 31.50, 8, 13, None),
    ("Petrol", 62.40, 8, 14, None),
]


def find_group(api, name):
    for group in api.request("GET", "/groups?includeArchived=true") or []:
        if group["name"] == name:
            return group
    return None


def add_expense(api, group, payer, members, row, currency):
    description, amount, months_ago, day, split = row
    split_type, values = split if split else ("Equal", None)

    api.request(
        "POST",
        "/expenses",
        {
            "groupId": group["id"],
            "paidByMemberId": payer,
            "description": description,
            "amount": amount,
            "currency": currency,
            "spentAt": spread(months_ago, day),
            "splitType": split_type,
            "splits": [
                {"memberId": member, "value": None if values is None else values[index]}
                for index, member in enumerate(members)
            ],
            "items": None,
            "receiptId": None,
            "notes": None,
            "clientId": None,
            "importFingerprint": None,
            "importBatchId": None,
        },
        # A split the server will not accept should not take the whole run with it.
        tolerate=True,
    )


def seed_group(api, name, currency, cast, rows, icon=None, colour=None):
    """Returns the group and whether this run is what created it."""
    existing = find_group(api, name)
    if existing:
        print(f"  {name}: already there, left alone")
        return existing, False

    group = api.request("POST", "/groups", {
        "name": name,
        "baseCurrency": currency,
        "iconName": icon,
        "description": None,
        "colorHex": colour,
        "placeholderMemberNames": [],
    })

    # Real accounts, added the way the app adds them.
    for person in cast:
        api.request("POST", f"/groups/{group['id']}/members/user", {"userId": person.user["id"]})

    group = api.request("GET", f"/groups/{group['id']}")
    members = [m["id"] for m in group["members"]]
    print(f"  {name}: created with {len(members)} people, {len(rows)} expenses")

    for index, row in enumerate(rows):
        # Rotated rather than random, so everyone pays a fair share of the months
        # and the stacked chart has more than one colour in every bar.
        add_expense(api, group, members[index % len(members)], members, row, currency)

    return api.request("GET", f"/groups/{group['id']}"), True


def settle(api, group, from_index, to_index, amount, months_ago, day, note):
    members = [m["id"] for m in group["members"]]
    if len(members) <= max(from_index, to_index):
        return

    api.request("POST", "/settlements", {
        "groupId": group["id"],
        "fromMemberId": members[from_index],
        "toMemberId": members[to_index],
        "amount": amount,
        "currency": group["baseCurrency"],
        "settledAt": spread(months_ago, day, hour=9),
        "note": note,
        "receiptId": None,
        "clientId": None,
    }, tolerate=True)


def comment(api, group, body, index=0):
    page = api.request("GET", f"/expenses?groupId={group['id']}&pageSize=5")
    items = page.get("items") if isinstance(page, dict) else page
    if not items or len(items) <= index:
        return

    expense_id = items[index]["id"]
    api.request("POST", f"/expenses/{expense_id}/comments", {
        "expenseId": expense_id, "body": body, "parentCommentId": None,
    }, tolerate=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--email", required=True, help="the account to own the groups")
    parser.add_argument("--name", default="You", help="display name for that account")
    parser.add_argument("--api", default=DEFAULT_API)
    args = parser.parse_args()

    owner = Api(args.api, args.email, args.name, "seed-owner-device")
    user = owner.sign_in()
    print(f"Signed in as {user['email']}\n")

    # Each on its own device id: the server refuses to move one between accounts.
    cast = {}
    for index, (email, name) in enumerate([
        ("emma@test.com", "Emma"),
        ("chloe@test.com", "Chloe"),
        ("luc@test.com", "Luc"),
        ("sarah@test.com", "Sarah"),
        ("marc@test.com", "Marc"),
        ("julie@test.com", "Julie"),
    ]):
        person = Api(args.api, email, name, f"seed-person-{index}")
        person.sign_in()
        cast[name] = person
    print("Cast: " + ", ".join(f"{n} <{p.email}>" for n, p in cast.items()) + "\n")

    flat, fresh = seed_group(owner, "Colocation Mile End", "CAD",
                             [cast["Emma"], cast["Chloe"]], FLAT, icon="house")
    if fresh:
        settle(owner, flat, 1, 0, 400.00, 2, 26, "Rent catch-up")
        settle(owner, flat, 2, 0, 250.00, 1, 27, "Bills")
        comment(owner, flat, "Two shares to one: I have the big room.")

    trip, fresh = seed_group(owner, "Road trip Gaspesie", "CAD",
                             [cast["Luc"], cast["Sarah"], cast["Marc"]], ROAD_TRIP, icon="car")
    if fresh:
        comment(owner, trip, "Everything on my card, we settled at the end.")

    lunches, fresh = seed_group(owner, "Bureau lunches", "CAD",
                                [cast["Emma"], cast["Marc"], cast["Julie"], cast["Sarah"]],
                                LUNCHES, icon="utensils")
    if fresh:
        settle(owner, lunches, 2, 0, 120.00, 0, 20, "Lunch kitty")

    spain, fresh = seed_group(owner, "Voyage Espagne", "EUR", [cast["Emma"]], SPAIN, icon="plane")
    if fresh:
        comment(owner, spain, "Flights split exactly, the rest down the middle.")

    chalet, fresh = seed_group(owner, "Chalet Charlevoix", "CAD",
                               [cast["Luc"], cast["Julie"]], CHALET, icon="mountain-sun")
    if fresh:
        settle(owner, chalet, 1, 0, 300.00, 2, 20, "Sorted on the drive back")

    camping, fresh = seed_group(owner, "Camping 2025", "CAD", [cast["Sarah"]], CAMPING)
    if fresh:
        owner.request("POST", f"/groups/{camping['id']}/archive", tolerate=True)
        print("  Camping 2025: archived, so there is one of those to look at")

    print("\nDone. Sign in as", args.email)
    print("Sign in as any of the cast to see the same groups from their side.")


if __name__ == "__main__":
    main()
