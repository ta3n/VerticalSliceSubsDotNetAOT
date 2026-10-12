"""Locust load test for the Subscription Management API (run through scripts/locust-test.sh).

Mixed workload, weights ~ 40% plans / 25% cached get / 10% search / 15% create / 10% full lifecycle.
Set SHAPE=step for a step-up ladder (STEP_USERS, STEP_SECONDS, MAX_USERS) instead of a fixed -u/-t run.
"""
import os
import secrets
from locust import FastHttpUser, LoadTestShape, between, task

JSON = {"Content-Type": "application/json"}


def body(n):
    return {"serviceName": f"Locust-{n}", "price": 9900 + n % 100, "startDate": "2026-09-05", "trialEndsAt": "2026-09-12"}


class SubscriptionUser(FastHttpUser):
    wait_time = between(0, 0.1)   # near closed-loop; raise for a realistic think time

    def on_start(self):
        with self.client.post("/subscriptions", json=body(secrets.randbelow(10**6)), headers=JSON, name="POST /subscriptions (setup)") as r:
            self.sub_id = r.json()["id"]

    @task(40)
    def plans(self):
        self.client.get("/plans", name="GET /plans")

    @task(25)
    def get_cached(self):
        self.client.get(f"/subscriptions/{self.sub_id}", name="GET /subscriptions/{id}")

    @task(10)
    def search(self):
        self.client.post("/subscriptions/search", json={"status": "ACTIVE", "limit": 20}, headers=JSON, name="POST /subscriptions/search")

    @task(15)
    def create(self):
        self.client.post("/subscriptions", json=body(secrets.randbelow(10**9)), headers=JSON, name="POST /subscriptions")

    @task(10)
    def lifecycle(self):
        with self.client.post("/subscriptions", json=body(secrets.randbelow(10**9)), headers=JSON, name="lifecycle: create") as r:
            if r.status_code != 201:
                return
            sid = r.json()["id"]
        self.client.post(f"/subscriptions/{sid}/pause", name="lifecycle: pause")
        self.client.post(f"/subscriptions/{sid}/resume", name="lifecycle: resume")
        with self.client.post(f"/subscriptions/{sid}/renew", name="lifecycle: renew") as r:
            inv = r.json().get("invoice", {}).get("id") if r.status_code == 200 else None
        if inv:
            self.client.post(f"/invoices/{inv}/pay", name="lifecycle: pay")
        self.client.delete(f"/subscriptions/{sid}", name="lifecycle: cancel")


if os.environ.get("SHAPE") == "step":
    class StepShape(LoadTestShape):
        step_users = int(os.environ.get("STEP_USERS", 250))
        step_seconds = int(os.environ.get("STEP_SECONDS", 20))
        max_users = int(os.environ.get("MAX_USERS", 4000))

        def tick(self):
            step = int(self.get_run_time() // self.step_seconds) + 1
            users = step * self.step_users
            if users > self.max_users:
                return None
            return users, self.step_users   # spawn the whole step within ~1s
