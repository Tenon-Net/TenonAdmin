"""采集检查器的独立夹具；所有内容均为测试构造，不是真实报销单。"""

from contextlib import redirect_stdout
from copy import deepcopy
from decimal import Decimal
import io
import json
from pathlib import Path
import tempfile
import unittest

from check_expense_collection import INPUT_FIELDS, check_collection, load_collection, main
from simulate_expense_collection import create_simulation, main as simulate


TEMPLATE = Path(__file__).resolve().parents[1] / "docs/workflow/expense-ai-decision-real-sample-template.json"
EARLY = "2026-01-01T00:00:00Z"
LATE = "2026-01-02T00:00:00Z"


def collection():
    data = json.loads(TEMPLATE.read_text())
    data["recordKind"] = "collection"
    batch = data["batch"]
    batch.update(datasetId="test-only", datasetVersion="1")
    batch["policy"].update(reference="test-policy", version="1", effectiveFrom="2026-01-01",
                           rulesTableRef="test-rules", confirmedBy="owner", confirmedAtUtc=EARLY)
    batch["dataUse"].update(ownerId="owner", permittedPurpose="local-test", storageRef="test-store",
                            retentionUntil="2026-12-31", permittedProviderProcessing="none")
    batch["sampling"].update(population="test", windowStart="2026-01-01", windowEnd="2026-01-02",
                             method="test", exclusionLogRef="test-log", strata=["ordinary"], targetCount=1)
    batch["splitPlan"].update(groupingMethod="test", seed=1, devProportion=Decimal("0.8"),
                              assignedBy="owner", assignedAtUtc=LATE, exposureLogRef="test-exposure")
    sample = data["samples"][0]
    sample.update(caseId="test-case", dataOrigin="real", leakageGroupId="test-group",
                  split="dev", samplingStratum="ordinary")
    snapshot = sample["snapshot"]
    snapshot.update(availableAtUtc=EARLY, capturedAtUtc=EARLY, sourceRef="test-source",
                    modelInput={"amount": 1000})
    snapshot["fieldProvenance"] = [
        {"field": field, "availability": "present" if field == "amount" else "missing",
         "sourceKind": "applicant", "sourceSystem": "test-system", "sourceField": field,
         "evidenceRefs": ["test-evidence"], "availableAtUtc": EARLY}
        for field in sorted(INPUT_FIELDS)
    ]
    for role in ("reviewerA", "reviewerB"):
        sample["annotations"][role].update(
            reviewerId=role, label="manual", ruleIds=["test-rule"], rationale="缺少事实",
            evidenceRefs=["test-evidence"], labeledAtUtc=LATE,
            modelOutputVisible=False, laterOutcomeVisible=False)
    sample["annotations"]["adjudication"].update(
        status="agreed", label="manual", ruleIds=["test-rule"], rationale="缺少事实",
        evidenceRefs=["test-evidence"], resolvedAtUtc=LATE)
    sample["quality"].update(scoringStatus="eligible")
    sample["quality"]["redaction"].update(status="passed", reviewerId="redactor", reviewedAtUtc=EARLY)
    return data


