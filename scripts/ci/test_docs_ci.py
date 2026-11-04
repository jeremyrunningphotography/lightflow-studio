import json
import os
from pathlib import Path
import subprocess
import shutil
import tempfile
import unittest
from unittest.mock import patch

import docs_ci


class ClassificationTests(unittest.TestCase):
    def test_scope(self):
        for paths, expected in [
            (['docs/AGENT_ASSIGNMENTS.md'], True),
            (['docs/guide.md', 'docs/evidence.zip'], True),
            (['AGENTS.md'], False), (['LightflowStudio/App.cs'], False),
            (['LightflowStudio.Tests/Test.cs'], False),
            (['.github/workflows/ci-release.yml'], False),
            (['dependencies/ffmpeg.json'], False), (['global.json'], False),
            (['scripts/build.ps1'], False),
            (['docs/guide.md', 'App.cs'], False), ([], False),
        ]:
            with self.subTest(paths=paths):
                self.assertEqual(expected, docs_ci.docs_only(paths))

    def test_statuses(self):
        for status in ['A', 'M', 'D', 'T']:
            self.assertTrue(docs_ci.docs_only(docs_ci.parse_changes(
                f'{status}\0docs/a.md\0'.encode())))
        for status in ['R100', 'C099']:
            self.assertTrue(docs_ci.docs_only(docs_ci.parse_changes(
                f'{status}\0docs/old.md\0docs/new.md\0'.encode())))
            for old, new in [('App.cs', 'docs/a.md'), ('docs/a.md', 'App.cs')]:
                self.assertFalse(docs_ci.docs_only(docs_ci.parse_changes(
                    f'{status}\0{old}\0{new}\0'.encode())))

    def test_malformed_discovery(self):
        for raw in [b'M\0docs/a', b'X\0docs/a\0', b'R100\0docs/a\0',
                    b'M\0\0', b'M\0docs/../App.cs\0', b'M\0/docs/a\0',
                    b'M\0docs\\a\0', b'M\0docs/\xff\0']:
            with self.subTest(raw=raw), self.assertRaises((ValueError, IndexError, UnicodeError)):
                docs_ci.parse_changes(raw)

    def test_event_ranges(self):
        base, head, merge = 'a' * 40, 'b' * 40, 'c' * 40
        with patch.object(docs_ci, 'git', side_effect=[merge.encode(), b'M\0docs/a.md\0']) as git:
            self.assertTrue(docs_ci.docs_only(docs_ci.discover(
                {'pull_request': {'base': {'sha': base}, 'head': {'sha': head}}}, 'pull_request')[2]))
            self.assertEqual(('merge-base', base, head), git.call_args_list[0].args)
            self.assertIn(merge, git.call_args_list[1].args)
        with patch.object(docs_ci, 'git', return_value=b'D\0docs/a.md\0') as git:
            docs_ci.discover({'ref': 'refs/heads/main', 'before': base, 'after': head}, 'push')
            self.assertIn(base, git.call_args.args)
            self.assertIn(head, git.call_args.args)
        for event, name in [({}, 'workflow_dispatch'), ({'ref': 'refs/tags/v1'}, 'push'),
                            ({'ref': 'refs/heads/main', 'before': '0' * 40, 'after': head}, 'push'),
                            ({}, 'pull_request')]:
            with self.assertRaises((ValueError, KeyError)):
                docs_ci.discover(event, name)

    def test_discovery_errors_emit_full(self):
        with tempfile.TemporaryDirectory() as directory:
            event, output = Path(directory) / 'event.json', Path(directory) / 'output'
            event.write_text('{}')
            for error in [ValueError('ambiguous'), subprocess.CalledProcessError(1, 'git'), OSError('missing')]:
                with patch.dict(os.environ, GITHUB_EVENT_PATH=str(event), GITHUB_EVENT_NAME='pull_request', GITHUB_OUTPUT=str(output)), patch('sys.argv', ['docs_ci.py']), patch.object(docs_ci, 'discover', side_effect=error):
                    docs_ci.main()
                self.assertEqual('docs_only=false', output.read_text().splitlines()[-1])


