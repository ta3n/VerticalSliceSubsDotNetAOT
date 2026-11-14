"""Prints the quality gate, key measures and open issues of a SonarQube project (used by scripts/sonar.sh report).

Usage: python3 -I scripts/sonar_report.py <sonar-url> <project-key> <user:password | token:>
(hotspots need a user login: a project analysis token may not browse them)
"""
import base64
import json
import sys
import urllib.parse
import urllib.request

url, key, credentials = sys.argv[1:4]
auth = "Basic " + base64.b64encode(credentials.encode()).decode()


def get(path, **params):
    req = urllib.request.Request(f"{url}{path}?{urllib.parse.urlencode(params)}", headers={"Authorization": auth})
    with urllib.request.urlopen(req) as r:
        return json.load(r)


gate = get("/api/qualitygates/project_status", projectKey=key)["projectStatus"]
print(f"Quality gate: {gate['status']}")
for c in gate.get("conditions", []):
    mark = "  " if c["status"] == "OK" else "✗ "
    print(f"  {mark}{c['metricKey']}: {c.get('actualValue', '-')} ({c['comparator']} {c.get('errorThreshold', '')})")

metrics = "coverage,line_coverage,branch_coverage,lines_to_cover,uncovered_lines,bugs,vulnerabilities,code_smells," \
          "security_hotspots,duplicated_lines_density,ncloc"
measures = {m["metric"]: m.get("value", "-") for m in get("/api/measures/component", component=key, metricKeys=metrics)["component"]["measures"]}
print("\nMeasures: " + ", ".join(f"{k}={measures.get(k, '-')}" for k in metrics.split(",")))

issues, page = [], 1
while True:
    r = get("/api/issues/search", componentKeys=key, issueStatuses="OPEN,CONFIRMED", ps=500, p=page)
    issues += r["issues"]
    if page * 500 >= r["paging"]["total"]:
        break
    page += 1
print(f"\nOpen issues: {len(issues)}")
for i in sorted(issues, key=lambda i: (i["component"], i.get("line", 0))):
    sev = ",".join(f"{x['softwareQuality'][0]}:{x['severity']}" for x in i.get("impacts", []))
    comp = i["component"].split(":", 1)[-1]
    print(f"  {comp}:{i.get('line', '-')}  [{i['rule']}] [{sev}] {i['message']}")

hotspots = get("/api/hotspots/search", project=key, status="TO_REVIEW", ps=500).get("hotspots", [])
print(f"\nSecurity hotspots to review: {len(hotspots)}")
for h in hotspots:
    comp = h["component"].split(":", 1)[-1]
    print(f"  {comp}:{h.get('line', '-')}  [{h['ruleKey']}] [{h['vulnerabilityProbability']}] {h['message']}")

print("\nPer-file coverage (lowest first, files with uncovered lines):")
tree = get("/api/measures/component_tree", component=key, qualifiers="FIL", metricKeys="coverage,uncovered_lines",
           ps=500, s="metric", metricSort="uncovered_lines", asc="false")
for c in tree["components"]:
    m = {x["metric"]: x.get("value") for x in c["measures"]}
    if m.get("uncovered_lines") and float(m["uncovered_lines"]) > 0:
        print(f"  {m.get('coverage', '-'):>6}%  {int(float(m['uncovered_lines'])):>4} uncovered  {c['path']}")
