#!/usr/bin/env python3

import argparse
import collections
import datetime as dt
import json
import os
from pathlib import Path
import random
import subprocess
import uuid


ROOT = Path(__file__).resolve().parents[1]
NAMESPACE = uuid.UUID("f1ef15c6-85a1-4f22-b5d4-c72654d591bb")
DATASET = "dmv-local-demo-v1"
REASON = "Synthetic local DMV demo data"
SCHOOLS = [
    ("Morgan State University", "MD", 23),
    ("University of Maryland, College Park", "MD", 13),
    ("University of Maryland, Baltimore County", "MD", 11),
    ("Towson University", "MD", 8),
    ("Bowie State University", "MD", 7),
    ("Coppin State University", "MD", 5),
    ("Johns Hopkins University", "MD", 5),
    ("University of Maryland Eastern Shore", "MD", 3),
    ("Salisbury University", "MD", 3),
    ("Frostburg State University", "MD", 2),
    ("Montgomery College", "MD", 3),
    ("Community College of Baltimore County", "MD", 3),
    ("Prince George's Community College", "MD", 2),
    ("Howard University", "DC", 10),
    ("George Washington University", "DC", 5),
    ("Georgetown University", "DC", 4),
    ("American University", "DC", 3),
    ("University of the District of Columbia", "DC", 4),
    ("Gallaudet University", "DC", 2),
    ("Catholic University of America", "DC", 2),
    ("George Mason University", "VA", 5),
    ("Virginia Commonwealth University", "VA", 3),
    ("Virginia Tech", "VA", 3),
    ("Hampton University", "VA", 2),
    ("Norfolk State University", "VA", 2),
    ("University of Delaware", "DE", 2),
    ("Delaware State University", "DE", 2),
]
FIRST_NAMES = "Avery Jordan Taylor Casey Morgan Cameron Riley Quinn Parker Drew Alex Sam Jamie Devon Robin Skyler Sage Rowan Emery Reese Kendall Blair Adrian Finley Peyton Dakota Amari Kai Remy Ellis Zion Milan Eden Charlie Micah Simone Maya Amina Imani Zoe Noah Lucas Elijah Ethan Daniel Isaac Gabriel Nathan Caleb Aaron Julian Olivia Sophia Ava Layla Nia Fatima Aisha Priya Nadia Chloe Grace Leah Tolu Kemi Tariq Omar Yusuf Mateo Diego Arjun Anika Sora Hana Min Jae Mei Chen".split()
LAST_NAMES = "Johnson Williams Brown Davis Wilson Moore Taylor Anderson Thomas Jackson White Harris Martin Thompson Garcia Martinez Robinson Clark Lewis Lee Walker Hall Allen Young King Wright Scott Green Adams Baker Nelson Carter Mitchell Perez Roberts Turner Phillips Campbell Parker Evans Edwards Collins Stewart Morris Reed Cook Morgan Bell Murphy Bailey Rivera Cooper Richardson Cox Howard Ward Torres Peterson Gray Ramirez James Watson Brooks Kelly Sanders Price Bennett Wood Barnes Ross Henderson Coleman Jenkins Perry Powell Long Patterson Hughes Flores Washington Butler Simmons Foster Gonzales Bryant Alexander Russell Griffin Okafor Adeyemi Balogun Mensah Diallo Ahmed Khan Ali Patel Shah Nguyen Kim Chen Park Singh Das Santos Cruz Tran Choi".split()
GENDERS = [("man", "Man"), ("woman", "Woman"), ("nonbinary", "Non-binary"), ("self-described", "Self-described"), ("prefer-not-to-answer", "Prefer not to answer")]
LEVELS = [("undergraduate-2y", "Undergraduate university (2 year)"), ("undergraduate-3y", "Undergraduate university (3+ year)"), ("graduate", "Graduate university (Masters, Doctoral, etc)"), ("not-a-student", "I'm not currently a student")]
TRACKS = ["Civic technology", "Health and accessibility", "Climate and sustainability", "Education", "Financial inclusion", "Creative tools"]
SKILLS = ["Python", "JavaScript", "TypeScript", "React", "Java", "C++", "Figma", "SQL", "Swift", "Data analysis", "Product design"]


