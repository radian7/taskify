"""Structural diff of a generated OpenAPI JSON document against a committed YAML contract (T144)."""
import json
import sys

import yaml

METHODS = {"get", "put", "post", "delete", "patch", "head", "options"}


def load_contract(path):
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def resolve(doc, node):
    """Follows a local $ref (one level is enough for parameters)."""
    while isinstance(node, dict) and "$ref" in node:
        target = doc
        for part in node["$ref"].lstrip("#/").split("/"):
            target = target[part]
        node = target
    return node


def describe(doc):
    ops = {}
    for path, item in doc.get("paths", {}).items():
        shared = item.get("parameters", [])
        for method, op in item.items():
            if method not in METHODS:
                continue
            params = set()
            for p in shared + op.get("parameters", []):
                p = resolve(doc, p)
                if p.get("in") in ("path", "query"):
                    params.add((p["in"], p["name"]))
            ops[(method.upper(), path)] = {
                "id": op.get("operationId"),
                "params": params,
                "codes": set(str(c) for c in op.get("responses", {})),
            }
    return ops


def main(generated_path, contract_path, allow_missing=False):
    with open(generated_path, encoding="utf-8") as f:
        generated = describe(json.load(f))
    contract = describe(load_contract(contract_path))
    problems = []

    for key in sorted(generated.keys() - contract.keys()):
        problems.append(f"{key[0]} {key[1]}: in the code, missing from the contract")
    for key in sorted(() if allow_missing else contract.keys() - generated.keys()):
        problems.append(f"{key[0]} {key[1]}: in the contract, missing from the code")
    for key in sorted(generated.keys() & contract.keys()):
        g, c = generated[key], contract[key]
        where = f"{key[0]} {key[1]}"
        if g["id"] != c["id"]:
            problems.append(f"{where}: operationId code={g['id']} contract={c['id']}")
        if g["params"] != c["params"]:
            problems.append(f"{where}: parameters differ, code={sorted(g['params'])} contract={sorted(c['params'])}")
        g2 = {x for x in g["codes"] if x.startswith("2")}
        c2 = {x for x in c["codes"] if x.startswith("2")}
        if g2 != c2:
            problems.append(f"{where}: 2xx codes code={sorted(g2)} contract={sorted(c2)}")
        undocumented = g["codes"] - c["codes"]
        if undocumented:
            problems.append(f"{where}: codes not in the contract: {sorted(undocumented)}")

    if problems:
        print(f"Drift between {generated_path} and {contract_path}:")
        for p in problems:
            print("  - " + p)
        return 1
    print(f"OK {contract_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2], "--allow-missing" in sys.argv[3:]))
