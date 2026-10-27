"""Load project credentials and launch the installed Codex imagegen CLI.

No API implementation is duplicated here. Image options are passed through to
the skill's image_gen.py. Relative paths always refer to the project root.
"""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import subprocess
import sys
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[2]
MODELS = ('gpt-image-2', 'gpt-image-2.5-flare', 'gpt-image-2.5-sunburst')
FIELDS = {'OPENAI_BASE_URL', 'OPENAI_API_KEY', 'OPENAI_IMAGE_MODEL'}


def read_settings(path: Path) -> dict[str, str]:
    """Parse literal KEY=value lines, without expansion or executing shell code."""
    if not path.exists():
        return {}
    values = {}
    for number, line in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        key, separator, value = line.partition('=')
        key, value = key.strip(), value.strip()
        if not separator or key not in FIELDS:
            raise ValueError(f'Invalid setting at {path.name}:{number}; expected a supported KEY=value.')
        if value.startswith(('"', "'")):
            if len(value) < 2 or value[-1] != value[0]:
                raise ValueError(f'Unclosed quote at {path.name}:{number}.')
            value = value[1:-1]
        values[key] = value
    return values


def load_settings(root: Path = ROOT) -> dict[str, str]:
    settings = read_settings(root / '.env.imagegen.example')
    settings.update(read_settings(root / '.env.imagegen.local'))
    base = settings.get('OPENAI_BASE_URL', '').rstrip('/')
    parsed = urlsplit(base)
    if (parsed.scheme != 'https' or not parsed.hostname or parsed.username
            or parsed.password or parsed.query or parsed.fragment
            or parsed.path.endswith(('/images/generations', '/images/edits'))):
        raise ValueError('OPENAI_BASE_URL must be an HTTPS API base URL without credentials, query, or image endpoint.')
    settings['OPENAI_BASE_URL'] = base
    if settings.get('OPENAI_IMAGE_MODEL') not in MODELS:
        raise ValueError('OPENAI_IMAGE_MODEL must be one of: ' + ', '.join(MODELS))
    return settings


def skill_path() -> Path:
    codex_root = Path(os.environ.get('CODEX_HOME', str(Path.home() / '.codex')))
    return codex_root / 'skills/.system/imagegen/scripts/image_gen.py'


def child_environment(settings: dict[str, str]) -> dict[str, str]:
    env = os.environ.copy()
    # This provider must never receive another OpenAI account's inherited key/IDs.
    for name in ('OPENAI_API_KEY', 'OPENAI_ORG_ID', 'OPENAI_PROJECT_ID', 'OPENAI_WEBHOOK_SECRET'):
        env.pop(name, None)
    env['OPENAI_BASE_URL'] = settings['OPENAI_BASE_URL'] + '/'
    if settings.get('OPENAI_API_KEY'):
        env['OPENAI_API_KEY'] = settings['OPENAI_API_KEY']
    env['PYTHONIOENCODING'] = 'utf-8'
    return env


def has_option(args: list[str], option: str) -> bool:
    return any(arg == option or arg.startswith(option + '=') for arg in args)


def main(argv: list[str] | None = None) -> int:
    args = list(sys.argv[1:] if argv is None else argv)
    if not args or args[0] in ('-h', '--help'):
        print('Usage: imagegen.ps1 status | generate [options] | edit [options] | generate-batch [options]')
        print('Use --out ArtSource/Kairos/GptImage/<name>.png; add --dry-run for an offline preview.')
        print('For all image options: imagegen.ps1 generate --help (or edit --help).')
        return 0
    if args[0] not in ('status', 'generate', 'edit', 'generate-batch'):
        raise ValueError('Unknown command. Use status, generate, edit, or generate-batch.')
    settings = load_settings()
    cli = skill_path()
    if args[0] == 'status':
        print('Base URL: ' + settings['OPENAI_BASE_URL'])
        print('Generate: ' + settings['OPENAI_BASE_URL'] + '/images/generations')
        print('Edit: ' + settings['OPENAI_BASE_URL'] + '/images/edits')
        print('Default model: ' + settings['OPENAI_IMAGE_MODEL'])
        print('Available models: ' + ', '.join(MODELS))
        print('Local API key: ' + ('configured' if settings.get('OPENAI_API_KEY') else 'MISSING (fill .env.imagegen.local)'))
        print('OpenAI SDK: ' + ('installed' if importlib.util.find_spec('openai') else 'MISSING'))
        print('Codex imagegen CLI: ' + ('found' if cli.is_file() else 'MISSING'))
        print('Offline status only; provider access has not been verified.')
        return 0
    if not cli.is_file():
        raise ValueError('Codex imagegen CLI is missing. Install the imagegen skill or set CODEX_HOME to its installation root.')
    help_only = '--help' in args or '-h' in args
    if not help_only and '--dry-run' not in args and not settings.get('OPENAI_API_KEY'):
        raise ValueError('Fill OPENAI_API_KEY in .env.imagegen.local first. Global API keys are intentionally ignored.')
    if not help_only and not (has_option(args, '--out') or has_option(args, '--out-dir')):
        raise ValueError('Choose an output using --out ArtSource/Kairos/GptImage/<name>.png (or --out-dir for batches).')
    command = [sys.executable, str(cli), args[0], '--model', settings['OPENAI_IMAGE_MODEL'], '--no-augment', *args[1:]]
    if not help_only:
        endpoint = 'edits' if args[0] == 'edit' else 'generations'
        print('Endpoint: ' + settings['OPENAI_BASE_URL'] + '/images/' + endpoint, flush=True)
    result = subprocess.run(command, cwd=ROOT, env=child_environment(settings), capture_output=True, encoding='utf-8', errors='replace')
    # Also redact a key if a provider error happens to echo it in the response.
    secret = settings.get('OPENAI_API_KEY')
    for output, stream in ((result.stdout, sys.stdout), (result.stderr, sys.stderr)):
        if secret:
            output = output.replace(secret, '[REDACTED]')
        print(output, end='', file=stream)
    return result.returncode


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, OSError) as error:
        print(f'Error: {error}', file=sys.stderr)
        raise SystemExit(1)