def identity(value):
    return str(uuid.uuid5(NAMESPACE, value))


def stamp(value):
    return value.isoformat() if value else None


def field(key, kind, label, column=False, required=False, options=None):
    result = {"key": key, "type": kind, "label": label, "required": required, "storage": "column" if column else "responses", "options": [{"value": v, "label": label} for v, label in (options or [])]}
    if column:
        result["column"] = key
    return result


def fixtures(now):
    rng = random.Random(20260924)
    directory = json.loads((ROOT / "src/portaladmin/lib/data/universities/us.json").read_text())["universities"]
    assert {s[0] for s in SCHOOLS} <= {s["name"] for s in directory}
    next_year = now.year + 1
    future = dt.datetime(next_year, 4, 17, 13, tzinfo=dt.timezone.utc)
    previous = (now - dt.timedelta(days=7)).replace(hour=17, minute=0, second=0, microsecond=0)
    events = [
        {"id": identity("main-event"), "slug": "dmv-demo-morganhacks", "name": f"MorganHacks {next_year} (Demo)", "starts_at": stamp(future), "ends_at": stamp(future + dt.timedelta(days=1, hours=5)), "registration_opens_at": stamp(now - dt.timedelta(days=60)), "registration_closes_at": stamp(future - dt.timedelta(days=14)), "capacity": 700, "created_at": stamp(now - dt.timedelta(days=65)), "decisions_announced_at": stamp(now - dt.timedelta(days=14))},
        {"id": identity("past-event"), "slug": "dmv-demo-build-night", "name": "DMV Campus Build Night (Demo)", "starts_at": stamp(previous), "ends_at": stamp(previous + dt.timedelta(hours=5)), "registration_opens_at": stamp(now - dt.timedelta(days=90)), "registration_closes_at": stamp(previous - dt.timedelta(days=1)), "capacity": 250, "created_at": stamp(now - dt.timedelta(days=95)), "decisions_announced_at": stamp(now - dt.timedelta(days=14))},
    ]
    application_fields = [
        dict(field("demo_intro", "section", "Local demo application"), help="Synthetic data for local development. These are not real applicants."),
        *[field(key, kind, label, True, True) for key, kind, label in [
            ("email", "email", "Email"), ("first_name", "shortText", "First name"), ("last_name", "shortText", "Last name"),
            ("age", "number", "Age"), ("phone", "phone", "Phone number"), ("school", "shortText", "School"), ("country", "shortText", "Country of residence")]],
        field("level_of_study", "select", "Current level of study", True, True, LEVELS),
        field("graduation_year", "number", "Expected graduation year", True),
        field("gender", "radio", "What is your gender?", options=GENDERS),
        field("first_time_hacker", "consent", "This is my first hackathon", True),
        field("major", "shortText", "Major or field of study"),
        field("home_region", "select", "Campus region", options=[(s, s) for s in ["Maryland", "Washington, D.C.", "Virginia", "Delaware"]]),
        field("interests", "select", "Project interests", options=[(s, s) for s in TRACKS]),
        field("skills", "checkboxes", "Skills", options=[(s, s) for s in SKILLS]),
        field("motivation", "paragraph", "What would you like to build?"),
        field("shirt_size", "select", "T-shirt size", True, options=[(s, s) for s in ["XS", "S", "M", "L", "XL", "XXL"]]),
        field("dietary_needs", "shortText", "Dietary needs", True),
        field("mlh_coc_agreed_at", "consent", "Demo code of conduct acknowledgement", True, True),
        field("mlh_data_sharing_at", "consent", "Demo data sharing acknowledgement", True, True),
    ]
    survey_fields = [field("workshop", "select", "Which workshop would you join?", options=[(s, s) for s in ["Web development", "Intro to AI", "UI and UX design", "Hardware hacking", "Pitching your project"]]), field("team_size", "number", "Preferred team size"), field("travel", "select", "How will you travel?", options=[(s, s) for s in ["Campus shuttle", "Metro / MARC", "Carpool", "Driving", "Walking"]])]
    feedback_fields = [field("rating", "number", "How was your experience?"), field("favorite", "shortText", "Favorite part of the event"), field("feedback", "paragraph", "What should we improve?")]
    forms, versions = [], []
    for index, event in enumerate(events):
        for kind in ["application", "survey"]:
            form_id = identity(f"form-{index}-{kind}")
            fields = application_fields if kind == "application" else survey_fields if index == 0 else feedback_fields
            forms.append({"id": form_id, "event_id": event["id"], "code": ["dmvappx", "dmvwork", "dmvpast", "dmvfeed"][index * 2 + (kind == "survey")], "name": ("Hacker application" if kind == "application" else "Workshop preferences" if index == 0 else "Event feedback") + " (Demo)", "kind": kind, "closes_at": event["registration_closes_at"] if kind == "application" else stamp(now + dt.timedelta(days=30)) if index == 0 else stamp(now - dt.timedelta(days=1)), "requires_sign_in": kind == "survey", "eligible_statuses": ["accepted", "confirmed"] if kind == "survey" and index == 0 else ["checked_in"] if kind == "survey" else [], "created_at": event["created_at"]})
            versions.append({"id": identity(f"version-{index}-{kind}"), "event_id": event["id"], "form_id": form_id, "version": 1, "status": "published", "fields": fields, "created_at": event["created_at"], "published_at": event["registration_opens_at"]})
    draft_id = identity("form-draft")
    forms.append({"id": draft_id, "event_id": events[0]["id"], "code": "dmvdraf", "name": "Post-event feedback (Demo)", "kind": "survey", "closes_at": None, "requires_sign_in": True, "eligible_statuses": ["checked_in"], "created_at": stamp(now - dt.timedelta(days=2))})
    versions.append({"id": identity("version-draft"), "event_id": events[0]["id"], "form_id": draft_id, "version": 1, "status": "draft", "fields": feedback_fields, "created_at": stamp(now - dt.timedelta(days=2)), "published_at": None})
    distributions = [
        {"incomplete": 132, "submitted": 336, "under_review": 216, "accepted": 180, "confirmed": 180, "waitlisted": 72, "rejected": 48, "declined": 18, "expired": 12, "withdrawn": 6},
        {"incomplete": 15, "confirmed": 15, "checked_in": 225, "waitlisted": 9, "rejected": 15, "declined": 9, "expired": 6, "withdrawn": 6},
    ]
    people, applications, history, notes, submissions = [], [], [], [], []
    for event_index, (event, distribution) in enumerate(zip(events, distributions)):
        statuses = [status for status, count in distribution.items() for _ in range(count)]
        rng.shuffle(statuses)
        for index, status in enumerate(statuses):
            key = f"{event_index}-{index}"
            school, region, _ = rng.choices(SCHOOLS, weights=[s[2] for s in SCHOOLS])[0]
            first, last = rng.choice(FIRST_NAMES), rng.choice(LAST_NAMES)
            email = f"{first.lower()}.{last.lower()}.{event_index}{index:04d}@example.test"
            person_id, application_id = identity(f"person-{key}"), identity(f"application-{key}")
            age_days = rng.choices(range(46), weights=[2 + 9 / (1 + abs(day - 4)) + 8 / (1 + abs(day - 12)) + 6 / (1 + abs(day - 27)) for day in range(46)])[0]
            if status not in ("submitted", "under_review", "incomplete"):
                age_days = max(age_days, 8)
            if event_index:
                age_days = rng.randint(16, 82)
            created = now - dt.timedelta(days=age_days, hours=rng.randrange(19), minutes=rng.randrange(60) + 10)
            submitted = min(created + dt.timedelta(minutes=rng.randrange(20, 260)), now - dt.timedelta(minutes=5)) if status != "incomplete" else None
            reviewed = submitted + dt.timedelta(hours=3) if submitted else None
            decided = submitted + dt.timedelta(days=2) if status in ("accepted", "confirmed", "checked_in", "waitlisted", "rejected", "declined", "expired") else None
            confirmed = decided + dt.timedelta(hours=12) if status in ("confirmed", "checked_in") else None
            declined = decided + dt.timedelta(hours=15) if status == "declined" else None
            checked_in = previous + dt.timedelta(minutes=rng.randrange(1, 150)) if status == "checked_in" else None
            gender = rng.choices(["man", "woman", "nonbinary", "self-described", "prefer-not-to-answer", None], weights=[47, 42, 5, 1, 3, 2])[0]
            level = "undergraduate-2y" if "College" in school and "University" not in school else rng.choices(["undergraduate-3y", "graduate", "not-a-student"], weights=[85, 13, 2])[0]
            major = rng.choice(["Computer Science", "Information Systems", "Computer Engineering", "Cybersecurity", "Data Science", "Design", "Electrical Engineering", "Business", "Mathematics", "Biology"])
            interest = rng.choice(TRACKS)
            responses = {"demo_dataset": DATASET, "major": major, "home_region": {"MD": "Maryland", "DC": "Washington, D.C.", "VA": "Virginia", "DE": "Delaware"}[region], "interests": interest, "skills": rng.sample(SKILLS, rng.randint(1, 4)), "motivation": f"I would like to build a {interest.lower()} project with students from other campuses and learn how to turn a prototype into something useful."}
            if gender:
                responses["gender"] = gender
            people.append({"id": person_id, "kind": "hacker", "email": email, "full_name": f"{first} {last}", "created_at": stamp(created), "updated_at": stamp(created)})
            application = {"id": application_id, "event_id": event["id"], "person_id": person_id, "email": email, "status": status, "form_version": 1, "first_name": first, "last_name": last, "school": school, "level_of_study": level, "graduation_year": now.year + rng.randint(1, 4), "first_time_hacker": rng.choices([True, False, None], weights=[58, 39, 3])[0], "shirt_size": rng.choices(["XS", "S", "M", "L", "XL", "XXL"], weights=[5, 20, 35, 25, 12, 3])[0], "dietary_needs": rng.choices(["None", "Vegetarian", "Vegan", "Halal", "Gluten-free"], weights=[69, 12, 5, 10, 4])[0], "country": "United States", "age": rng.randint(18, 29), "phone": f"+1{rng.choice(['202','410','301','703'])}55501{index % 100:02d}", "mlh_coc_agreed_at": stamp(submitted), "mlh_data_sharing_at": stamp(submitted), "mlh_marketing_opt_in": False, "responses": responses, "started_at": stamp(created), "submitted_at": stamp(submitted), "decided_at": stamp(decided), "rsvp_deadline": stamp(now + dt.timedelta(days=7)) if status == "accepted" else stamp(decided + dt.timedelta(days=2)) if status == "expired" else None, "confirmed_at": stamp(confirmed), "declined_at": stamp(declined), "checked_in_at": stamp(checked_in), "created_at": stamp(created)}
            applications.append(application)
            path = []
            if submitted:
                path.append(("submitted", submitted))
            if status not in ("incomplete", "submitted", "withdrawn"):
                path.append(("under_review", min(reviewed, now - dt.timedelta(minutes=1))))
            if decided:
                path.append((status if status in ("waitlisted", "rejected") else "accepted", decided))
            if confirmed:
                path.append(("confirmed", confirmed))
            if declined:
                path.append(("declined", declined))
            if status == "expired":
                path.append(("expired", decided + dt.timedelta(days=2)))
            if checked_in:
                path.append(("checked_in", checked_in))
            if status == "withdrawn":
                path.append(("withdrawn", submitted + dt.timedelta(days=1)))
            previous_status = "incomplete"
            for step, (to_status, at) in enumerate(path):
                history.append({"id": identity(f"history-{key}-{step}"), "application_id": application_id, "from_status": previous_status, "to_status": to_status, "reason": REASON, "created_at": stamp(at)})
                previous_status = to_status
            assert previous_status == status
            if index % 10 == 0 and submitted:
                notes.append({"id": identity(f"note-{key}"), "application_id": application_id, "body": rng.choice(["[Demo] Interested in teaming up with students from another campus.", "[Demo] Asked about beginner-friendly workshops and mentorship.", "[Demo] Follow up about campus shuttle availability.", "[Demo] Project interests align with the civic technology track."]), "created_at": stamp(min(submitted + dt.timedelta(hours=2), now))})
        eligible = [a for a in applications if a["event_id"] == event["id"] and a["status"] in (["accepted", "confirmed"] if event_index == 0 else ["checked_in"])][:240 if event_index == 0 else 180]
        for application in eligible:
            answers = {"workshop": rng.choice(["Web development", "Intro to AI", "UI and UX design", "Hardware hacking", "Pitching your project"]), "team_size": rng.randint(2, 4), "travel": rng.choice(["Campus shuttle", "Metro / MARC", "Carpool", "Driving", "Walking"])} if event_index == 0 else {"rating": rng.choices([3, 4, 5], weights=[10, 30, 60])[0], "favorite": rng.choice(["Meeting other students", "Workshops", "Mentor support", "Building a working prototype"]), "feedback": rng.choice(["More beginner workshops would be helpful.", "Loved meeting students from nearby campuses.", "The mentors made it easier to get unstuck.", "Would appreciate an earlier shuttle schedule."])}
            at = now - dt.timedelta(days=rng.randint(0, 4) if event_index == 0 else rng.randint(2, 6), minutes=rng.randint(10, 500))
            submissions.append({"id": identity(f"survey-{application['id']}"), "form_id": identity(f"form-{event_index}-survey"), "form_version": 1, "person_id": application["person_id"], "application_id": application["id"], "answers": answers, "submitted_at": stamp(at), "updated_at": stamp(at)})
    return {"events": events, "forms": forms, "form_versions": versions, "people": people, "applications": applications, "status_history": history, "notes": notes, "form_submissions": submissions}