class ExpenseCollectionTests(unittest.TestCase):
    def codes(self, data):
        return {issue["code"] for issue in check_collection(data)["issues"]}

    def test_valid_collection_is_not_evaluation_authorization(self):
        result = check_collection(collection())
        self.assertTrue(result["checksPassed"], result["issues"])
        self.assertFalse(result["readyForEvaluation"])
        self.assertEqual(result["counts"]["eligible"], 1)

    def test_empty_template_is_not_a_collection(self):
        self.assertIn("collection_required", self.codes(json.loads(TEMPLATE.read_text())))

    def test_double_blind_final_label_and_redaction_requirements(self):
        for path, value, code in [
            (("annotations", "reviewerB", "reviewerId"), "reviewerA", "distinct_reviewers_required"),
            (("annotations", "reviewerA", "modelOutputVisible"), True, "blind_label_required"),
            (("annotations", "reviewerA", "laterOutcomeVisible"), 0, "blind_label_required"),
            (("annotations", "adjudication", "label"), "approve", "agreed_labels_differ"),
            (("annotations", "adjudication", "status"), "disputed", "final_label_unresolved"),
            (("quality", "redaction", "status"), "pending", "redaction_not_passed"),
        ]:
            with self.subTest(code=code):
                data = collection()
                target = data["samples"][0]
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = value
                self.assertIn(code, self.codes(data))

    def test_disagreement_requires_independent_adjudicator(self):
        data = collection()
        annotations = data["samples"][0]["annotations"]
        annotations["reviewerB"]["label"] = "reject"
        annotations["adjudication"].update(status="adjudicated", adjudicatorId="reviewerA")
        self.assertIn("independent_adjudicator_required", self.codes(data))
        annotations["adjudication"]["adjudicatorId"] = "third-person"
        self.assertEqual(self.codes(data), set())

    def test_future_and_unavailable_facts_cannot_enter_inputs(self):
        data = collection()
        snapshot = data["samples"][0]["snapshot"]
        source = next(row for row in snapshot["fieldProvenance"] if row["field"] == "amount")
        source["availableAtUtc"] = LATE
        self.assertIn("future_information", self.codes(data))
        source["availability"] = "after_snapshot"
        self.assertIn("unavailable_field_in_input", self.codes(data))
        snapshot["modelInput"] = {}
        self.assertEqual(self.codes(data), set())
        source["availableAtUtc"] = EARLY
        self.assertIn("after_snapshot_time_order", self.codes(data))

    def test_same_group_cannot_cross_splits_even_if_excluded(self):
        data = collection()
        other = deepcopy(data["samples"][0])
        other.update(caseId="other-case", split="holdout")
        other["quality"].update(scoringStatus="excluded", exclusionReason="test exclusion")
        data["samples"].append(other)
        self.assertIn("cross_split_group", self.codes(data))
        other["split"] = "dev"
        self.assertEqual(self.codes(data), set())
        other["caseId"] = "test-case"
        self.assertIn("duplicate_case", self.codes(data))

    def test_pending_and_excluded_counts_are_not_eligible(self):
        data = collection()
        pending = deepcopy(data["samples"][0])
        pending["caseId"] = "pending-case"
        pending["quality"]["scoringStatus"] = "pending"
        pending["annotations"]["adjudication"].update(status="disputed", label=None)
        excluded = deepcopy(pending)
        excluded["caseId"] = "excluded-case"
        excluded["quality"].update(scoringStatus="excluded", exclusionReason="snapshot missing")
        data["samples"] += [pending, excluded]
        result = check_collection(data)
        self.assertTrue(result["checksPassed"], result["issues"])
        self.assertEqual(result["counts"], {"total": 3, "eligible": 1, "pending": 1, "excluded": 1, "disputed": 2})

    def test_sealed_requires_freeze_metadata(self):
        data = collection()
        data["batch"]["splitPlan"]["holdoutStatus"] = "sealed"
        self.assertIn("sha256_required", self.codes(data))
        self.assertIn("commit_hash_required", self.codes(data))

    def test_json_rejects_duplicate_keys_and_nonstandard_numbers_without_leaks(self):
        for content in ('{"SECRET_SENTINEL":1,"SECRET_SENTINEL":2}', '{"amount":NaN}', '{"amount":Infinity}'):
            with self.subTest(content=content), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "input.json"
                path.write_text(content)
                output = io.StringIO()
                with redirect_stdout(output):
                    code = main([str(path)])
                self.assertEqual(code, 2)
                self.assertNotIn("SECRET_SENTINEL", output.getvalue())
                self.assertFalse(json.loads(output.getvalue())["checksPassed"])

    def test_loader_does_not_round_amounts_or_rewrite_file(self):
        content = '{"amount":1000.0000000000000000000000000001}'
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "input.json"
            path.write_text(content)
            self.assertEqual(load_collection(path)["amount"], Decimal("1000.0000000000000000000000000001"))
            self.assertEqual(path.read_text(), content)

    def test_validation_output_never_echoes_sample_values(self):
        data = collection()
        data["batch"]["splitPlan"]["devProportion"] = 0.8
        sample = data["samples"][0]
        sample.update(caseId="PRIVATE_SENTINEL", leakageGroupId="PRIVATE_SENTINEL")
        sample["snapshot"]["modelInput"]["PRIVATE_SENTINEL"] = "PRIVATE_SENTINEL"
        data["samples"].append(deepcopy(sample))
        data["samples"][1]["split"] = "holdout"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "input.json"
            path.write_text(json.dumps(data))
            before = path.read_bytes()
            output = io.StringIO()
            with redirect_stdout(output):
                code = main([str(path)])
            self.assertEqual(code, 1)
            self.assertNotIn("PRIVATE_SENTINEL", output.getvalue())
            self.assertEqual(path.read_bytes(), before)

    def test_simulation_is_explicit_and_keeps_pending_and_excluded_records(self):
        data = create_simulation()
        result = check_collection(data, simulation=True)
        self.assertTrue(result["checksPassed"], result["issues"])
        self.assertEqual(result["mode"], "simulation")
        self.assertFalse(result["readyForEvaluation"])
        self.assertEqual(result["counts"], {"total": 6, "eligible": 4, "pending": 1, "excluded": 1, "disputed": 1})
        self.assertIn("real_origin_required", self.codes(data))
        result = check_collection(collection(), simulation=True)
        self.assertFalse(result["checksPassed"])
        self.assertIn("synthetic_origin_required", {item["code"] for item in result["issues"]})

    def test_simulation_rejects_mixed_origins_and_still_checks_group_leakage(self):
        for change, code in (("origin", "synthetic_origin_required"), ("group", "cross_split_group")):
            with self.subTest(change=change):
                data = create_simulation()
                if change == "origin":
                    data["samples"][0]["dataOrigin"] = "real"
                else:
                    data["samples"][3]["leakageGroupId"] = data["samples"][0]["leakageGroupId"]
                result = check_collection(data, simulation=True)
                self.assertFalse(result["checksPassed"])
                self.assertIn(code, {item["code"] for item in result["issues"]})

    def test_simulation_cli_generates_and_checks_without_changing_template(self):
        before = TEMPLATE.read_bytes()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "simulation.json"
            with redirect_stdout(io.StringIO()):
                self.assertEqual(simulate(["--output", str(path)]), 0)
            data = load_collection(path)
            self.assertEqual(data["recordKind"], "simulation")
            self.assertTrue(all(row["dataOrigin"] == "synthetic" for row in data["samples"]))
            output = io.StringIO()
            with redirect_stdout(output):
                self.assertEqual(main([str(path), "--simulation"]), 0)
            self.assertEqual(json.loads(output.getvalue())["mode"], "simulation")
            self.assertEqual(TEMPLATE.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
