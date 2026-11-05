"""Conservative docs routing and inexpensive validation; Python standard library only."""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
from urllib.parse import unquote, urlsplit


def git(*args):
    return subprocess.check_output(['git', *args])


def parse_changes(raw):
    fields = raw.decode('utf-8', errors='strict').split('\0')
    if not fields or fields.pop() != '':
        raise ValueError('Missing NUL terminator')
    paths = []
    while fields:
        status = fields.pop(0)
        if not re.fullmatch(r'[AMDT]|[RC]\d{1,3}', status):
            raise ValueError('Unknown change status')
        count = 2 if status[0] in 'RC' else 1
        for _ in range(count):
            path = fields.pop(0)
            if not path or path.startswith('/') or '\\' in path or any(
                part in ('', '.', '..') for part in path.split('/')
            ):
                raise ValueError('Ambiguous path')
            paths.append(path)
    return paths


def docs_only(paths):
    return bool(paths) and all(p.startswith('docs/') for p in paths)


def discover(event, name):
    if name == 'pull_request':
        base = event['pull_request']['base']['sha']
        head = event['pull_request']['head']['sha']
        # Compare the complete PR, not just its latest commit.
        base = git('merge-base', base, head).decode().strip()
    elif name == 'push' and event.get('ref') == 'refs/heads/main':
        base, head = event['before'], event['after']
    else:
        raise ValueError('Tags, manual runs and unknown events require full CI')
    if not all(re.fullmatch(r'[0-9a-f]{40}', s) and s != '0' * 40 for s in (base, head)):
        raise ValueError('Missing or invalid commit')
    raw = git('diff', '--name-status', '-z', '-M', '-C', '--find-copies-harder', base, head, '--')
    return base, head, parse_changes(raw)


def validate_registry(root):
    text = (root / 'docs/AGENT_ASSIGNMENTS.md').read_text(encoding='utf-8')
    header = '| Number | Agent ID | Short description | GitHub references | Assignment status |'
    if text.count(header) != 1:
        raise ValueError('Missing assignment table')
    table = text.split(header, 1)[1].strip().split('\n\n', 1)[0].splitlines()
    if not table or table.pop(0) != '| --- | --- | --- | --- | --- |':
        raise ValueError('Invalid assignment table separator')
    rows = []
    for line in table:
        match = re.fullmatch(r'\| (\d{3,}) \| ([^|]+) \| ([^|]+) \| ([^|]+) \| ([^|]+) \|', line)
        if not match:
            raise ValueError('Malformed assignment row: ' + line)
        rows.append(match.groups())
    if not rows:
        raise ValueError('Empty assignment table')
    numbers = []
    for number, identity, *_ in rows:
        if not re.fullmatch(r'LF-(WIN|MAC|ANY|BOTH)-(DEV|RES|DOC|TST|OPS|REV|ADM)-' + number + r'(; LF-(WIN|MAC|ANY|BOTH)-(DEV|RES|DOC|TST|OPS|REV|ADM)-' + number + r' companion)?', identity):
            raise ValueError('Assignment identity does not match its number: ' + identity)
        numbers.append(int(number))
    if len(numbers) != len(set(numbers)):
        raise ValueError('Duplicate assignment number')


def validate(root, base, head, paths):
    if not docs_only(paths):
        raise ValueError('Documentation validation requires exclusively docs/** changes')
    subprocess.run(['git', '-c', 'core.whitespace=blank-at-eol,blank-at-eof', 'diff', '--check', base, head, '--'], check=True)
    validate_registry(root)
    for path in set(paths):
        file = root / path
        if not file.is_file() or file.suffix.lower() != '.md':
            continue  # Deleted documents and binary evidence are valid docs changes.
        text = file.read_text(encoding='utf-8-sig')
        if re.search(r'^(<<<<<<< |=======\s*$|>>>>>>> )', text, re.M):
            raise ValueError('Merge conflict marker: ' + path)
        fence = None
        for line in text.splitlines():
            marker = re.match(r'^ {0,3}(`{3,}|~{3,})(.*)$', line)
            if marker:
                token, rest = marker.groups()
                if fence is None:
                    fence = token
                elif token[0] == fence[0] and len(token) >= len(fence) and not rest.strip():
                    fence = None
        if fence:
            raise ValueError('Unclosed Markdown code fence: ' + path)
        # Inspect inline Markdown links/images outside fenced and inline code.
        prose = re.sub(r'^(`{3,}|~{3,}).*?^\1\s*$', '', text, flags=re.M | re.S)
        prose = re.sub(r'`[^`\n]+`', '', prose)
        for target in re.findall(r'!?\[[^\]\n]*\]\((<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)', prose):
            url = urlsplit(target.strip('<>'))
            if url.scheme or url.netloc or not url.path:
                continue
            destination = root / unquote(url.path).lstrip('/') if url.path.startswith('/') else file.parent / unquote(url.path)
            if not destination.exists():
                raise ValueError(f'Broken local reference in {path}: {target}')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--validate', action='store_true')
    args = parser.parse_args()
    try:
        event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text(encoding='utf-8'))
        base, head, paths = discover(event, os.environ['GITHUB_EVENT_NAME'])
        docs = docs_only(paths)
    except (KeyError, ValueError, IndexError, UnicodeError, OSError, subprocess.CalledProcessError) as error:
        print(f'Full CI: changed-file discovery unavailable: {error}')
        docs = False
    if args.validate:
        if not docs:
            raise ValueError('Cannot validate an unproven docs-only change')
        validate(Path.cwd(), base, head, paths)
        print('Documentation validation passed')
    else:
        with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
            output.write(f'docs_only={str(docs).lower()}\n')
        print('Documentation-only' if docs else 'Full CI')


if __name__ == '__main__':
    main()