class ValidationTests(unittest.TestCase):
    def test_registry_and_references(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'docs').mkdir()
            registry = root / 'docs/AGENT_ASSIGNMENTS.md'
            header = '| Number | Agent ID | Short description | GitHub references | Assignment status |\n| --- | --- | --- | --- | --- |\n'
            row = '| 034 | LF-WIN-DEV-034 | CI | #475 | Active |\n'
            registry.write_text(header + row)
            docs_ci.validate_registry(root)
            registry.write_text(header + row + '| 034 | malformed |\n')
            with self.assertRaisesRegex(ValueError, 'Malformed'):
                docs_ci.validate_registry(root)
            registry.write_text(header + row + row)
            with self.assertRaisesRegex(ValueError, 'Duplicate'):
                docs_ci.validate_registry(root)
            registry.write_text(header + row.replace('LF-WIN-DEV-034', 'LF-WIN-DEV-035'))
            with self.assertRaisesRegex(ValueError, 'identity'):
                docs_ci.validate_registry(root)
            registry.write_text(header + row)
            guide = root / 'docs/guide.md'
            guide.write_text('[registry](AGENT_ASSIGNMENTS.md)\n')
            with patch.object(docs_ci.subprocess, 'run'):
                docs_ci.validate(root, 'a', 'b', ['docs/guide.md', 'docs/deleted.md'])
                guide.write_text('[broken](missing.md)\n')
                with self.assertRaisesRegex(ValueError, 'Broken local'):
                    docs_ci.validate(root, 'a', 'b', ['docs/guide.md'])
                guide.write_text('```python\nunclosed\n')
                with self.assertRaisesRegex(ValueError, 'Unclosed'):
                    docs_ci.validate(root, 'a', 'b', ['docs/guide.md'])
                with self.assertRaisesRegex(ValueError, 'exclusively'):
                    docs_ci.validate(root, 'a', 'b', ['AGENTS.md'])

    def test_real_git_add_delete_rename_copy(self):
        with tempfile.TemporaryDirectory() as directory:
            def git(*args):
                return subprocess.check_output(['git', '-C', directory, *args])
            git('init', '-q')
            git('config', 'user.email', 'ci@example.invalid')
            git('config', 'user.name', 'CI fixture')
            root = Path(directory)
            (root / 'docs').mkdir()
            for name in ['old.md', 'delete.md', 'source.md']:
                (root / 'docs' / name).write_text(name * 100)
            git('add', '.')
            git('commit', '-qm', 'base')
            base = git('rev-parse', 'HEAD').decode().strip()
            (root / 'docs/old.md').rename(root / 'docs/new.md')
            (root / 'docs/delete.md').unlink()
            (root / 'docs/copy.md').write_bytes((root / 'docs/source.md').read_bytes())
            (root / 'docs/added.md').write_text('Added\n')
            git('add', '.')
            git('commit', '-qm', 'docs')
            raw = git('diff', '--name-status', '-z', '-M', '-C', '--find-copies-harder', base, 'HEAD')
            for status in [b'A\0', b'D\0', b'R100\0', b'C100\0']:
                self.assertIn(status, raw)
            self.assertTrue(docs_ci.docs_only(docs_ci.parse_changes(raw)))
            (root / 'docs/new.md').rename(root / 'App.cs')
            git('add', '.')
            git('commit', '-qm', 'move code')
            self.assertFalse(docs_ci.docs_only(docs_ci.parse_changes(git('diff', '--name-status', '-z', '-M', base, 'HEAD'))))


class WorkflowTests(unittest.TestCase):
    def test_required_gate_shells(self):
        import re
        bash = shutil.which('bash') or r'C:\Program Files\Git\bin\bash.exe'
        workflow = (Path(__file__).resolve().parents[2] / '.github/workflows/ci-release.yml').read_text()
        for job in ['test', 'package']:
            section = re.split(r'\n  [a-z][a-z-]*:\n', workflow.split(f'\n  {job}:\n', 1)[1])[0]
            script = section.split('        run: |\n')[1]
            script = '\n'.join(line[10:] for line in script.splitlines() if line.startswith('          '))
            cases = [
                ('true', 'success', 'skipped', 'success', 'success', True),
                ('true', 'failure', 'skipped', 'success', 'success', False),
                ('true', 'skipped', 'skipped', 'success', 'success', False),
                ('false', 'skipped', 'success', 'success', 'success', True),
                ('', 'skipped', 'success', 'success', 'success', True),
                ('false', 'success', 'failure', 'success', 'success', False),
                ('false', 'skipped', 'skipped', 'success', 'success', False),
                ('false', 'skipped', 'cancelled', 'success', 'success', False),
                ('true', 'success', 'skipped', 'failure', 'success', False),
            ]
            if job == 'package':
                cases.append(('true', 'success', 'skipped', 'success', 'failure', False))
            for docs, doc_result, full_result, changes, tests, expected in cases:
                env = dict(os.environ, DOCS_ONLY=docs, DOCS_RESULT=doc_result,
                           FULL_RESULT=full_result, CHANGES_RESULT=changes, TEST_RESULT=tests)
                with self.subTest(job=job, case=(docs, doc_result, full_result, changes, tests)):
                    self.assertEqual(expected, subprocess.run([bash, '-e', '-c', script], env=env).returncode == 0)

    def test_heavy_job_routing(self):
        workflow = (Path(__file__).resolve().parents[2] / '.github/workflows/ci-release.yml').read_text()
        for job in ['full-test', 'neutral', 'mac-storage']:
            self.assertIn(f"  {job}:\n    needs: changes\n    if: always() && !cancelled() && needs.changes.outputs.docs_only != 'true'", workflow)
        self.assertIn("    if: always() && !cancelled() && needs.test.result == 'success' && needs.test.outputs.docs_only != 'true'", workflow)
        self.assertIn("    if: always() && !cancelled() && startsWith(github.ref, 'refs/tags/v') && needs.package.result == 'success'", workflow)
        self.assertIn('    needs: test', workflow)
        self.assertIn('      docs_only: ${{ needs.changes.outputs.docs_only }}', workflow)
        self.assertNotIn('paths-ignore:', workflow)
        self.assertNotIn('    paths:', workflow)
        for job in ['test', 'package']:
            section = workflow.split(f'\n  {job}:\n', 1)[1]
            self.assertIn('    if: always()', section.split('    steps:', 1)[0])


if __name__ == '__main__':
    unittest.main()