def sql_string(value):
    return "'" + value.replace("'", "''") + "'"


def insert(table, rows, extra=None):
    columns = list(rows[0])
    names = ", ".join(columns)
    source = sql_string(json.dumps(rows, separators=(",", ":")))
    extras = extra or {}
    target = names + (", " + ", ".join(extras) if extras else "")
    selected = ", ".join(f"r.{name}" for name in columns) + (", " + ", ".join(extras.values()) if extras else "")
    return f"INSERT INTO {table} ({target}) SELECT {selected} FROM jsonb_populate_recordset(NULL::{table}, {source}::jsonb) r ON CONFLICT (id) DO NOTHING;"


def main():
    parser = argparse.ArgumentParser(description="Seed synthetic DMV applicants into a loopback-only MorganHacks database. Existing records are preserved; repeat runs do not duplicate fixtures.")
    parser.add_argument("--apply", action="store_true", help="Write the demo fixtures; otherwise print the planned counts.")
    parser.add_argument("--backup-dir", type=Path, help="Required with --apply; pg_dump backup directory outside the repository.")
    parser.add_argument("--postgres-container", help="Use pg_dump from the local Postgres container when its version is newer than the host client.")
    args = parser.parse_args()
    connection = dict(part.split("=", 1) for part in os.environ.get("ARCTIC_DB", "").split(";") if "=" in part)
    env = os.environ.copy()
    for source, target in [("Host", "PGHOST"), ("Port", "PGPORT"), ("Database", "PGDATABASE"), ("Username", "PGUSER"), ("Password", "PGPASSWORD")]:
        if source in connection:
            env[target] = connection[source]
    if env.get("PGHOST") not in ("localhost", "127.0.0.1", "::1") or env.get("PGDATABASE") != "morganhacks":
        parser.error("Set ARCTIC_DB or PGHOST/PGDATABASE explicitly to the local morganhacks database.")
    if env.get("PGHOSTADDR") and env["PGHOSTADDR"] not in ("127.0.0.1", "::1"):
        parser.error("PGHOSTADDR must be loopback.")
    data = fixtures(dt.datetime.now(dt.timezone.utc).replace(microsecond=0))
    print(json.dumps({"target": f"{env['PGHOST']}:{env.get('PGPORT', '5432')}/{env['PGDATABASE']}", "dataset": DATASET, "counts": {name: len(rows) for name, rows in data.items()}, "schools": len(SCHOOLS), "statuses": dict(collections.Counter(row["status"] for row in data["applications"]))}, indent=2), flush=True)
    if not args.apply:
        return
    if not args.backup_dir:
        parser.error("--backup-dir is required with --apply.")
    backup_dir = args.backup_dir.resolve()
    if backup_dir.is_relative_to(ROOT):
        parser.error("Keep database backups outside the repository.")
    backup_dir.mkdir(parents=True, exist_ok=True, mode=0o700)
    backup = backup_dir / f"before-dmv-seed-{dt.datetime.now(dt.timezone.utc):%Y%m%dT%H%M%S%f}.dump"
    if args.postgres_container:
        container = json.loads(subprocess.check_output(["docker", "inspect", args.postgres_container], text=True))[0]
        bindings = container["NetworkSettings"]["Ports"].get("5432/tcp") or []
        if not any(item["HostIp"] in ("127.0.0.1", "::1") and item["HostPort"] == env.get("PGPORT", "5432") for item in bindings):
            parser.error("The Postgres container must publish the selected loopback port.")
        with backup.open("xb") as output:
            subprocess.run(["docker", "exec", "-e", "PGPASSWORD", "-e", "PGUSER", "-e", "PGDATABASE", args.postgres_container, "pg_dump", "--host=127.0.0.1", "--format=custom"], stdout=output, env=env, check=True)
    else:
        subprocess.run(["pg_dump", "--format=custom", "--file", str(backup)], env=env, check=True)
    backup.chmod(0o600)
    actor = "(SELECT id FROM identity.people WHERE kind = 'organizer' AND revoked_at IS NULL ORDER BY created_at LIMIT 1)"
    statements = ["BEGIN;", "SET LOCAL lock_timeout = '5s';", "SET LOCAL statement_timeout = '60s';", "SELECT pg_advisory_xact_lock(826244, 1);", f"SELECT set_config('app.reason', {sql_string(REASON)}, true);"]
    statements += [insert("applications.events", data["events"], {"created_by": actor}), insert("applications.forms", data["forms"], {"created_by": actor}), insert("applications.form_versions", data["form_versions"], {"created_by": actor, "published_by": f"CASE WHEN r.status = 'published' THEN {actor} END"}), insert("identity.people", data["people"]), insert("applications.applications", data["applications"], {"decided_by": f"CASE WHEN r.decided_at IS NOT NULL THEN {actor} END", "checked_in_by": f"CASE WHEN r.checked_in_at IS NOT NULL THEN {actor} END"})]
    statements.append(f"UPDATE applications.status_history h SET to_status = 'incomplete', created_at = a.created_at FROM applications.applications a WHERE h.application_id = a.id AND a.responses ->> 'demo_dataset' = {sql_string(DATASET)} AND h.from_status IS NULL AND h.reason = {sql_string(REASON)};")
    statements += [insert("applications.status_history", data["status_history"]), insert("applications.notes", data["notes"], {"author_id": actor}), insert("applications.form_submissions", data["form_submissions"])]
    statements.append(f"DO $$ BEGIN IF (SELECT count(*) FROM applications.applications WHERE responses ->> 'demo_dataset' = {sql_string(DATASET)}) <> 1500 THEN RAISE EXCEPTION 'Expected 1500 demo applicants'; END IF; END $$;")
    statements += ["COMMIT;", f"SELECT e.name, count(*) AS applicants, count(a.submitted_at) AS submitted, count(DISTINCT a.school) AS schools FROM applications.applications a JOIN applications.events e ON e.id = a.event_id WHERE a.responses ->> 'demo_dataset' = {sql_string(DATASET)} GROUP BY e.name ORDER BY applicants DESC;"]
    subprocess.run(["psql", "-X", "--quiet", "-v", "ON_ERROR_STOP=1", "-P", "pager=off"], input="\n".join(statements), text=True, env=env, check=True)
    print(f"Backup: {backup}")
    print("Local demo data is ready. No mail campaigns or queued messages were created.")


if __name__ == "__main__":
    main()
