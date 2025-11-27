#!/usr/bin/env python3
import os
import sys
import json
import re
from pathlib import Path

ROOT = Path.cwd()
OUT = ROOT / ".ci-generated"
OUT.mkdir(exist_ok=True, parents=True)

# try to locate templates relative to this script
THIS = Path(__file__).resolve()
if (THIS.parents[1] / "templates").exists():
    TEMPLATES = THIS.parents[1] / "templates"
elif (THIS.parents[2] / "templates").exists():
    TEMPLATES = THIS.parents[2] / "templates"
else:
    TEMPLATES = THIS.parent / "templates"  # fallback

def detect_language(root: Path):
    """
    Надёжно определяем язык/фреймворк по наличию файлов.
    Возвращаем dict: language in ('java','go','node','python','unknown') и details.
    """
    files = {p.name for p in root.glob("*") if p.is_file()}
    res = {"language": None, "details": {}, "found_files": sorted(list(files))}
    if (root / "pom.xml").exists() or (root / "build.gradle").exists() or (root / "build.gradle.kts").exists():
        res["language"] = "java"
        if (root / "pom.xml").exists():
            try:
                content = (root / "pom.xml").read_text(encoding="utf-8", errors="ignore")
                m = re.search(r"<maven\.compiler\.source>([^<]+)</maven\.compiler\.source>", content)
                if not m:
                    m = re.search(r"<java\.version>([^<]+)</java\.version>", content)
                res["details"]["java_version"] = m.group(1) if m else None
            except Exception:
                res["details"]["java_version"] = None
    elif (root / "go.mod").exists():
        res["language"] = "go"
        try:
            first = (root / "go.mod").read_text().splitlines()[0]
            res["details"]["go_mod_first"] = first
        except Exception:
            res["details"]["go_mod_first"] = None
    elif (root / "package.json").exists():
        res["language"] = "node"
        try:
            pkg = json.loads((root / "package.json").read_text(encoding="utf-8", errors="ignore"))
            res["details"]["name"] = pkg.get("name")
            res["details"]["scripts"] = pkg.get("scripts", {})
            res["details"]["engines"] = pkg.get("engines")
        except Exception:
            res["details"].update({"name": None, "scripts": {}, "engines": None})
    elif any((root / f).exists() for f in ("pyproject.toml", "requirements.txt", "setup.py")):
        res["language"] = "python"
        # try to get python version from pyproject if exists
        if (root / "pyproject.toml").exists():
            try:
                txt = (root / "pyproject.toml").read_text(encoding="utf-8", errors="ignore")
                m = re.search(r"python\s*=\s*\"([^\"]+)\"", txt)
                res["details"]["python_requires"] = m.group(1) if m else None
            except Exception:
                res["details"]["python_requires"] = None
    else:
        res["language"] = "unknown"
    return res

def choose_templates(lang):
    # Map language to template paths and default commands
    map_ = {
        "java": {
            "docker": TEMPLATES / "docker" / "Dockerfile.java",
            "jenkins": TEMPLATES / "jenkins" / "Jenkinsfile.java",
            "gitlab": TEMPLATES / "gitlab" / ".gitlab-ci.java.yml",
            "build_cmd": "mvn -B -DskipTests=false clean package",
            "test_cmd": "mvn test",
            "cache": "~/.m2/repository"
        },
        "go": {
            "docker": TEMPLATES / "docker" / "Dockerfile.go",
            "jenkins": TEMPLATES / "jenkins" / "Jenkinsfile.go",
            "gitlab": TEMPLATES / "gitlab" / ".gitlab-ci.go.yml",
            "build_cmd": "go build -v ./...",
            "test_cmd": "go test ./...",
            "cache": "$GOMODCACHE"
        },
        "node": {
            "docker": TEMPLATES / "docker" / "Dockerfile.node",
            "jenkins": TEMPLATES / "jenkins" / "Jenkinsfile.node",
            "gitlab": TEMPLATES / "gitlab" / ".gitlab-ci.node.yml",
            "build_cmd": "npm ci && npm run build || true",
            "test_cmd": "npm test",
            "cache": "~/.npm"
        },
        "python": {
            "docker": TEMPLATES / "docker" / "Dockerfile.python",
            "jenkins": TEMPLATES / "jenkins" / "Jenkinsfile.python",
            "gitlab": TEMPLATES / "gitlab" / ".gitlab-ci.python.yml",
            "build_cmd": "python -m pip install -r requirements.txt --user",
            "test_cmd": "pytest -q",
            "cache": "~/.cache/pip"
        },
    }
    return map_.get(lang)

def render_template(path: Path, vars: dict):
    if not path or not path.exists():
        return None
    s = path.read_text(encoding="utf-8", errors="ignore")
    for k, v in vars.items():
        s = s.replace("{{"+k+"}}", str(v))
    return s

def main():
    print("Анализ проекта в:", ROOT)
    info = detect_language(ROOT)
    print("Найдено:", info["language"])
    tpl = choose_templates(info["language"])
    report_lines = []
    report_lines.append(f"language: {info['language']}")
    report_lines.append(f"found_files: {info.get('found_files', [])}")
    if not tpl:
        report_lines.append("No built-in template for detected language. Provide justification to add support.")
        tpl = {
            "docker": TEMPLATES / "docker" / "Dockerfile.generic",
            "jenkins": TEMPLATES / "jenkins" / "Jenkinsfile.generic",
            "gitlab": TEMPLATES / "gitlab" / ".gitlab-ci.generic.yml",
            "build_cmd": "echo \"No build configured\"",
            "test_cmd": "echo \"No tests configured\"",
            "cache": ""
        }

    # Переменные для рендеринга шаблонов
    project_name = info.get("details", {}).get("name") or "local-project"
    vars = {
        "BUILD_CMD": tpl.get("build_cmd", ""),
        "TEST_CMD": tpl.get("test_cmd", ""),
        "CACHE_DIR": tpl.get("cache", ""),
        "PROJECT_NAME": project_name,
        "IMAGE_NAME": (os.environ.get("CI_REGISTRY_IMAGE") or f"{project_name}:latest"),
        "SONAR_HOST": os.environ.get("SONAR_HOST", "http://sonarqube:9000"),
        "NEXUS_URL": os.environ.get("NEXUS_URL", "http://nexus:8081"),
        **info.get("details", {})  # Добавляем все детали из анализа (java_version, scripts и т.д.)
    }

    outputs = {
        "docker": OUT / "Dockerfile",
        "jenkins": OUT / "Jenkinsfile",
        "gitlab": OUT / ".gitlab-ci.yml"
    }

    for key, out_path in outputs.items():
        tpl_path = tpl.get(key)
        content = render_template(tpl_path, {
            "BUILD_CMD": vars["BUILD_CMD"],
            "TEST_CMD": vars["TEST_CMD"],
            "CACHE_DIR": vars["CACHE_DIR"],
            "IMAGE_NAME": vars["IMAGE_NAME"],
            "SONAR_HOST": vars["SONAR_HOST"],
            "NEXUS_URL": vars["NEXUS_URL"],
        })
        if content is None:
            report_lines.append(f"Template for {key} not found: {tpl_path}")
        else:
            out_path.write_text(content, encoding="utf-8")
            report_lines.append(f"Wrote {out_path}")

    (OUT / "report.txt").write_text("\n".join(report_lines), encoding="utf-8")
    print("Генерация завершена. Файлы в .ci-generated/")
    print((OUT / "report.txt").read_text(encoding="utf-8"))

if __name__ == "__main__":
    main()
